using System.Text;
using MikeShift.Core.Actions;
using MikeShift.Core.Documents;

namespace MikeShift.Core.Tests.Documents;

public class DocumentProbeTests
{
    private static string[] Titles(Capabilities caps, params string[] paths) =>
        ActionCatalog.For(paths, caps).Select(a => a.Title).ToArray();

    [Fact]
    public void Plain_documents_offer_every_other_format()
    {
        using var tmp = new TempDir();
        Assert.Equal(["PDF", "DOCX", "RTF", "HTML", "ODT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.txt", "x")));
        Assert.Equal(["PDF", "RTF", "TXT", "HTML", "ODT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.docx", Fixtures.DocxSample())));
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "HTML", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.odt", Fixtures.OdtSample())));
        Assert.Equal(["PDF", "DOCX", "TXT", "HTML", "ODT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.rtf", Encoding.ASCII.GetBytes(Fixtures.RtfSample()))));
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "ODT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.htm", Fixtures.HtmlSample())));
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "HTML", "ODT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("a.md", Fixtures.MarkdownSample())));
    }

    [Fact]
    public void A_document_with_pictures_is_offered_as_txt_only()
    {
        using var tmp = new TempDir();
        Assert.Equal(["TXT", "Zip"], Titles(Capabilities.All, tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture())));
        Assert.Equal(["TXT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("pic.odt", Fixtures.OdtWithPicture())));
        Assert.Equal(["TXT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("pic.rtf", Encoding.ASCII.GetBytes(@"{\rtf1 text {\pict\pngblip 89504e47}}"))));
        Assert.Equal(["TXT", "Zip"], Titles(Capabilities.None, tmp.WriteFile("pic.html", "<p>x</p><IMG SRC='a.png'>")));
    }

    [Fact]
    public void One_picture_file_in_a_selection_restricts_the_whole_selection()
    {
        using var tmp = new TempDir();
        var plain = tmp.WriteFile("a.docx", Fixtures.DocxSample());
        var pic = tmp.WriteFile("b.docx", Fixtures.DocxWithPicture());
        Assert.Equal(["TXT", "Zip"], Titles(Capabilities.None, plain, pic));
        Assert.Equal(["PDF", "RTF", "TXT", "HTML", "ODT", "Zip"], Titles(Capabilities.None, plain));
    }

    [Fact]
    public void Files_already_in_the_target_format_do_not_count_against_the_selection()
    {
        using var tmp = new TempDir();
        var pic = tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture());
        var txt = tmp.WriteFile("a.txt", "hello");
        // The picture DOCX allows TXT; the TXT file is simply skipped by the action ("already TXT").
        Assert.Contains("TXT", Titles(Capabilities.None, pic, txt));
        Assert.DoesNotContain("PDF", Titles(Capabilities.None, pic, txt));
    }

    [Fact]
    public void Docx_and_odt_must_start_with_PK()
    {
        using var tmp = new TempDir();
        Assert.Equal(["Zip"], Titles(Capabilities.None, tmp.WriteFile("fake.docx", "not a zip at all")));
        Assert.Equal(["Zip"], Titles(Capabilities.None, tmp.WriteFile("fake.odt", Encoding.ASCII.GetBytes(Fixtures.RtfSample()))));
    }

    [Fact]
    public void Legacy_doc_needs_word()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("old.doc", Fixtures.Noise(128));
        Assert.Equal(["Zip"], Titles(Capabilities.None, doc));
        Assert.Equal(["Zip"], Titles(Capabilities.All with { WordCom = false }, doc));
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "HTML", "ODT", "Zip"], Titles(Capabilities.All, doc));
    }

    [Fact]
    public void Unreadable_or_missing_files_never_hide_tiles()
    {
        Assert.True(DocumentProbe.CanConvertTo([@"C:\no\such\folder\a.docx"], DocFormat.Txt, Capabilities.None));
        Assert.True(DocumentProbe.CanConvertTo([@"C:\no\such\a.rtf"], DocFormat.Pdf, Capabilities.None));
    }

    [Fact]
    public void Probe_does_not_read_cloud_placeholders()
    {
        using var tmp = new TempDir();
        // A fake that would be rejected if read (not a zip), flagged like a OneDrive online-only file.
        var path = tmp.WriteFile("cloud.docx", "not a zip");
        Assert.False(DocumentProbe.CanConvertTo([path], DocFormat.Pdf, Capabilities.None)); // read normally: rejected

        DocumentProbe.ClearCache();
        var original = DocumentProbe.GetAttributes;
        try
        {
            DocumentProbe.GetAttributes = _ => Kind.RecallOnDataAccess | FileAttributes.Archive;
            Assert.True(DocumentProbe.CanConvertTo([path], DocFormat.Pdf, Capabilities.None)); // placeholder: not read, not hidden
        }
        finally
        {
            DocumentProbe.GetAttributes = original;
            DocumentProbe.ClearCache();
        }
    }

    [Fact]
    public void Probe_verdicts_are_cached_but_follow_file_changes()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("c.html", "<p>x</p>");
        Assert.True(DocumentProbe.CanConvertTo([path], DocFormat.Pdf, Capabilities.None));
        File.WriteAllText(path, "<p>x</p><img src=a.png>");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.False(DocumentProbe.CanConvertTo([path], DocFormat.Pdf, Capabilities.None));
    }

    [Fact]
    public void Picture_scans_find_markers_across_buffer_boundaries()
    {
        using var tmp = new TempDir();
        var filler = new string('x', 65536 - 3);
        var path = tmp.WriteFile("edge.html", filler + "<img src=a>");
        Assert.True(PictureScan.HtmlHasPictures(path));
        Assert.False(PictureScan.HtmlHasPictures(tmp.WriteFile("none.html", filler + "<p>img</p>")));
    }

    [Fact]
    public void Document_actions_have_the_documented_order_and_hide_the_same_extension()
    {
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "HTML", "ODT"], DocumentActions().Select(a => a.Title).ToArray());
        Assert.All(DocumentActions(), a => Assert.Equal(WheelPage.Format, a.Page));
        Assert.Contains("htm", DocumentActions().Single(a => a.Title == "HTML").HideFor);
        Assert.DoesNotContain(DocumentActions(), a => a.Title == "PDF" && a.HideFor.Count > 0);
    }

    private static IReadOnlyList<ActionDef> DocumentActions() => ActionCatalog.ListFor(Category.Document);

    [Fact]
    public async Task Batch_run_converts_each_file_and_skips_files_already_in_the_format()
    {
        using var tmp = new TempDir();
        var docx = tmp.WriteFile("a.docx", Fixtures.DocxSample());
        var txt = tmp.WriteFile("b.txt", "plain");
        var bad = tmp.WriteFile("c.docx", "garbage");
        var action = DocumentActions().Single(a => a.Title == "TXT");

        var result = await action.Run([docx, txt, bad], new ActionContext(Capabilities.None, NoInteraction.Instance), CancellationToken.None);

        Assert.Equal(1, result.AlreadyInFormat);
        Assert.Single(result.Outputs);
        Assert.Equal(tmp.Combine("a.txt"), result.Outputs[0].Path);
        Assert.Single(result.Failures);
        Assert.Equal(bad, result.Failures[0].Input);
    }
}
