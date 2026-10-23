using MikeShift.Core.Documents;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Document category's actions, in wheel order (PLAN §1.2). OWNED BY W2d (Documents).
/// There are no tools for documents (only Zip, which the catalog appends). Whether a given format is offered for a
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
    ];

    private static ActionDef Doc(string title, DocFormat format, params string[] hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, ctx, ct) => DocumentConverter.ToFormatAsync(input, format, ctx.Capabilities, ct),
            hideFor: hideFor,
            appliesTo: (paths, caps) => DocumentProbe.CanConvertTo(paths, format, caps));
}
