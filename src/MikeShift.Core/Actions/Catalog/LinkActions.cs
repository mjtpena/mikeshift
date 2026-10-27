using MikeShift.Core.Images;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Link category's actions (Internet shortcuts, <c>.url</c>). Titles are unique within the list and double as CLI names.
/// </summary>
public static class LinkActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        // The address in the shortcut, not the file, goes into the code.
        ActionDef.PerFile("QR code", WheelPage.Tool,
            (input, _, ct) => QrCodes.WriteAsync(input, ct),
            glyph: Glyphs.QrCode),
    ];
}
