using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace MikeShift.Core.Tests.Pdf;

/// <summary>
/// Searchable PDF: scanned pages and pictures get an invisible text layer from text recognition, and look exactly as before. Tests that read
/// pictures need a text recognition language (<see cref="Capabilities.Ocr"/>).
/// </summary>
public class PdfSearchableTests
{
    private static readonly string[] Page1 = ["Scanned page one says hello.", "Account number 5532 is closed."];
    private static readonly string[] Page2 = ["Scanned page two says goodbye."];

    private static ActionDef PdfTile => ActionCatalog.Find(PdfActions.All, "Searchable PDF")!;
    private static ActionDef PictureTile => ActionCatalog.Find(ImageActions.All, "Searchable PDF")!;
    private static ActionDef MergeTile => ActionCatalog.Find(ImageActions.All, "Merge searchable PDF")!;

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>The text PdfPig finds in the PDF's text layers, as the TXT tile would write it (no text recognition).</summary>
    private static string TextLayer(string pdf) => PdfText.Extract(File.ReadAllBytes(pdf), default);

    private static List<(double Width, double Height)> PageSizes(string pdf)
    {
        using var doc = PigDocument.Open(pdf);
        return doc.GetPages().Select(p => (p.Width, p.Height)).ToList();
    }

    private static async Task<byte[][]> Renders(string pdf)
    {
        var doc = await PdfRender.OpenAsync(File.ReadAllBytes(pdf));
        var pages = new List<byte[]>();
        for (uint i = 0; i < doc.PageCount; i++) pages.Add((await PdfRender.RenderPageForTextAsync(doc, i)).Bgra);
        return [.. pages];
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_scanned_pdf_gets_a_text_layer_that_reads_back_as_the_recognised_text()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1, Page2));
        using var guard = OriginalsGuard.Watch(pdf);
        Assert.Equal("", TextLayer(pdf));

        var output = await PdfConverter.MakeSearchableAsync(pdf, default);

        Assert.Equal(tmp.Combine("scan searchable.pdf"), output);
        // PdfPig (what the TXT tile reads) finds the same text text recognition read from the scan.
        var recognised = File.ReadAllText(await PdfConverter.ToTextAsync(pdf, readScans: true, default));
        Assert.Equal(recognised, TextLayer(output));
        Assert.Equal([.. Page1, "", .. Page2], Lines(TextLayer(output)));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task The_searchable_pdf_renders_exactly_like_the_scan_with_the_same_pages_and_sizes()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1, Page2));

        var output = await PdfConverter.MakeSearchableAsync(pdf, default);

        Assert.Equal(PageSizes(pdf), PageSizes(output));
        Assert.Equal(2, PageSizes(output).Count);
        var before = await Renders(pdf);
        var after = await Renders(output);
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++) Assert.True(before[i].AsSpan().SequenceEqual(after[i]), $"page {i + 1} renders differently");
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Each_word_sits_where_it_is_on_the_page()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1));

        var output = await PdfConverter.MakeSearchableAsync(pdf, default);

        using var doc = PigDocument.Open(output);
        var page = doc.GetPage(1);
        var words = page.GetWords().ToList();
        Assert.All(words, w =>
        {
            Assert.InRange(w.BoundingBox.Left, 0, page.Width);
            Assert.InRange(w.BoundingBox.Right, 0, page.Width);
            Assert.InRange(w.BoundingBox.Bottom, 0, page.Height);
            Assert.InRange(w.BoundingBox.Top, 0, page.Height);
        });
        // The picture is placed 40 points from the top, so line one is above line two, and "Scanned" starts the first line.
        var scanned = words.First(w => w.Text == "Scanned");
        var account = words.First(w => w.Text == "Account");
        Assert.True(scanned.BoundingBox.Bottom > account.BoundingBox.Bottom);
        Assert.True(scanned.BoundingBox.Left < page.Width / 3);
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Pages_that_already_have_text_are_kept_as_they_are_and_only_the_scans_are_read()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("mixed.pdf", Fixtures.PdfOfPages(
            PdfFixturePage.Text("Typed page with a text layer."),
            PdfFixturePage.Scan(Page2)));

        var output = await PdfConverter.MakeSearchableAsync(pdf, default);

        Assert.Equal(["Typed page with a text layer.", "", .. Page2], Lines(TextLayer(output)));
        var before = await Renders(pdf);
        var after = await Renders(output);
        Assert.True(before[0].AsSpan().SequenceEqual(after[0]));
        Assert.True(before[1].AsSpan().SequenceEqual(after[1]));
    }

    /// <summary>
    /// Turned pages, with and without a crop box. The scan is a picture stored turned so that the page's <c>/Rotate</c> shows it upright; the
    /// word "Account" is found by text recognition in the rendered input (where the viewer shows it) and in the output's text layer (PdfPig,
    /// in unrotated page coordinates measured from the crop box's corner), and the two must be the same place.
    /// </summary>
    [SkipIfNoCapabilityTheory(nameof(Capabilities.Ocr))]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    [InlineData(270, false)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(180, true)]
    [InlineData(270, true)]
    public async Task A_turned_scan_gets_its_words_where_the_viewer_shows_them(int rotation, bool cropped)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("turned.pdf", Fixtures.PdfOfPages(PdfFixturePage.Blank(TurnedPicture(Page1, rotation))));
        double pageWidth = 595.276, pageHeight = 841.89; // A4 points, as the fixture writes it
        using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
        {
            doc.Pages[0].Rotate = rotation;
            if (cropped)
            {
                doc.Pages[0].CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(30, 60), new PdfSharp.Drawing.XPoint(560, 840));
                (pageWidth, pageHeight) = (530, 780);
            }
            doc.Save(path);
        }

        var output = await PdfConverter.MakeSearchableAsync(path, default);

        // Where the viewer shows the word: text recognition on the rendered input, pixels of a 300 dpi render to points.
        var rendered = await PdfRender.OpenAsync(File.ReadAllBytes(path));
        var raster = await PdfRender.RenderPageForTextAsync(rendered, 0);
        var read = await MikeShift.Core.Images.TextRecognition.RecognizeAsync(raster, default);
        var seen = read.Lines.SelectMany(l => l.Words).First(w => w.Text == "Account");
        var shown = (X: (seen.X + seen.Width / 2) * 72 / 300, Y: (seen.Y + seen.Height / 2) * 72 / 300);

        // Where the text layer puts it: PdfPig reports the page as shown (turned, from the crop box's corner, origin bottom-left).
        using var read2 = PigDocument.Open(output);
        var box = read2.GetPage(1).GetWords().First(w => w.Text == "Account").BoundingBox;
        var shownHeight = rotation is 90 or 270 ? pageWidth : pageHeight;
        var placed = (X: (box.Left + box.Right) / 2, Y: shownHeight - (box.Top + box.Bottom) / 2);

        Assert.InRange(Math.Abs(placed.X - shown.X), 0, 8);
        Assert.InRange(Math.Abs(placed.Y - shown.Y), 0, 8);
        var before = await Renders(path);
        var after = await Renders(output);
        Assert.True(before[0].AsSpan().SequenceEqual(after[0]));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_scan_shown_through_a_crop_box_gets_its_text_where_it_is_shown()
    {
        using var tmp = new TempDir();
        var whole = tmp.WriteFile("whole.pdf", Fixtures.PdfScanOfText(Page1));
        var cropped = tmp.WriteFile("cropped.pdf", Fixtures.PdfScanOfText(Page1));
        using (var doc = PdfReader.Open(cropped, PdfDocumentOpenMode.Modify))
        {
            doc.Pages[0].CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(30, 400), new PdfSharp.Drawing.XPoint(560, 800));
            doc.Save(cropped);
        }

        var a = await PdfConverter.MakeSearchableAsync(whole, default);
        var b = await PdfConverter.MakeSearchableAsync(cropped, default);

        // The scan is the same, so a word sits at the same place on the page itself with or without the crop box. PdfPig measures from the
        // crop box's corner (30 and 400 points here), and the two reads of the scan can differ a little.
        using var docA = PigDocument.Open(a);
        using var docB = PigDocument.Open(b);
        var wa = docA.GetPage(1).GetWords().First(w => w.Text == "Account").BoundingBox;
        var wb = docB.GetPage(1).GetWords().First(w => w.Text == "Account").BoundingBox;
        Assert.InRange(Math.Abs(wa.Left - (wb.Left + 30)), 0, 6);
        Assert.InRange(Math.Abs(wa.Bottom - (wb.Bottom + 400)), 0, 6);
    }

    /// <summary>A page-shaped picture of <paramref name="lines"/> turned anticlockwise by <paramref name="rotation"/>, so a page turned clockwise by it shows the text upright.</summary>
    private static byte[] TurnedPicture(string[] lines, int rotation)
    {
        using var text = SKImage.FromEncodedData(Fixtures.TextPicture(lines, fontSize: 48));
        using var surface = SKSurface.Create(new SKImageInfo(1240, 1754, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        switch (rotation)
        {
            case 90: surface.Canvas.Translate(0, 1754); surface.Canvas.RotateDegrees(-90); break;
            case 180: surface.Canvas.Translate(1240, 1754); surface.Canvas.RotateDegrees(180); break;
            case 270: surface.Canvas.Translate(1240, 0); surface.Canvas.RotateDegrees(90); break;
        }
        surface.Canvas.DrawImage(text, 40, 40, new SKSamplingOptions());
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Right-to-left lines: the words go into the layer in reading order and read back in it (no text recognition needed: the words are given).
    [Theory]
    [InlineData("שלום", "עולם")]
    [InlineData("مرحبا", "بالعالم")]
    public void Right_to_left_words_read_back_in_reading_order_from_the_text_layer(string first, string second)
    {
        if (!PdfFonts.IsInstalled("Arial")) return;
        using var tmp = new TempDir();
        var bytes = Fixtures.PdfOfPages(PdfFixturePage.Text("x"));
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        // One line, as text recognition reports a right-to-left line: the first word is the rightmost.
        var words = new List<MikeShift.Core.Images.RecognizedWord>
        {
            new(first, 400, 100, 90, 30),
            new(second, 250, 100, 90, 30),
        };
        var text = new MikeShift.Core.Images.RecognizedText(600, 800, [new MikeShift.Core.Images.RecognizedLine(first + " " + second, words)]);

        var added = PdfSearchable.AddInvisibleText(page, text, new PdfSharp.Drawing.XRect(0, 0, 600, 800));
        var path = tmp.Combine("rtl.pdf");
        doc.Save(path);

        Assert.Equal(2, added);
        var extracted = PdfText.ExtractPages(File.ReadAllBytes(path), default)[0];
        Assert.EndsWith(first + " " + second, extracted.Replace("\r\n", "\n").Split('\n').Last());
    }

    [Fact]
    public async Task A_pdf_with_text_on_every_page_is_refused_as_already_searchable()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(2));
        using var guard = OriginalsGuard.Watch(pdf);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MakeSearchableAsync(pdf, default));

        Assert.Equal(OcrCopy.AlreadySearchable, ex.Message);
        Assert.Contains("already searchable", ex.Message);
        Assert.Equal(["doc.pdf"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_scan_with_no_readable_text_is_refused_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("blank.pdf", Fixtures.PdfOfPages(PdfFixturePage.Blank(Fixtures.PngRgb(400, 300))));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MakeSearchableAsync(pdf, default));

        Assert.Equal(OcrCopy.NoTextInPdf, ex.Message);
        Assert.Equal(["blank.pdf"], FileNames(tmp));
    }

    [Fact]
    public async Task An_encrypted_or_damaged_pdf_is_refused_with_the_usual_message()
    {
        using var tmp = new TempDir();
        var good = Fixtures.PdfScanOfText(Page1);
        var damaged = tmp.WriteFile("damaged.pdf", good[..(good.Length / 2)]);
        var notPdf = tmp.WriteFile("notpdf.pdf", Fixtures.PngRgb(20, 20));

        var ex1 = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MakeSearchableAsync(damaged, default));
        var ex2 = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MakeSearchableAsync(notPdf, default));

        Assert.Equal(PdfGuard.DamagedMessage, ex1.Message);
        Assert.Equal(PdfGuard.NotPdfMessage, ex2.Message);
        Assert.Equal(["damaged.pdf", "notpdf.pdf"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_cancelled_job_writes_nothing()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScanOfText(Page1, Page2));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfConverter.MakeSearchableAsync(pdf, cts.Token));

        Assert.Equal(["scan.pdf"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_picture_becomes_a_searchable_pdf_with_its_text()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("receipt.png", Fixtures.TextPicture(Fixtures.OcrLines));
        using var guard = OriginalsGuard.Watch(png);

        var output = await PdfConverter.ImageToSearchablePdfAsync(png, default);

        Assert.Equal(tmp.Combine("receipt searchable.pdf"), output);
        Assert.Equal(Fixtures.OcrLines, Lines(TextLayer(output)));
        var plain = await PdfConverter.ImageToPdfAsync(png, default);
        Assert.Equal(PageSizes(plain), PageSizes(output)); // same page as the plain PDF tile makes
        var before = await Renders(plain);
        var after = await Renders(output);
        Assert.True(before[0].AsSpan().SequenceEqual(after[0]));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Pictures_merge_into_one_searchable_pdf_in_the_order_given()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.png", Fixtures.TextPicture(Page1));
        var b = tmp.WriteFile("b.png", Fixtures.TextPicture(Page2));

        var output = await PdfConverter.MergeImagesSearchableAsync([b, a], default);

        Assert.Equal(tmp.Combine("Merged searchable.pdf"), output);
        Assert.Equal([.. Page2, "", .. Page1], Lines(TextLayer(output)));
        Assert.Equal(2, PageSizes(output).Count);
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_picture_without_text_is_refused_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("colours.png", Fixtures.PngRgb(400, 300));
        var other = tmp.WriteFile("other.png", Fixtures.PngRgb(300, 300));

        var one = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ImageToSearchablePdfAsync(png, default));
        var many = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeImagesSearchableAsync([png, other], default));

        Assert.Equal(OcrCopy.NoTextInPicture, one.Message);
        Assert.Equal(OcrCopy.NoTextInPictures, many.Message);
        Assert.Equal(["colours.png", "other.png"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task The_tiles_keep_the_outputs_already_written_when_one_file_fails()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("a scan.pdf", Fixtures.PdfScanOfText(Page1));
        var typed = tmp.WriteFile("b typed.pdf", Fixtures.PdfText(1));

        var result = await PdfTile.Run([good, typed], new ActionContext(Capabilities.All), default);

        Assert.Equal(tmp.Combine("a scan searchable.pdf"), Assert.Single(result.Outputs).Path);
        Assert.Equal(OcrCopy.AlreadySearchable, Assert.Single(result.Failures).Message);
    }

    [Fact]
    public void The_tiles_need_text_recognition_and_merge_needs_two_pictures()
    {
        var withOcr = Capabilities.All;
        var withoutOcr = Capabilities.All with { Ocr = false };
        string[] one = [@"C:\x\a.png"];
        string[] two = [@"C:\x\a.png", @"C:\x\b.png"];
        string[] pdf = [@"C:\x\a.pdf"];

        Assert.True(PdfTile.IsVisibleFor(pdf, withOcr));
        Assert.False(PdfTile.IsVisibleFor(pdf, withoutOcr));
        Assert.True(PictureTile.IsVisibleFor(one, withOcr));
        Assert.False(PictureTile.IsVisibleFor(one, withoutOcr));
        Assert.False(MergeTile.IsVisibleFor(one, withOcr));
        Assert.True(MergeTile.IsVisibleFor(two, withOcr));
        Assert.False(MergeTile.IsVisibleFor(two, withoutOcr));
        Assert.Equal(WheelPage.Format, PictureTile.Page);
        Assert.Equal(WheelPage.Tool, PdfTile.Page);
        Assert.Equal(WheelPage.Tool, MergeTile.Page);
    }

    [Theory]
    [InlineData("Hello", "Arial")]
    [InlineData("Привет", "Arial")]
    [InlineData("שלום", "Arial")]
    [InlineData("مرحبا", "Arial")]
    public void Latin_Greek_Cyrillic_Hebrew_and_Arabic_words_use_the_default_font(string word, string expected)
    {
        if (!PdfFonts.IsInstalled(expected)) return;
        Assert.Equal(expected, PdfSearchable.FamilyOf(word));
    }

    [Fact]
    public void Words_in_a_script_with_no_installed_font_have_no_family_and_are_left_out()
    {
        // Every script the document writer has a fallback for resolves to an installed font, or to nothing when this PC lacks it.
        foreach (var word in new[] { "日本語", "한국어", "हिन्दी", "ไทย" })
        {
            var family = PdfSearchable.FamilyOf(word);
            Assert.True(family is null || PdfFonts.IsInstalled(family), word);
        }
    }
}
