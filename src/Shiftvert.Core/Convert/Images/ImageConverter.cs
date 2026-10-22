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
/// Image conversions (PLAN §1.2 Image rows, §3.4 Images). OWNED BY W2a (Images): implement the bodies; you may change
/// signatures inside this file (and the matching lambdas in <c>Actions/Catalog/ImageActions.cs</c>, which you also own).
/// <para>Every method returns the path of the file it wrote (via <c>Naming.CommitAsync</c>), throws
/// <see cref="ConversionException"/> with a user-facing message for expected failures, and never modifies the input.</para>
/// </summary>
public static class ImageConverter
{
    /// <summary>Converts to <paramref name="format"/>; output <c>name.ext</c> (no suffix), uniquified.</summary>
    public static Task<string> ToFormatAsync(string input, ImageFormat format, CancellationToken ct) =>
        throw new NotImplementedException("W2a");

    /// <summary>Compress: long side to 3000 px, quality 0.6, " compressed"; keep only if at least 5 % smaller.</summary>
    public static Task<string> CompressAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2a");

    /// <summary>Resize 50 %: half the long side, same format, " 50%".</summary>
    public static Task<string> ResizeHalfAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2a");

    /// <summary>Crop to <paramref name="crop"/> (+ optional scale), same format, " cropped".</summary>
    public static Task<string> CropAsync(string input, CropChoice crop, CancellationToken ct) =>
        throw new NotImplementedException("W2a");

    /// <summary>Strip EXIF/metadata, keep orientation, " clean".</summary>
    public static Task<string> StripExifAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2a");
}
