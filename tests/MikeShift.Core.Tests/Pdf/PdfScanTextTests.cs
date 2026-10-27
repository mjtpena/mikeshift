using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Pdf;

namespace MikeShift.Core.Tests.Pdf;

/// <summary>
/// PDF TXT on pages without a text layer: they are rendered and read by text recognition, while pages with text still go through PdfPig.
/// Tests that read pictures need a text recognition language (<see cref="Capabilities.Ocr"/>).
/// </summary>
public class PdfScanTextTests
{
    private static readonly string[] Page1 = ["Scanned page one says hello.", "Account number 5532 is closed."];
    private static readonly string[] Page2 = ["Scanned page two says goodbye."];

    private static string[] Lines(string path) => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_pdf_built_from_pictures_gives_their_text_with_a_blank_line_between_pages()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1, Page2));
        using var guard = OriginalsGuard.Watch(pdf);
        Assert.Equal("", PdfText.Extract(File.ReadAllBytes(pdf), default)); // really no text layer

        var output = await PdfConverter.ToTextAsync(pdf, readScans: true, default);

        Assert.Equal(tmp.Combine("scan.txt"), output);
        Assert.Equal([.. Page1, "", .. Page2], Lines(output));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Text_pages_keep_their_text_layer_and_scanned_pages_are_read_between_them()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("mixed.pdf", Fixtures.PdfOfPages(
            PdfFixturePage.Text("Typed page with a text layer."),
            PdfFixturePage.Scan(Page2),
            PdfFixturePage.Text("Another typed page.")));

        var output = await PdfConverter.ToTextAsync(pdf, readScans: true, default);

        Assert.Equal(["Typed page with a text layer.", "", .. Page2, "", "Another typed page."], Lines(output));
    }

    [Fact]
    public async Task A_pdf_with_a_text_layer_gives_the_same_text_with_or_without_reading_scans()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(3));
        var expected = PdfText.Extract(File.ReadAllBytes(pdf), default);

        var plain = File.ReadAllText(await PdfConverter.ToTextAsync(pdf, readScans: false, default));
        var withScans = File.ReadAllText(await PdfConverter.ToTextAsync(pdf, readScans: true, default));

        Assert.Equal(expected, plain);
        Assert.Equal(expected, withScans);
    }

    [Fact]
    public async Task Without_text_recognition_a_scan_is_refused_with_how_to_add_it()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, readScans: false, default));

        Assert.Equal(OcrCopy.ScanWithoutOcr, ex.Message);
        Assert.Contains("probably a scan", ex.Message);
        Assert.Equal(["scan.pdf"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_scan_with_no_readable_text_fails_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("blank.pdf", Fixtures.PdfOfPages(PdfFixturePage.Blank(Fixtures.PngRgb(400, 300))));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, readScans: true, default));

        Assert.Equal(OcrCopy.NoTextInPdf, ex.Message);
        Assert.Equal(["blank.pdf"], FileNames(tmp));
    }

    [Fact]
    public async Task The_PDF_TXT_tile_reads_scans_only_when_the_PC_can()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1));
        var txt = ActionCatalog.Find(PdfActions.All, "TXT")!;

        var result = await txt.Run([pdf], new ActionContext(Capabilities.All with { Ocr = false }), default);

        Assert.Equal(OcrCopy.ScanWithoutOcr, Assert.Single(result.Failures).Message);
        Assert.Equal(["scan.pdf"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_cancelled_scan_job_writes_nothing()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1, Page2));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfConverter.ToTextAsync(pdf, readScans: true, cts.Token));

        Assert.Equal(["scan.pdf"], FileNames(tmp));
    }

    [Fact]
    public void Pages_are_rendered_for_reading_at_300_dpi_within_the_size_limit()
    {
        Assert.Equal((2480, 3508), PdfRender.TargetSize(793.7, 1122.5, PdfRender.TextScale)); // A4
        var (w, h) = PdfRender.TargetSize(5000, 2000, PdfRender.TextScale);
        Assert.Equal(PdfRender.MaxLongSide, w);
        Assert.True(h < w);
    }
}
