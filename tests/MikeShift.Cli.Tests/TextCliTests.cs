using MikeShift.Core;

namespace MikeShift.Cli.Tests;

/// <summary>
/// TXT from pictures and scanned PDFs end to end through the real executable. These read generated pictures of known text with the Windows
/// OCR engine, so they are skipped on a PC without a text recognition language.
/// </summary>
public class TextCliTests
{
    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public void List_on_a_picture_offers_TXT()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var r = CliProcess.Run("--list", png);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("TXT", r.Stdout.Trim().Split(", "));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public void Run_TXT_on_a_picture_writes_its_text_next_to_it()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("scan.png", Fixtures.TextPicture(Fixtures.OcrLines));
        using var guard = OriginalsGuard.Watch(png);

        var r = CliProcess.Run("--run", "TXT", png);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(tmp.Combine("scan.txt"), Assert.Single(r.StdoutLines));
        Assert.Equal(Fixtures.OcrLines, File.ReadAllText(tmp.Combine("scan.txt")).Replace("\r\n", "\n").Split('\n'));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public void Run_TXT_on_a_scanned_pdf_reads_the_scan()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Fixtures.OcrLines));

        var r = CliProcess.Run("--run", "TXT", pdf);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(tmp.Combine("scan.txt"), Assert.Single(r.StdoutLines));
        Assert.Contains(Fixtures.OcrLines[1], File.ReadAllText(tmp.Combine("scan.txt")));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public void Run_TXT_on_a_picture_without_text_exits_1_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("colours.png", Fixtures.PngRgb(400, 300));

        var r = CliProcess.Run("--run", "TXT", png);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("Couldn't find any text in this picture.", r.Stderr);
        Assert.Equal(["colours.png"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }
}
