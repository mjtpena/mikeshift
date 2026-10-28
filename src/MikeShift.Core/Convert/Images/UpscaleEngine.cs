using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MikeShift.Core.Feedback;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Windows.Graphics.Imaging;

namespace MikeShift.Core.Images;

/// <summary>
/// Makes a picture twice as large in each direction. The one place the converter meets an upscaling engine, so everything around it (naming,
/// the size checks, orientation, transparency, cancellation, wording) is tested with a stand-in that needs no NPU. A second engine (a bundled
/// model for PCs without an NPU, issue #15) would be another implementation behind the same tile and the same checks.
/// </summary>
internal interface IUpscaleEngine
{
    /// <summary>
    /// The pixels of <paramref name="source"/> (opaque, upright) at exactly twice the width and height. Throws <see cref="ConversionException"/>
    /// with a user-facing message when the engine cannot run, and <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    Task<Raster> ScaleAsync(Raster source, CancellationToken ct);
}

/// <summary>What Windows says about the image scaler on this PC (its <c>AIFeatureReadyState</c>, without the dependency in the signature).</summary>
internal enum UpscaleReadiness
{
    /// <summary>Not supported here, or the answer is one this code does not know.</summary>
    NotSupported,
    /// <summary>The model is installed.</summary>
    Ready,
    /// <summary>Supported, but Windows has not installed the model yet.</summary>
    NotReady,
    /// <summary>The user turned the feature off.</summary>
    DisabledByUser,
}

/// <summary>
/// The Windows AI image scaler (<c>Microsoft.Windows.AI.Imaging.ImageScaler</c>): runs on the NPU of a Copilot+ PC, offline. Windows installs
/// the model itself through Windows Update the first time it is needed; MikeShift opens no connection and the picture never leaves the PC.
/// The component needs package identity (the <c>systemAIModels</c> capability in the manifest), so an unpackaged run reports it as unavailable.
/// </summary>
internal sealed class WindowsUpscaleEngine : IUpscaleEngine
{
    public static WindowsUpscaleEngine Instance { get; } = new();

    /// <summary>
    /// The value for <see cref="Capabilities.AiUpscale"/>: true when the ready state is <c>Ready</c>, or <c>NotReady</c> (this PC supports the
    /// scaler and Windows just has not installed the model yet, which the first run asks for). False for <c>DisabledByUser</c>,
    /// <c>NotSupportedOnCurrentSystem</c>, and when the component cannot load at all. Never throws.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            return IsShown(Readiness());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The ready-state rule in one place, so a test can pin it without the API.</summary>
    internal static bool IsShown(UpscaleReadiness state) => state is UpscaleReadiness.Ready or UpscaleReadiness.NotReady;

    // Its own method: if the component's assemblies are missing, the JIT fails here, inside the callers' try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UpscaleReadiness Readiness() => ImageScaler.GetReadyState() switch
    {
        AIFeatureReadyState.Ready => UpscaleReadiness.Ready,
        AIFeatureReadyState.NotReady => UpscaleReadiness.NotReady,
        AIFeatureReadyState.DisabledByUser => UpscaleReadiness.DisabledByUser,
        _ => UpscaleReadiness.NotSupported,
    };

    public async Task<Raster> ScaleAsync(Raster source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await EnsureModelAsync(ct).ConfigureAwait(false);
            // The scaler is synchronous and has no cancellation: run it off the caller's thread, and honour a cancel as soon as it returns.
            var scaled = await Task.Run(() => Scale(source), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return scaled;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or OutOfMemoryException
                                       or NotSupportedException or FileNotFoundException or TypeLoadException or DllNotFoundException
                                       or UnauthorizedAccessException or BadImageFormatException)
        {
            throw new ConversionException(UpscaleCopy.EngineFailed, ex);
        }
    }

    /// <summary>
    /// Asks Windows to install the model when it is not there yet (<c>EnsureReadyAsync</c>) and waits for it, so the first Upscale 2x on a PC
    /// takes as long as that download takes. The job can be cancelled meanwhile. Later runs see <c>Ready</c> and skip this.
    /// </summary>
    private static async Task EnsureModelAsync(CancellationToken ct)
    {
        var state = Readiness();
        if (state == UpscaleReadiness.Ready) return;
        if (state != UpscaleReadiness.NotReady) throw new ConversionException(UpscaleCopy.NotAvailable);

        var result = await ImageScaler.EnsureReadyAsync().AsTask(ct).ConfigureAwait(false);
        if (result.Status != AIFeatureReadyResultState.Success) throw new ConversionException(UpscaleCopy.ModelNotInstalled);
    }

    private static Raster Scale(Raster source)
    {
        var width = source.Width * 2;
        var height = source.Height * 2;
        // The scaler takes BGRA bitmaps. The converter hands over opaque pixels; transparency is rebuilt around the engine, not inside it.
        using var input = SoftwareBitmap.CreateCopyFromBuffer(
            source.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, source.Width, source.Height, BitmapAlphaMode.Premultiplied);
        using var scaler = ImageScaler.CreateAsync().AsTask().GetAwaiter().GetResult();
        using var output = scaler.ScaleSoftwareBitmap(input, width, height);
        using var bgra = output.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(output, BitmapPixelFormat.Bgra8);
        var pixels = new byte[checked(output.PixelWidth * output.PixelHeight * 4)];
        (bgra ?? output).CopyToBuffer(pixels.AsBuffer());
        return new Raster(output.PixelWidth, output.PixelHeight, pixels);
    }
}
