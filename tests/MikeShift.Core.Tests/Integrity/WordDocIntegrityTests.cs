using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Legacy .doc input (needs Microsoft Word) converted to every document format. Word is the reader here, so the check is the one a
/// user would make: every word of the document arrives, in order, in every output. Runs Word, so it is a Slow test, and it shares the
/// Word collection with the other tests that start Word.
/// </summary>
[Collection(WordCollection.Name)]
[Trait("Category", "Slow")]
public class WordDocIntegrityTests
{
    public static TheoryData<string> Titles() => new() { "PDF", "DOCX", "RTF", "TXT", "HTML", "ODT" };

    [SlowTheory, MemberData(nameof(Titles))]
    public async Task A_doc_file_converts_with_all_its_words(string title)
    {
        if (!Capabilities.Current.WordInstalled) return;
        using var wordOn = Kit.WithCaps(Capabilities.Current.WithWord(true)); // Word is opt-in: this test is the opt-in
        using var tmp = new TempDir();
        // Word opens RTF saved with a .doc extension as a Word document.
        var input = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));

        var output = Kit.One(await Kit.RunAsync(title, input));

        var expected = DocCheck.PlainText(Fixtures.DocumentOutline);
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
            case "HTML":
                got = DocCheck.PlainText(DocCheck.HtmlOutline(File.ReadAllText(output)));
                break;
            case "RTF":
                got = DocCheck.PlainText(DocCheck.RtfOutline(File.ReadAllText(output, Encoding.GetEncoding(1252))));
                break;
            case "TXT":
                got = File.ReadAllText(output);
                break;
            default:
            {
                using var pdf = PdfDocument.Open(output);
                got = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).Where(t => t is not ("•" or "1." or "2." or "·")));
                Assert.All(await PdfCheck.RenderAllAsync(output), p => Assert.True(PdfCheck.InkFraction(p) > 0.001));
                break;
            }
        }
        // The fixture's RTF bullet glyph is not a real character, so Word draws it as "?"; list markers are not part of the words.
        got = Regex.Replace(got, @"(?<![\w])\?(?![\w])|\b[12]\.(?= [A-Z])|•", "");
        Assert.Equal(expected, Regex.Replace(got, @"\s+", " ").Trim());
        Kit.Record("document", "legacy.doc (read by Word)", title, "per-format reader; every word in order", "all words identical to the source text");
    }
}
