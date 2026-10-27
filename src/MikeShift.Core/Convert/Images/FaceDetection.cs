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

            return faces.Select(f => MapBox(f.FaceBox.X, f.FaceBox.Y, f.FaceBox.Width, f.FaceBox.Height, scaleX, scaleY)).ToList();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OutOfMemoryException or NotSupportedException)
        {
            throw new ConversionException(FaceBlurCopy.Unreadable, ex);
        }
    }

    /// <summary>A box found in the reduced copy, in the pixels of the full picture (rounded outwards, so the face is never cut short).</summary>
    internal static RectPx MapBox(double x, double y, double width, double height, double scaleX, double scaleY) =>
        new((int)Math.Floor(x * scaleX), (int)Math.Floor(y * scaleY), (int)Math.Ceiling(width * scaleX), (int)Math.Ceiling(height * scaleY));

    /// <summary>The size of the copy the detector reads: the picture itself up to <paramref name="maxSide"/> on the long side, otherwise shrunk to fit.</summary>
    internal static (int Width, int Height) ReducedSize(int width, int height, int maxSide)
    {
        var longSide = Math.Max(width, height);
        if (longSide <= maxSide) return (width, height);
        var s = (double)maxSide / longSide;
        return (Math.Clamp((int)Math.Floor(width * s), 1, maxSide), Math.Clamp((int)Math.Floor(height * s), 1, maxSide));
    }

    private static Raster FitWithin(Raster pixels, int maxSide)
    {
        var (w, h) = ReducedSize(pixels.Width, pixels.Height, maxSide);
        return w == pixels.Width && h == pixels.Height
            ? pixels
            : pixels.CropScale(new RectPx(0, 0, pixels.Width, pixels.Height), w, h);
    }
}
