using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Every action the catalog offers for documents (PDF, DOCX, RTF, TXT, HTML, ODT). Each output is re-read by a reader that is NOT the
/// converters' own (OpenXml SDK for DOCX, System.Xml for ODT, tokenizers written for the suite for RTF and HTML, PdfPig for PDF) and
/// compared with the source's structure: headings, bold / italic / underline, bulleted and numbered lists, the table, every word.
/// </summary>
public class DocumentIntegrityTests
{
    private static readonly Capabilities NoWord = Capabilities.Current with { WordCom = false };

    private static readonly string[] Titles = ["PDF", "DOCX", "RTF", "TXT", "HTML", "ODT"];

    private static string[] Expected => Fixtures.DocumentOutline;

    private static byte[] SourceBytes(string kind) => kind switch
    {
        "docx" => Fixtures.DocxSample(),
        "rtf" => Encoding.ASCII.GetBytes(Fixtures.RtfSample()),
        "odt" => Fixtures.OdtSample(),
        "html" => new UTF8Encoding(false).GetBytes(Fixtures.HtmlSample()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsSame(string kind, string title) => string.Equals(kind, title, StringComparison.OrdinalIgnoreCase);

    public static TheoryData<string, string> Cases()
    {
        var d = new TheoryData<string, string>();
        foreach (var kind in new[] { "docx", "rtf", "odt", "html" })
            foreach (var t in Titles)
                if (!IsSame(kind, t)) d.Add(kind, t);
        return d;
    }

    /// <summary>Reads a source or output file back as an outline, whatever its format.</summary>
    private static IReadOnlyList<string> Outline(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".docx" => DocCheck.DocxOutline(path),
        ".odt" => DocCheck.OdtOutline(path),
        ".html" or ".htm" => DocCheck.HtmlOutline(File.ReadAllText(path, Encoding.UTF8)),
        ".rtf" => DocCheck.RtfOutline(File.ReadAllText(path, Encoding.GetEncoding(1252))),
        _ => throw new ArgumentException(path),
    };

    // ------------------------------------------------------------------ the readers are right

    [Theory]
    [InlineData("docx")]
    [InlineData("rtf")]
    [InlineData("odt")]
    [InlineData("html")]
    public void The_independent_readers_see_the_source_fixtures_as_designed(string kind)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("src." + kind, SourceBytes(kind));
        Assert.Equal(Expected, Outline(path));
        if (kind == "docx") Assert.Empty(DocCheck.ValidateDocx(path));
        if (kind == "odt") DocCheck.AssertOdtPackage(path, manifestListsParts: false);
    }

    // ------------------------------------------------------------------ Format page

    [Theory, MemberData(nameof(Cases))]
    public async Task Conversion_keeps_structure_formatting_and_every_word(string kind, string title)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("report." + kind, SourceBytes(kind));
        using var _ = Kit.WithCaps(NoWord); // the built-in layout; the Word route has its own test

        var output = Kit.One(await Kit.RunAsync(title, input));

        Assert.Equal(tmp.Combine("report." + title.ToLowerInvariant()), output);
        var metric = await Validate(title, output, Expected);
        Kit.Record("document", $"report.{kind}", title, ValidatorName(title), metric);
    }

    private static string ValidatorName(string title) => title switch
    {
        "DOCX" => "OpenXml SDK reader + OpenXmlValidator",
        "ODT" => "System.Xml reader + ODF package rules",
        "RTF" => "RTF tokenizer written for the suite",
        "HTML" => "HTML tokenizer written for the suite",
        "TXT" => "plain text lines",
        _ => "PdfPig (text, fonts) + Windows.Data.Pdf render",
    };

    private static async Task<string> Validate(string title, string output, IReadOnlyList<string> expected)
    {
        switch (title)
        {
            case "DOCX":
            {
                var errors = DocCheck.ValidateDocx(output);
                Assert.True(errors.Count == 0, "OpenXmlValidator: " + string.Join(" | ", errors.Take(3)));
                Assert.Equal(expected, DocCheck.DocxOutline(output));
                return "outline identical (headings, bold/italic/underline, bullets, numbers, table); OpenXmlValidator: 0 errors";
            }
            case "ODT":
                DocCheck.AssertOdtPackage(output);
                Assert.Equal(expected, DocCheck.OdtOutline(output));
                return "outline identical; mimetype first and stored, manifest and every XML part well formed";
            case "RTF":
                Assert.Equal(expected, Outline(output));
                return "outline identical (headings, bold/italic/underline, bullets, numbers, table)";
            case "HTML":
                Assert.Equal(expected, Outline(output));
                return "outline identical (h1/h2, strong/em/u, ul/ol, table)";
            case "TXT":
            {
                var bytes = File.ReadAllBytes(output);
                var text = new UTF8Encoding(false, true).GetString(bytes);
                var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim()).Where(l => l.Length > 0).ToArray();
                Assert.Equal(
                    ["Quarterly Report", "Overview", "Revenue grew by twelve percent and costs were flat.", "Plain underlined words.",
                     "• Apples", "• Oranges", "1. First", "2. Second", "Name Qty", "Apples 3"], lines);
                return "every line identical; bullets and numbers written out as text";
            }
            default:
                return await ValidatePdf(output, expected);
        }
    }

    /// <summary>PdfPig: all the words in order, headings larger than body text, the bold and italic words in bold and italic faces; Windows renders every page.</summary>
    internal static async Task<string> ValidatePdf(string pdf, IReadOnlyList<string> expected)
    {
        using var doc = PdfDocument.Open(pdf);
        var words = doc.GetPages().SelectMany(p => p.GetWords()).ToList();
        Assert.NotEmpty(words);
        // Bullets and "1." markers are drawn as separate words; the expected text has the list items without them.
        var spoken = words.Select(w => w.Text).Where(t => t is not ("•" or "1." or "2." or "·" or "-")).ToList();
        Assert.Equal(DocCheck.PlainText(expected), string.Join(" ", spoken));

        double Size(string word) => words.First(w => w.Text.StartsWith(word, StringComparison.Ordinal)).Letters[0].PointSize;
        string Face(string word) => words.First(w => w.Text.StartsWith(word, StringComparison.Ordinal)).Letters[0].FontName ?? "";
        Assert.True(Size("Quarterly") > Size("Revenue") + 2, "the heading is not larger than the body text");
        Assert.True(Size("Overview") > Size("Revenue"), "the sub-heading is not larger than the body text");
        Assert.True(Size("Quarterly") > Size("Overview") - 0.01, "heading 1 is smaller than heading 2");
        Assert.Contains("Bold", Face("twelve"), StringComparison.OrdinalIgnoreCase);
        Assert.Matches("(?i)italic|oblique", Face("flat"));
        Assert.DoesNotContain("Bold", Face("Revenue"), StringComparison.OrdinalIgnoreCase);

        var rendered = await PdfCheck.RenderAllAsync(pdf);
        Assert.Equal(doc.NumberOfPages, rendered.Count);
        Assert.All(rendered, p => Assert.True(PdfCheck.InkFraction(p) > 0.001));
        return $"{doc.NumberOfPages} page(s), {words.Count} words in order, heading {Size("Quarterly"):0.#} pt > body {Size("Revenue"):0.#} pt, bold/italic faces, renders";
    }

    // ------------------------------------------------------------------ plain text sources

    public static TheoryData<string, string> TextCases()
    {
        var d = new TheoryData<string, string>();
        foreach (var kind in new[] { "utf8", "utf8-bom", "latin-1252", "hebrew-1255", "md" })
            foreach (var t in new[] { "PDF", "DOCX", "RTF", "HTML", "ODT" })
                d.Add(kind, t);
        return d;
    }

    private static (string File, byte[] Bytes, string[] Lines) TextSource(string kind) => kind switch
    {
        "utf8" => ("notes.txt", Fixtures.DocTxtUtf8(), [Fixtures.DocUnicodeLine, "Second line"]),
        "utf8-bom" => ("notes.txt", Fixtures.DocTxtUtf8Bom(), [Fixtures.DocUnicodeLine, "Second line"]),
        "latin-1252" => ("notes.txt", Fixtures.DocTxtLatin1252(), [Fixtures.DocLatinLine, "Price: 5€ — “fine”"]),
        "hebrew-1255" => ("notes.txt", Fixtures.DocTxtHebrew1255(), [Fixtures.DocHebrewText, "שלום שוב"]),
        "md" => ("notes.md", new UTF8Encoding(false).GetBytes(Fixtures.MarkdownSample()), ["# Title", "Some *emphasis* and `code`.", "- one", "- two"]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory, MemberData(nameof(TextCases))]
    public async Task Text_files_keep_every_character_whatever_their_encoding(string kind, string title)
    {
        var (file, bytes, lines) = TextSource(kind);
        using var tmp = new TempDir();
        var input = tmp.WriteFile(file, bytes);
        using var _ = Kit.WithCaps(NoWord);

        var output = Kit.One(await Kit.RunAsync(title, input));

        var expectedText = Regex.Replace(string.Join(" ", lines), @"\s+", " ").Trim();
        string got;
        switch (title)
        {
            case "DOCX":
                Assert.Empty(DocCheck.ValidateDocx(output));
                got = DocCheck.PlainText(DocCheck.DocxOutline(output));
                break;
            case "ODT":
                DocCheck.AssertOdtPackage(output);
                got = DocCheck.PlainText(DocCheck.OdtOutline(output));
                break;
            case "RTF":
                got = DocCheck.PlainText(Outline(output));
                break;
            case "HTML":
                got = DocCheck.PlainText(Outline(output));
                break;
            default:
            {
                using var pdf = PdfDocument.Open(output);
                got = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
                // PDF text of right-to-left scripts is laid out in visual order: compare as a set of letters for Hebrew.
                if (kind == "hebrew-1255")
                {
                    Assert.Equal(expectedText.Replace(" ", "").OrderBy(c => c), got.Replace(" ", "").OrderBy(c => c));
                    Assert.True((await PdfCheck.RenderAllAsync(output)).Count == 1);
                    Kit.Record("document", $"{file} ({kind})", title, "PdfPig letters + Windows.Data.Pdf render", "all letters present (visual order), renders");
                    return;
                }
                break;
            }
        }
        // Markdown is plain text here: '*' and '`' are kept literally; DocCheck marks '_' as italic so none is expected.
        Assert.Equal(expectedText, Regex.Replace(got, @"\s+", " ").Trim());
        Kit.Record("document", $"{file} ({kind})", title, ValidatorName(title), "every word and character identical to the decoded source text");
    }

    // ------------------------------------------------------------------ documents with pictures

    [Fact]
    public async Task A_document_with_a_picture_offers_only_TXT_and_the_text_is_all_there()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture());
        using var _ = Kit.WithCaps(NoWord);

        foreach (var t in new[] { "PDF", "RTF", "HTML", "ODT" })
            Assert.False(Kit.Offered(t, input), t + " must not be offered for a document with pictures (they would be silently dropped)");
        var output = Kit.One(await Kit.RunAsync("TXT", input));
        var lines = File.ReadAllText(output).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        Assert.Contains("Quarterly Report", lines);
        Assert.Contains("Revenue grew by twelve percent and costs were flat.", lines);
        Kit.Record("document", "pic.docx (embedded picture)", "TXT (only offer)", "plain text lines", "PDF/RTF/HTML/ODT not offered; the text is complete");
    }

    // ------------------------------------------------------------------ PDF through Word

    [SlowFact, Trait("Category", "Slow")]
    public async Task Docx_to_pdf_through_Word_is_a_real_pdf_with_all_the_words()
    {
        if (!Capabilities.Current.WordCom) return;
        using var tmp = new TempDir();
        var input = tmp.WriteFile("report.docx", Fixtures.DocxSample());

        var output = Kit.One(await Kit.RunAsync("PDF", input));

        using var doc = PdfDocument.Open(output);
        var words = string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).Where(t => t is not ("•" or "1." or "2." or "·")));
        Assert.Equal(DocCheck.PlainText(Expected), words);
        var rendered = await PdfCheck.RenderAllAsync(output);
        Assert.All(rendered, p => Assert.True(PdfCheck.InkFraction(p) > 0.001));
        Kit.Record("document", "report.docx", "PDF (Word installed: exported by Word)", "PdfPig text + Windows.Data.Pdf render", $"{doc.NumberOfPages} page(s), all words in order, renders");
    }
}
