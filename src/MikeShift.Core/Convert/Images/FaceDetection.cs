using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace MikeShift.Core.Images;

/// <summary>Finds the faces in a picture and returns their boxes in the picture's own pixels. Tests pass a stand-in so they don't depend on how accurate the real detector is.</summary>
internal delegate Task<IReadOnlyList<RectPx>> FaceFinder(Raster pixels, CancellationToken ct);

/// <summary>
/// Face detection through <c>Windows.Media.FaceAnalysis.FaceDetector</c>: built into Windows 10 and 11, offline, no NPU and no extra package.
/// It finds frontal faces best; profiles and small or partly hidden faces can be missed, which is why the docs tell people to check the result.
/// </summary>
internal static class FaceDetection
{
    /// <summary>The detector reads a copy no larger than this on its long side; the boxes are mapped back to the full picture.</summary>
    public const int DetectLongSide = 1600;

    /// <summary>True if Windows can detect faces here (<see cref="Capabilities.FaceDetect"/>). Never throws.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return FaceDetector.IsSupported;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The boxes of the faces in <paramref name="pixels"/>. Throws <see cref="ConversionException"/> when the detector can't run or fails;
    /// an empty list means no face was found.
    /// </summary>
    public static async Task<IReadOnlyList<RectPx>> FindAsync(Raster pixels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsAvailable()) throw new ConversionException(FaceBlurCopy.NoDetector);

        // Shrink first, then flatten, so a huge picture never gets a second full-size copy.
        var small = FitWithin(pixels, DetectLongSide).FlattenOnWhite();
        var scaleX = (double)pixels.Width / small.Width;
        var scaleY = (double)pixels.Height / small.Height;

        try
        {
            using var color = SoftwareBitmap.CreateCopyFromBuffer(
                small.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, small.Width, small.Height, BitmapAlphaMode.Ignore);
            // The detector only takes the formats it lists (Gray8 or Nv12).
            var format = FaceDetector.GetSupportedBitmapPixelFormats().First();
            using var input = SoftwareBitmap.Convert(color, format);
            var detector = await FaceDetector.CreateAsync().AsTask(ct).ConfigureAwait(false);
            var faces = await detector.DetectFacesAsync(input).AsTask(ct).ConfigureAwait(false);

            return faces.Select(f => new RectPx(
                (int)Math.Floor(f.FaceBox.X * scaleX), (int)Math.Floor(f.FaceBox.Y * scaleY),
                (int)Math.Ceiling(f.FaceBox.Width * scaleX), (int)Math.Ceiling(f.FaceBox.Height * scaleY))).ToList();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OutOfMemoryException or NotSupportedException)
        {
            throw new ConversionException(FaceBlurCopy.Unreadable, ex);
        }
    }

    private static Raster FitWithin(Raster pixels, int maxSide)
    {
        var longSide = Math.Max(pixels.Width, pixels.Height);
        if (longSide <= maxSide) return pixels;
        var s = (double)maxSide / longSide;
        var w = Math.Clamp((int)Math.Floor(pixels.Width * s), 1, maxSide);
        var h = Math.Clamp((int)Math.Floor(pixels.Height * s), 1, maxSide);
        return pixels.CropScale(new RectPx(0, 0, pixels.Width, pixels.Height), w, h);
    }
}
