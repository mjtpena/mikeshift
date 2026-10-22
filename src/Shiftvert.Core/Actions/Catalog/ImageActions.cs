using Shiftvert.Core.Images;
using Shiftvert.Core.Pdf;

namespace Shiftvert.Core.Actions.Catalog;

/// <summary>
/// The Image category's actions, in wheel order (PLAN §1.2). OWNED BY W2a (Images), except the "PDF" and "Merge PDF"
/// entries, whose lambdas call <see cref="PdfConverter"/> (W2b may adjust those two calls).
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class ImageActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        // ---- Format page
        Format("PNG", ImageFormat.Png, "png"),
        Format("JPG", ImageFormat.Jpg, "jpg", "jpeg", "jpe", "jfif"),
        Format("TIFF", ImageFormat.Tiff, "tif", "tiff"),
        Format("GIF", ImageFormat.Gif, "gif"),
        Format("BMP", ImageFormat.Bmp, "bmp", "dib"),
        Format("HEIC", ImageFormat.Heic, requires: c => c.HeifEncode, hideFor: ["heic", "heif"]),
        Format("WebP", ImageFormat.Webp, requires: c => c.WebpEncode, hideFor: ["webp"]),
        ActionDef.PerFile("PDF", WheelPage.Format,
            (input, _, ct) => PdfConverter.ImageToPdfAsync(input, ct)),

        // ---- Tool page
        ActionDef.PerFile("Compress", WheelPage.Tool,
            (input, _, ct) => ImageConverter.CompressAsync(input, ct),
            glyph: Glyphs.Compress),
        ActionDef.PerFile("Resize 50%", WheelPage.Tool,
            (input, _, ct) => ImageConverter.ResizeHalfAsync(input, ct),
            glyph: Glyphs.Resize),
        ActionDef.PerFile("Crop", WheelPage.Tool,
            async (input, ctx, ct) => await ImageConverter.CropAsync(input, await ctx.RequireCropAsync(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            glyph: Glyphs.Crop, interactive: true),
        ActionDef.PerFile("Strip EXIF", WheelPage.Tool,
            (input, _, ct) => ImageConverter.StripExifAsync(input, ct),
            glyph: Glyphs.StripExif),
        ActionDef.SingleOutput("Merge PDF", WheelPage.Tool,
            (paths, _, ct) => PdfConverter.MergeImagesAsync(paths, ct),
            glyph: Glyphs.MergePdf,
            appliesTo: ActionDef.AtLeast(2)),
    ];

    private static ActionDef Format(string title, ImageFormat format, params string[] hideFor) =>
        Format(title, format, null, hideFor);

    private static ActionDef Format(string title, ImageFormat format, Func<Capabilities, bool>? requires, string[] hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, _, ct) => ImageConverter.ToFormatAsync(input, format, ct),
            hideFor: hideFor, requires: requires);
}
