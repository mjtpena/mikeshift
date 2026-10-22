namespace Shiftvert.Core.Documents;

/// <summary>
/// Output formats of the Document category. (Named <c>DocFormat</c>, not <c>DocumentFormat</c>, because
/// <c>DocumentFormat.OpenXml</c> is a namespace and a type of that name would shadow it.)
/// </summary>
public enum DocFormat
{
    Pdf,
    Docx,
    Rtf,
    Txt,
    Html,
    Odt,
}

/// <summary>
/// Document conversions via the DocModel pivot (PLAN §3.4 Documents). OWNED BY W2d (Documents): implement the bodies;
/// you may change signatures inside this file (and the lambdas in <c>Actions/Catalog/DocumentActions.cs</c>, which you also own).
/// <para>Returns the path written via <c>Naming.CommitAsync</c>; throws <see cref="ConversionException"/> for expected failures.</para>
/// </summary>
public static class DocumentConverter
{
    /// <summary>
    /// Converts <paramref name="input"/> to <paramref name="format"/>; output <c>name.ext</c>.
    /// <paramref name="caps"/> tells the converter whether Word COM is available (legacy .doc, high-fidelity PDF).
    /// </summary>
    public static Task<string> ToFormatAsync(string input, DocFormat format, Capabilities caps, CancellationToken ct) =>
        throw new NotImplementedException("W2d");
}

/// <summary>
/// Selection-level document checks used by <c>DocumentActions</c> to hide tiles. OWNED BY W2d (Documents).
/// </summary>
public static class DocumentProbe
{
    /// <summary>
    /// True if converting every file in <paramref name="paths"/> to <paramref name="target"/> is worth offering:
    /// files with embedded pictures allow TXT only; legacy .doc requires <see cref="Capabilities.WordCom"/>;
    /// DOCX/ODT must start with the <c>PK</c> signature. Must be cheap (called while the wheel is populated).
    /// </summary>
    // W2d: implement (stub offers everything)
    public static bool CanConvertTo(IReadOnlyList<string> paths, DocFormat target, Capabilities caps) => true;
}
