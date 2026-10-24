using MikeShift.Core;
using System.Text;

namespace MikeShift.Cli.Tests;

/// <summary>Documents end to end through the real executable.</summary>
[Collection("WordCom")] // some tests below start (or look for) a real Word; they never overlap with each other
public class DocumentCliTests
{
    [Fact]
    public void List_on_a_docx_offers_every_other_format_but_not_docx()
    {
        using var tmp = new TempDir();
        var docx = tmp.WriteFile("sample.docx", Fixtures.DocxSample());

        var r = CliProcess.Run("--list", docx);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("PDF, RTF, TXT, HTML, ODT, Zip", r.Stdout.Trim());
    }

    [Fact]
    public void List_on_a_docx_with_a_picture_offers_txt_only()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--list", tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture()));
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("TXT, Zip", r.Stdout.Trim());
    }

    [Fact]
    public void Run_pdf_on_an_rtf_writes_a_real_pdf_next_to_it()
    {
        using var tmp = new TempDir();
        var rtf = tmp.WriteFile("report.rtf", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        using var guard = OriginalsGuard.Watch(rtf);

        var r = CliProcess.Run("--run", "PDF", rtf);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("report.pdf"), output);
        var bytes = File.ReadAllBytes(output);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.Contains("Quarterly Report", string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)));
    }

    [Fact]
    public void Run_txt_on_a_hebrew_windows_1255_file_and_on_markdown()
    {
        using var tmp = new TempDir();
        var heb = tmp.WriteFile("heb.txt", Fixtures.DocTxtHebrew1255());
        var md = tmp.WriteFile("notes.md", Fixtures.MarkdownSample());

        var r = CliProcess.Run("--run", "DOCX", heb, md);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal([tmp.Combine("heb.docx"), tmp.Combine("notes.docx")], r.StdoutLines);
        Assert.All(r.StdoutLines, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public void Run_txt_on_a_picture_document_works_and_other_formats_are_unknown_actions()
    {
        using var tmp = new TempDir();
        var pic = tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture());

        var txt = CliProcess.Run("--run", "TXT", pic);
        Assert.Equal(0, txt.ExitCode);
        Assert.Contains("Quarterly Report", File.ReadAllText(Assert.Single(txt.StdoutLines)));

        var pdf = CliProcess.Run("--run", "PDF", pic);
        Assert.Equal(1, pdf.ExitCode);
        Assert.Contains("unknown action", pdf.Stderr);
    }

    [Fact]
    public void A_corrupt_docx_is_reported_as_an_error_with_a_plain_message()
    {
        using var tmp = new TempDir();
        var bad = tmp.WriteFile("bad.docx", "definitely not a zip file");

        // The wheel hides every format for it (only Zip is offered)...
        Assert.Equal("Zip", CliProcess.Run("--list", bad).Stdout.Trim());
        Assert.Equal(1, CliProcess.Run("--run", "PDF", bad).ExitCode);
    }

    [Fact]
    public void Same_extension_is_skipped_and_reported_in_a_mixed_selection()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.txt", "alpha");
        var b = tmp.WriteFile("b.html", "<p>beta</p>");

        var r = CliProcess.Run("--run", "TXT", a, b);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal([tmp.Combine("b.txt")], r.StdoutLines);
        Assert.Contains("1 already TXT", r.Stderr);
    }

    // ------------------------------------------------------------------ Word is opt-in (--use-word)

    [Fact]
    public void A_legacy_doc_is_zip_only_without_use_word_even_when_Word_is_installed()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("old.doc", Fixtures.Noise(256));

        var r = CliProcess.Run("--list", doc);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("Zip", r.Stdout.Trim());

        var run = CliProcess.Run("--run", "PDF", doc);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("unknown action", run.Stderr);
    }

    [SkipIfNoCapability(nameof(Capabilities.WordInstalled))]
    public async Task Without_use_word_the_CLI_never_starts_Word_for_docx_to_pdf()
    {
        using var tmp = new TempDir();
        var docx = tmp.WriteFile("report.docx", Fixtures.DocxSample());

        var seen = await WordWatch.RunAsync(TimeSpan.FromMinutes(2), "--run", "PDF", docx);

        Assert.Equal(0, seen.Result.ExitCode);
        Assert.Empty(seen.NewWordProcesses);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(Assert.Single(seen.Result.StdoutLines));
        Assert.Contains("Quarterly Report", string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)));
    }

    [SkipIfNoCapability(nameof(Capabilities.WordInstalled))]
    public async Task Without_use_word_the_CLI_never_starts_Word_for_a_legacy_doc_either()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));

        var seen = await WordWatch.RunAsync(TimeSpan.FromMinutes(2), "--run", "TXT", doc);

        Assert.Equal(1, seen.Result.ExitCode); // TXT is not offered for a .doc without Word
        Assert.Empty(seen.NewWordProcesses);
    }

    /// <summary>The positive control of the two tests above: the same watcher does see Word when the run asks for it.</summary>
    [SlowFact, Trait("Category", "Slow")]
    public async Task With_use_word_a_legacy_doc_is_converted_by_Word()
    {
        if (!Capabilities.Current.WordInstalled) return;
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));

        var list = CliProcess.Run("--use-word", "--list", doc);
        Assert.Equal("PDF, DOCX, RTF, TXT, HTML, ODT, Zip", list.Stdout.Trim());

        var seen = await WordWatch.RunAsync(TimeSpan.FromMinutes(3), "--use-word", "--run", "TXT", doc);

        Assert.Equal(0, seen.Result.ExitCode);
        Assert.NotEmpty(seen.NewWordProcesses); // Word really was started (so the empty sets above mean something)
        Assert.Contains("Quarterly Report", File.ReadAllText(Assert.Single(seen.Result.StdoutLines)));
    }
}
