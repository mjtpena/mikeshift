using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Images;

/// <summary>Raster output formats offered by the Image category.</summary>
public enum ImageFormat
{
    Png,
    Jpg,
    Tiff,
    Gif,
    Bmp,
    /// <summary>Only offered when <see cref="Capabilities.HeifEncode"/>.</summary>
    Heic,
    /// <summary>Encoded with SkiaSharp (no WIC WebP encoder exists).</summary>
    Webp,
}

/// <summary>
/// Image conversions (PLAN §1.2 Image rows, §3.4 Images). Decoding is WIC (SkiaSharp fallback); every result is upright
/// pixels (EXIF orientation baked in, so the orientation tag is no longer needed), written through
/// <see cref="Naming.CommitStreamAsync"/>.
/// <para>Every method returns the path of the file it wrote, throws <see cref="ConversionException"/> with a user-facing
/// message for expected failures, and never modifies the input.</para>
/// </summary>
public static class ImageConverter
{
    // Naming suffixes (PLAN §1.1).
    private const string CompressedSuffix = " compressed";
    private const string ResizedSuffix = " 50%";
    private const string CroppedSuffix = " cropped";
    private const string CleanSuffix = " clean";

    private const double DefaultQuality = 0.9;
    private const double CompressQuality = 0.6;
    private const double StripQuality = 0.95;
    private const int CompressMaxLongSide = 3000;
    private const double CompressMinSaving = 0.05;
    private const int MaxOutputSide = 30_000;

    // ------------------------------------------------------------------ Format page

    /// <summary>Converts to <paramref name="format"/>; output <c>name.ext</c> (no suffix), uniquified.</summary>
    public static Task<string> ToFormatAsync(string input, ImageFormat format, CancellationToken ct) =>
        ToFormatAsync(input, format, ImageCodec.MaxPixels, ct);

    internal static async Task<string> ToFormatAsync(string input, ImageFormat format, long maxPixels, CancellationToken ct)
    {
        var img = await ImageCodec.DecodeAsync(input, maxPixels, ct).ConfigureAwait(false);
        var bytes = await EncodeAsync(img, img.Pixels, format, DefaultQuality, keepExif: true, ct).ConfigureAwait(false);
        return await CommitAsync(input, ExtensionFor(format, null), "", bytes, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Tool page

    /// <summary>Compress: long side to 3000 px, quality 0.6, " compressed"; keep only if at least 5 % smaller.</summary>
    public static Task<string> CompressAsync(string input, CancellationToken ct) =>
        CompressAsync(input, Capabilities.Current, ct);

    /// <summary>
    /// Compress. Output is a JPG (flattened on white) unless the source is HEIC or has real transparency: then HEIC when
    /// <paramref name="caps"/> allows encoding it, else PNG. Animated GIF/APNG/WebP are refused. If the result is not at least
    /// 5 % smaller than the original a <see cref="ConversionException"/> ("already small") is thrown and nothing is written.
    /// </summary>
    public static async Task<string> CompressAsync(string input, Capabilities caps, CancellationToken ct)
    {
        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        RequireSingleFrame(img, "compressed");

        var pixels = LongSideAtMost(img.Pixels, CompressMaxLongSide);
        var hasAlpha = img.HasRealAlpha;

        ImageFormat format;
        double quality;
        if (img.Container == ImageContainer.Heic || hasAlpha)
        {
            format = caps.HeifEncode ? ImageFormat.Heic : ImageFormat.Png;
            quality = CompressQuality;
        }
        else
        {
            format = ImageFormat.Jpg;
            quality = CompressQuality;
        }

        var bytes = await EncodeAsync(img, pixels, format, quality, keepExif: true, ct).ConfigureAwait(false);

        var originalLength = new FileInfo(input).Length;
        if (bytes.Length > originalLength * (1 - CompressMinSaving))
            throw new ConversionException("This image is already small, so compressing it wouldn't save much space.");

        return await CommitAsync(input, ExtensionFor(format, null), CompressedSuffix, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Resize 50 %: half the long side, same format, " 50%".</summary>
    public static Task<string> ResizeHalfAsync(string input, CancellationToken ct) =>
        ResizeHalfAsync(input, Capabilities.Current, ct);

    /// <summary>Resize to half width and height, keeping the format (PNG when the format can't be written on this PC).</summary>
    public static async Task<string> ResizeHalfAsync(string input, Capabilities caps, CancellationToken ct)
    {
        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        RequireSingleFrame(img, "resized");

        var w = Math.Max(1, (img.Width + 1) / 2);
        var h = Math.Max(1, (img.Height + 1) / 2);
        var pixels = img.Pixels.CropScale(new RectPx(0, 0, img.Width, img.Height), w, h);

        var format = SameFormat(img, caps);
        var bytes = await EncodeAsync(img, pixels, format, DefaultQuality, keepExif: true, ct).ConfigureAwait(false);
        return await CommitAsync(input, ExtensionFor(format, input), ResizedSuffix, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Crop to <paramref name="crop"/> (+ optional scale), same format, " cropped".</summary>
    public static Task<string> CropAsync(string input, CropChoice crop, CancellationToken ct) =>
        CropAsync(input, crop, Capabilities.Current, ct);

    /// <summary>
    /// Crops the upright image to <c>crop.Rect</c> (clamped to the image) and scales the piece to
    /// <c>OutputWidth x OutputHeight</c> with high-quality interpolation.
    /// </summary>
    public static async Task<string> CropAsync(string input, CropChoice crop, Capabilities caps, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crop);
        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        RequireSingleFrame(img, "cropped");

        // Clamp to the image.
        var x0 = Math.Clamp(crop.Rect.X, 0, img.Width);
        var y0 = Math.Clamp(crop.Rect.Y, 0, img.Height);
        var x1 = Math.Clamp(crop.Rect.X + crop.Rect.W, 0, img.Width);
        var y1 = Math.Clamp(crop.Rect.Y + crop.Rect.H, 0, img.Height);
        if (x1 <= x0 || y1 <= y0)
            throw new ConversionException("The crop area is outside the image.");
        var rect = new RectPx(x0, y0, x1 - x0, y1 - y0);

        var ow = crop.OutputWidth > 0 ? crop.OutputWidth : rect.W;
        var oh = crop.OutputHeight > 0 ? crop.OutputHeight : rect.H;
        if (ow > MaxOutputSide || oh > MaxOutputSide || (long)ow * oh > ImageCodec.MaxPixels)
            throw new ConversionException("The cropped picture would be too large.");

        var pixels = img.Pixels.CropScale(rect, ow, oh);
        var format = SameFormat(img, caps);
        var bytes = await EncodeAsync(img, pixels, format, DefaultQuality, keepExif: true, ct).ConfigureAwait(false);
        return await CommitAsync(input, ExtensionFor(format, input), CroppedSuffix, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Strip EXIF/metadata, keep orientation, " clean".</summary>
    public static Task<string> StripExifAsync(string input, CancellationToken ct) =>
        StripExifAsync(input, Capabilities.Current, ct);

    /// <summary>
    /// Re-encodes only the pixels (orientation is baked in), so no Exif, GPS, XMP or Photoshop data survives. For JPEG the output
    /// is scanned for APP1/APP13 segments and any that remain are removed.
    /// </summary>
    public static async Task<string> StripExifAsync(string input, Capabilities caps, CancellationToken ct)
    {
        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        RequireSingleFrame(img, "cleaned");

        var format = SameFormat(img, caps);
        var bytes = await EncodeAsync(img, img.Pixels, format, StripQuality, keepExif: false, ct).ConfigureAwait(false);
        if (format == ImageFormat.Jpg && JpegMeta.HasMetadataSegments(bytes))
            bytes = JpegMeta.RemoveMetadataSegments(bytes);

        return await CommitAsync(input, ExtensionFor(format, input), CleanSuffix, bytes, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ helpers

    private static void RequireSingleFrame(DecodedImage img, string verb) => RequireSingleFrame(img.FrameCount, img.IsAnimated, verb);

    private static void RequireSingleFrame(int frameCount, bool animated, string verb)
    {
        if (animated)
            throw new ConversionException($"Animated images can't be {verb} without losing the animation.");
        if (frameCount > 1)
            throw new ConversionException($"This file has {frameCount} pages, and only the first would be kept, so it can't be {verb}.");
    }

    /// <summary>
    /// Fast header-only version of the single-frame check, so an interactive tool (Crop) can refuse an animated GIF, multi-page
    /// TIFF or animated WebP before the user spends time in the editor. Throws the same <see cref="ConversionException"/> as the conversion.
    /// </summary>
    internal static async Task RequireSingleFrameAsync(string input, string verb, CancellationToken ct)
    {
        var (frames, animated) = await ImageCodec.FrameInfoAsync(input, ct).ConfigureAwait(false);
        RequireSingleFrame(frames, animated, verb);
    }

    private static Raster LongSideAtMost(Raster pixels, int maxLongSide)
    {
        var longSide = Math.Max(pixels.Width, pixels.Height);
        if (longSide <= maxLongSide) return pixels;
        var scale = (double)maxLongSide / longSide;
        var w = Math.Max(1, (int)Math.Round(pixels.Width * scale));
        var h = Math.Max(1, (int)Math.Round(pixels.Height * scale));
        return pixels.CropScale(new RectPx(0, 0, pixels.Width, pixels.Height), w, h);
    }

    /// <summary>The output format for "same format" tools; PNG where the source format can't be written here.</summary>
    private static ImageFormat SameFormat(DecodedImage img, Capabilities caps) => img.Container switch
    {
        ImageContainer.Png => ImageFormat.Png,
        ImageContainer.Jpeg => ImageFormat.Jpg,
        ImageContainer.Gif => ImageFormat.Gif,
        ImageContainer.Bmp => ImageFormat.Bmp,
        ImageContainer.Tiff => ImageFormat.Tiff,
        ImageContainer.Webp when caps.WebpEncode => ImageFormat.Webp,
        ImageContainer.Heic when caps.HeifEncode => ImageFormat.Heic,
        _ => ImageFormat.Png, // AVIF, ICO, JPEG XR, or an encoder this PC doesn't have
    };

    /// <summary>Extension (with dot) for <paramref name="format"/>; keeps the input's own spelling (.jpeg, .tif) when it matches.</summary>
    private static string ExtensionFor(ImageFormat format, string? input)
    {
        if (input is not null)
        {
            var own = Kind.ExtensionOf(input);
            var matches = format switch
            {
                ImageFormat.Jpg => own is "jpg" or "jpeg" or "jpe" or "jfif",
                ImageFormat.Tiff => own is "tif" or "tiff",
                ImageFormat.Bmp => own is "bmp" or "dib",
                ImageFormat.Heic => own is "heic" or "heif",
                ImageFormat.Png => own == "png",
                ImageFormat.Gif => own == "gif",
                ImageFormat.Webp => own == "webp",
                _ => false,
            };
            if (matches) return "." + own;
        }
        return format switch
        {
            ImageFormat.Png => ".png",
            ImageFormat.Jpg => ".jpg",
            ImageFormat.Tiff => ".tiff",
            ImageFormat.Gif => ".gif",
            ImageFormat.Bmp => ".bmp",
            ImageFormat.Heic => ".heic",
            ImageFormat.Webp => ".webp",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>Encodes, carrying the source's Exif into JPEG output (made upright for the new pixel size) when asked.</summary>
    private static Task<byte[]> EncodeAsync(
        DecodedImage source, Raster pixels, ImageFormat format, double quality, bool keepExif, CancellationToken ct)
    {
        byte[]? exif = null;
        if (keepExif && format == ImageFormat.Jpg && source.JpegExif is not null)
            exif = JpegMeta.MakeUpright(source.JpegExif, pixels.Width, pixels.Height);
        return ImageCodec.EncodeAsync(pixels, format, quality, exif, keepExif ? source.Properties : null, ct);
    }

    private static async Task<string> CommitAsync(string input, string extension, string suffix, byte[] bytes, CancellationToken ct)
    {
        var spec = Naming.Plan(input, extension, suffix);
        var commit = await Naming.CommitStreamAsync(
            spec, (stream, token) => stream.WriteAsync(bytes, token).AsTask(), ct).ConfigureAwait(false);
        return commit.Path;
    }
}
