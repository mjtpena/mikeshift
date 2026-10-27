using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using MikeShift.Core.Pdf;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Image category's actions, in wheel order. The "PDF" and "Merge PDF" entries call <see cref="PdfConverter"/>.
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class ImageActions
{
    /// <summary>The Resize tool for each percentage the settings offer: "Resize 25%", "Resize 50%", "Resize 75%" (all with the stable <see cref="ActionDef.Id"/> "Resize").</summary>
    private static readonly IReadOnlyDictionary<int, ActionDef> ResizeTools = ConversionOptions.ResizeChoices.ToDictionary(
        percent => percent,
        percent => ActionDef.PerFile(ConversionOptions.ResizeTitle(percent), WheelPage.Tool,
            (input, ctx, ct) => ImageConverter.ResizeAsync(input, percent, ctx.Capabilities, ctx.Options, ct),
            glyph: Glyphs.Resize, id: "Resize"));

    /// <summary>The list with the default Resize percentage (50). <see cref="WithResize"/> names it for the user's setting.</summary>
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
        Ico(),
        ActionDef.PerFile("PDF", WheelPage.Format,
            (input, _, ct) => PdfConverter.ImageToPdfAsync(input, ct)),
        ActionDef.PerFile("TXT", WheelPage.Format,
            (input, _, ct) => ImageText.ToTextAsync(input, ct),
            requires: c => c.Ocr),

        // ---- Tool page
        ActionDef.PerFile("Compress", WheelPage.Tool,
            (input, ctx, ct) => ImageConverter.CompressAsync(input, ctx.Capabilities, ctx.Options, ct),
            glyph: Glyphs.Compress,
            appliesTo: CanCompress),
        ResizeTools[ConversionOptions.DefaultResizePercent],
        ActionDef.PerFile("Crop", WheelPage.Tool,
            async (input, ctx, ct) =>
            {
                // Refuse animated GIF / multi-page TIFF / animated WebP now, not after the user drew the crop.
                await ImageConverter.RequireSingleFrameAsync(input, "cropped", ct).ConfigureAwait(false);
                var choice = await ctx.RequireCropAsync(input, ct).ConfigureAwait(false);
                return await ImageConverter.CropAsync(input, choice, ctx.Capabilities, ctx.Options, ct).ConfigureAwait(false);
            },
            glyph: Glyphs.Crop, interactive: true),
        ActionDef.PerFile("Strip EXIF", WheelPage.Tool,
            (input, ctx, ct) => ImageConverter.StripExifAsync(input, ctx.Capabilities, ct),
            glyph: Glyphs.StripExif),
        ActionDef.SingleOutput("Merge PDF", WheelPage.Tool,
            (paths, _, ct) => PdfConverter.MergeImagesAsync(paths, ct),
            glyph: Glyphs.MergePdf,
            appliesTo: ActionDef.AtLeast(2)),
        ReadQrCode(),
    ];

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ActionDef>> ByResize = ConversionOptions.ResizeChoices.ToDictionary(
        percent => percent,
        percent => percent == ConversionOptions.DefaultResizePercent
            ? All
            : (IReadOnlyList<ActionDef>)All.Select(a => a.Id == "Resize" ? ResizeTools[percent] : a).ToArray());

    /// <summary>The list with the Resize tool named for <paramref name="percent"/> (25, 50 or 75): same tiles, same order.</summary>
    public static IReadOnlyList<ActionDef> WithResize(int percent) =>
        ByResize.TryGetValue(percent, out var list) ? list : All;

    /// <summary>
    /// Compress turns HEIC into HEIC, or into PNG when no HEIF encoder exists; PNG is almost always bigger, so the tile would only
    /// ever say "already small". Hide it when every selected file is HEIC/HEIF and this PC can't encode HEIC.
    /// </summary>
    internal static bool CanCompress(IReadOnlyList<string> paths, Capabilities caps) =>
        caps.HeifEncode || paths.Count == 0 || !paths.All(p => Kind.ExtensionOf(p) is "heic" or "heif");

    /// <summary>
    /// The ICO tile. It runs through <see cref="Each"/> like the other formats, then adds one caution to the result when a source was under
    /// 256 px (the file is still written).
    /// </summary>
    private static ActionDef Ico()
    {
        HashSet<string> skip = ["ico"];
        return new ActionDef("ICO", WheelPage.Format, async (paths, ctx, ct) =>
        {
            var soft = false;
            var result = await Each.RunAsync(paths, ctx, async (input, token) =>
            {
                var (path, isSoft) = await ImageConverter.ToIcoAsync(input, token).ConfigureAwait(false);
                soft |= isSoft;
                return path;
            }, ct, skip, "ICO").ConfigureAwait(false);
            return soft ? result with { Notes = [FeedbackCopy.IconSourceSmall] } : result;
        })
        { HideFor = skip };
    }

    /// <summary>Read QR code: <c>photo qr.txt</c> with the text of every code. Like <see cref="ActionDef.PerFile"/>, plus the first code's first line for the toast.</summary>
    private static ActionDef ReadQrCode() => new("Read QR code", WheelPage.Tool, async (paths, ctx, ct) =>
    {
        var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = await Each.RunAsync(paths, ctx, async (input, token) =>
        {
            var (path, text) = await QrCodes.ReadAsync(input, token).ConfigureAwait(false);
            first[path] = text;
            return path;
        }, ct, alreadyLabel: "Read QR code").ConfigureAwait(false);
        return result with { Outputs = [.. result.Outputs.Select(o => first.TryGetValue(o.Path, out var text) ? o with { Preview = text } : o)] };
    }) { Glyph = Glyphs.ReadQrCode };

    private static ActionDef Format(string title, ImageFormat format, params string[] hideFor) =>
        Format(title, format, null, hideFor);

    private static ActionDef Format(string title, ImageFormat format, Func<Capabilities, bool>? requires, string[] hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, ctx, ct) => ImageConverter.ToFormatAsync(input, format, ctx.Options, ct),
            hideFor: hideFor, requires: requires);
}
