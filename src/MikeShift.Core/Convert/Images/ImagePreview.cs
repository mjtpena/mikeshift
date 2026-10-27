using MikeShift.Core.Actions;

namespace MikeShift.Core.Images;

/// <summary>
/// Upright, downscaled pixels of a picture for an on-screen preview, decoded the way the converters decode it (WIC first, SkiaSharp
/// when Windows has no decoder for the format, such as WebP without the WebP Image Extension). The crop editor uses it when WIC alone
/// cannot open the picture, so every picture the Crop tile is offered for can also be previewed.
/// </summary>
public static class ImagePreview
{
    /// <summary>
    /// BGRA pixels with straight alpha, upright, <paramref name="Width"/> x <paramref name="Height"/>; the full picture is
    /// <paramref name="ImageWidth"/> x <paramref name="ImageHeight"/> (also upright).
    /// </summary>
    public sealed record Pixels(byte[] Bgra, int Width, int Height, int ImageWidth, int ImageHeight);

    /// <summary>
    /// Decodes <paramref name="path"/> with its long side scaled down to at most <paramref name="maxLongSide"/> (never enlarged).
    /// Throws <see cref="ConversionException"/> with a plain message when the picture cannot be read.
    /// </summary>
    public static async Task<Pixels> DecodeAsync(string path, int maxLongSide, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLongSide, 1);
        var full = (await ImageCodec.DecodeAsync(path, ImageCodec.MaxPixels, ct).ConfigureAwait(false)).Pixels;
        var shrink = Math.Min(1.0, (double)maxLongSide / Math.Max(full.Width, full.Height));
        var w = Math.Max(1, (int)Math.Round(full.Width * shrink));
        var h = Math.Max(1, (int)Math.Round(full.Height * shrink));
        var scaled = full.CropScale(new RectPx(0, 0, full.Width, full.Height), w, h);
        return new Pixels(scaled.Bgra, w, h, full.Width, full.Height);
    }
}
