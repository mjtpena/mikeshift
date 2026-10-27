using MikeShift.Core.Documents;
using MikeShift.Core.Images;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Document category's actions, in wheel order.
/// The only tool for documents is QR code, for text files (besides Zip, which the catalog appends). Whether a given format is offered for a
/// concrete selection (pictures allow TXT only, legacy .doc needs Word) is decided by <see cref="DocumentProbe.CanConvertTo"/>.
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class DocumentActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        Doc("PDF", DocFormat.Pdf),
        Doc("DOCX", DocFormat.Docx, "docx"),
        Doc("RTF", DocFormat.Rtf, "rtf"),
        Doc("TXT", DocFormat.Txt, "txt"),
        Doc("HTML", DocFormat.Html, "html", "htm"),
        Doc("ODT", DocFormat.Odt, "odt"),

        // The one document tool: only for plain text files (a .txt that fits in a code).
        ActionDef.PerFile("QR code", WheelPage.Tool,
            (input, _, ct) => QrCodes.WriteAsync(input, ct),
            glyph: Glyphs.QrCode,
            appliesTo: (paths, _) => paths.All(p => Kind.ExtensionOf(p) == "txt")),
    ];

    private static ActionDef Doc(string title, DocFormat format, params string[] hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, ctx, ct) => DocumentConverter.ToFormatAsync(input, format, ctx.Capabilities, ct),
            hideFor: hideFor,
            appliesTo: (paths, caps) => DocumentProbe.CanConvertTo(paths, format, caps));
}
