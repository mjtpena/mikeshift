using MikeShift.Core.Pdf;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The PDF category's actions, in wheel order (PLAN §1.2). OWNED BY W2b (PDF).
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class PdfActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        // ---- Format page
        ActionDef.PerFile("PNG", WheelPage.Format,
            (input, _, ct) => PdfConverter.ToImagesAsync(input, PdfPageFormat.Png, ct)),
        ActionDef.PerFile("JPG", WheelPage.Format,
            (input, _, ct) => PdfConverter.ToImagesAsync(input, PdfPageFormat.Jpg, ct)),
        ActionDef.PerFile("TXT", WheelPage.Format,
            (input, _, ct) => PdfConverter.ToTextAsync(input, ct)),

        // ---- Tool page
        ActionDef.PerFile("Compress", WheelPage.Tool,
            (input, _, ct) => PdfConverter.CompressAsync(input, ct),
            glyph: Glyphs.Compress),
        ActionDef.PerFile("Split", WheelPage.Tool,
            (input, _, ct) => PdfConverter.SplitAsync(input, ct),
            glyph: Glyphs.Split),
        ActionDef.SingleOutput("Merge", WheelPage.Tool,
            (paths, _, ct) => PdfConverter.MergeAsync(paths, ct),
            glyph: Glyphs.Merge,
            appliesTo: ActionDef.AtLeast(2)),
    ];
}
