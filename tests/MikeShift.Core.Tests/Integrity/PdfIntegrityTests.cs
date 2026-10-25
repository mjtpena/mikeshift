using MikeShift.Core.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Every action the catalog offers for a PDF (PNG, JPG, TXT, Compress, Split, Merge), judged by PdfPig (structure and text) and by
/// the Windows PDF renderer (does every page draw), plus Skia/WIC decoding of the page images.
/// </summary>
public class PdfIntegrityTests
{
    private const double DipPerPoint = 96.0 / 72.0;

    // ------------------------------------------------------------------ fixtures

    /// <summary>A text PDF whose every page says exactly which document and page it is.</summary>
    private static byte[] LabelledPdf(string label, int pages)
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var font = new XFont("Arial", 14);
        for (var p = 1; p <= pages; p++)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(PageHeading(label, p, pages), font, XBrushes.Black, 60, 80);
            gfx.DrawString("The quick brown fox jumps over the lazy dog.", font, XBrushes.Black, 60, 120);
            gfx.DrawString(PageFooter(label, p), font, XBrushes.Black, 60, 150);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static string PageHeading(string label, int p, int n) => $"{label} page {p} of {n}";

    private static string PageFooter(string label, int p) => $"Second line of {label} on page {p}.";

    private static string PageText(string label, int p, int n) =>
        $"{PageHeading(label, p, n)} The quick brown fox jumps over the lazy dog. {PageFooter(label, p)}";

    /// <summary>Latin accents, Greek and a few symbols in a real PDF font, to prove text extraction does not mangle non-ASCII.</summary>
    private static byte[] UnicodePdf()
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString(UnicodeLine, new XFont("Arial", 14), XBrushes.Black, 60, 80);
            gfx.DrawString("Price: 5€ — “quoted”", new XFont("Arial", 14), XBrushes.Black, 60, 120);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private const string UnicodeLine = "Café naïve façade Ελληνικά";

    public static TheoryData<string> Sources() => new() { "text3", "image3", "landscape1", "flate1" };

    private static byte[] Make(string id) => id switch
    {
        "text3" => Fixtures.PdfText(3),
        "image3" => Fixtures.PdfImageHeavy(3),
        "landscape1" => Fixtures.PdfLandscape(),
        "flate1" => Fixtures.PdfFlateImage(),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static int PageCount(string id) => id switch { "text3" or "image3" => 3, _ => 1 };

    // ------------------------------------------------------------------ the source PDFs are what we think

    [Theory, MemberData(nameof(Sources))]
    public async Task Fixture_is_a_valid_pdf_for_two_independent_readers(string id)
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("src.pdf", Make(id));
        var pages = PdfCheck.Read(pdf);
        Assert.Equal(PageCount(id), pages.Count);
        var rendered = await PdfCheck.RenderAllAsync(pdf);
        Assert.Equal(PageCount(id), rendered.Count);
        Assert.All(rendered, p => Assert.True(PdfCheck.InkFraction(p) > 0.001));
        Kit.Record("pdf", id + ".pdf", "(source)", "PdfPig + Windows.Data.Pdf", $"{pages.Count} page(s)");
    }

    // ------------------------------------------------------------------ PNG / JPG

    public static TheoryData<string, string> ImageCases()
    {
        var d = new TheoryData<string, string>();
        foreach (var s in new[] { "text3", "image3", "landscape1", "flate1" })
            foreach (var f in new[] { "PNG", "JPG" })
                d.Add(s, f);
        return d;
    }

    [Theory, MemberData(nameof(ImageCases))]
    public async Task Pages_to_images_have_the_right_count_size_and_content(string id, string title)
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Make(id));
        var pagesInfo = PdfCheck.Read(pdf);
        var n = pagesInfo.Count;
        var ext = title == "PNG" ? ".png" : ".jpg";

        var output = Kit.One(await Kit.RunAsync(title, pdf));

        string[] files;
        if (n == 1)
        {
            Assert.Equal(tmp.Combine("doc" + ext), output);
            files = [output];
        }
        else
        {
            Assert.Equal(tmp.Combine("doc pages"), output);
            files = Directory.GetFiles(output).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.Equal(Enumerable.Range(1, n).Select(i => Path.Combine(output, $"doc {i:00}{ext}")).ToArray(), files);
        }

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(title == "PNG" ? "png" : "jpeg", ImageBytes.Sniff(files[i]));
            var skia = PixDecode.Skia(files[i])!;
            var wic = await PixDecode.WicAsync(files[i]);
            Assert.Equal((skia.W, skia.H), (wic.W, wic.H));
            if (title == "PNG") Assert.Equal(0, PixMetrics.MaxDiff(skia, wic));
            else Assert.True(PixMetrics.Psnr(skia, wic) >= 35);

            // Size: 2x the page's natural size (96 dpi), at most 8000 px on the long side.
            var (ew, eh) = (pagesInfo[i].Width * DipPerPoint * 2, pagesInfo[i].Height * DipPerPoint * 2);
            var cap = Math.Min(1.0, 8000 / Math.Max(ew, eh));
            Assert.InRange(skia.W, ew * cap - 2, ew * cap + 2);
            Assert.InRange(skia.H, eh * cap - 2, eh * cap + 2);

            // Content: a text page has ink where PDFsharp put the text (left margin 60 pt, first baseline 80 pt) and white elsewhere.
            if (id.StartsWith("text") || id == "landscape1")
            {
                var ink = PdfCheck.InkBounds(skia)!.Value;
                var k = DipPerPoint * 2;
                Assert.InRange(ink.X, 55 * k, 66 * k);
                Assert.InRange(ink.Y, 55 * k, 85 * k);
                Assert.True(PdfCheck.InkFraction(skia) is > 0.0002 and < 0.2);
            }
            else
            {
                Assert.True(PdfCheck.InkFraction(skia) > 0.3, "an image page should be mostly ink");
            }
        }

        if (id == "image3")
            await AssertImagePageMatchesSource(files, title);

        // JPG pages are the PNG pages re-encoded: they must agree.
        if (title == "JPG" && n > 0)
        {
            var png = Kit.One(await Kit.RunAsync("PNG", pdf));
            var pngFirst = n == 1 ? png : Directory.GetFiles(png).OrderBy(f => f, StringComparer.Ordinal).First();
            var p = PixMetrics.Psnr(PixDecode.Skia(pngFirst)!, PixDecode.Skia(files[0])!);
            Assert.True(p >= 26, $"JPG page differs from the PNG page ({p:0.0} dB)"); // the "flate1" picture is pure grain, the worst case for JPEG
        }
        Kit.Record("pdf", id + ".pdf", title, "Skia+WIC decode, PdfPig page sizes, ink position", $"{n} page image(s), sizes = 2x natural (96 dpi)");
    }

    /// <summary>The photo placed on each image page (known from the fixture) is visible at the right place and similar.</summary>
    private static Task AssertImagePageMatchesSource(string[] files, string title)
    {
        for (var i = 0; i < files.Length; i++)
        {
            var page = PixDecode.Skia(files[i])!;
            using var src = SKBitmap.Decode(Fixtures.PdfSourceJpeg(2800, 2100, quality: 0.95, seed: 100 + i));
            var k = DipPerPoint * 2;
            // PdfImageHeavy draws the image at (20, 20) with width A4 - 40 pt and height 0.75 * width.
            var x = (int)Math.Round(20 * k);
            var w = (int)Math.Round((595.276 - 40) * k);
            var h = (int)Math.Round(w * 0.75);
            using var scaled = src.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell))!;
            var bytes = new byte[w * h * 4];
            System.Runtime.InteropServices.Marshal.Copy(scaled.GetPixels(), bytes, 0, bytes.Length);
            var want = new Px(w, h, bytes);
            // Inset by 6 px so edge anti-aliasing does not count.
            var got = page.Crop(x + 6, x + 6, w - 12, h - 12);
            var exp = want.Crop(6, 6, w - 12, h - 12);
            var p = PixMetrics.Psnr(exp, got);
            Assert.True(p >= 24, $"page {i + 1}: the picture on the page differs from the embedded photo ({p:0.0} dB)");
        }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ TXT

    [Fact]
    public async Task Text_extraction_equals_the_text_that_was_drawn()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("labelled.pdf", LabelledPdf("Alpha", 3));

        var output = Kit.One(await Kit.RunAsync("TXT", pdf));

        Assert.Equal(tmp.Combine("labelled.txt"), output);
        var text = Kit.NormalizeText(File.ReadAllText(output));
        // Ground truth 1: what the generator drew.
        Assert.Equal(string.Join(' ', Enumerable.Range(1, 3).Select(p => PageText("Alpha", p, 3))), text);
        // Ground truth 2: PdfPig's own page-by-page extraction, which does not use the converter's line grouping.
        Assert.Equal(Kit.NormalizeText(string.Join(' ', PdfCheck.Texts(pdf))), text);
        Kit.Record("pdf", "labelled.pdf (3 pages)", "TXT", "PdfPig page text + the drawn text", "identical after whitespace normalisation");
    }

    [Fact]
    public async Task Text_extraction_keeps_non_ascii_characters_exactly()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("unicode.pdf", UnicodePdf());
        Assert.Contains(UnicodeLine, Kit.NormalizeText(string.Join(' ', PdfCheck.Texts(pdf)))); // the fixture itself is right

        var output = Kit.One(await Kit.RunAsync("TXT", pdf));

        var bytes = File.ReadAllBytes(output);
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes); // strict: throws on invalid UTF-8
        Assert.Contains(UnicodeLine, text);
        Assert.Contains("Price: 5€ — “quoted”", text);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "unexpected BOM");
        Kit.Record("pdf", "unicode.pdf", "TXT", "strict UTF-8 decode + PdfPig", "accents, Greek, euro sign, curly quotes preserved");
    }

    // ------------------------------------------------------------------ Split

    [Fact]
    public async Task Split_gives_one_openable_pdf_per_page_in_order()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("book.pdf", LabelledPdf("Book", 4));

        var folder = Kit.One(await Kit.RunAsync("Split", pdf));

        Assert.Equal(tmp.Combine("book split"), folder);
        var files = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(Enumerable.Range(1, 4).Select(i => Path.Combine(folder, $"book {i:00}.pdf")).ToArray(), files);
        var total = 0;
        for (var i = 0; i < files.Length; i++)
        {
            var pages = PdfCheck.Read(files[i]);
            Assert.Single(pages);
            total += pages.Count;
            Assert.Equal(PageText("Book", i + 1, 4), Kit.NormalizeText(pages[0].Text));
            var rendered = await PdfCheck.RenderAllAsync(files[i]);
            Assert.Single(rendered);
            Assert.True(PdfCheck.InkFraction(rendered[0]) > 0.002);
        }
        Assert.Equal(PdfCheck.Read(pdf).Count, total);
        Kit.Record("pdf", "book.pdf (4 pages)", "Split", "PdfPig + Windows.Data.Pdf", "4 files x 1 page, text of page i in file i, all render, total = source");
    }

    [Fact]
    public async Task Split_of_an_image_pdf_keeps_every_picture()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scans.pdf", Fixtures.PdfImageHeavy(3));
        var folder = Kit.One(await Kit.RunAsync("Split", pdf));
        var files = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(3, files.Length);
        var original = await PdfCheck.RenderAllAsync(pdf, 500);
        for (var i = 0; i < 3; i++)
        {
            var pages = PdfCheck.Read(files[i]);
            Assert.Single(pages);
            Assert.Equal(1, pages[0].Images);
            var r = (await PdfCheck.RenderAllAsync(files[i], 500))[0];
            Assert.True(PixMetrics.Psnr(original[i], r) >= 40, "a split page does not look like the original page");
        }
        Kit.Record("pdf", "scans.pdf (3 image pages)", "Split", "PdfPig + Windows.Data.Pdf render comparison", "3 files, each page renders identically to the original page");
    }

    // ------------------------------------------------------------------ Merge

    [Fact]
    public async Task Merge_sums_the_pages_and_keeps_their_order()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", LabelledPdf("Alpha", 3));
        var b = tmp.WriteFile("b.pdf", LabelledPdf("Beta", 2));
        var c = tmp.WriteFile("c.pdf", Fixtures.PdfLandscape());

        var merged = Kit.One(await Kit.RunAsync("Merge", a, b, c));

        Assert.Equal(tmp.Combine("Merged.pdf"), merged);
        var pages = PdfCheck.Read(merged);
        Assert.Equal(3 + 2 + 1, pages.Count);
        var expected = new[]
        {
            PageText("Alpha", 1, 3), PageText("Alpha", 2, 3), PageText("Alpha", 3, 3),
            PageText("Beta", 1, 2), PageText("Beta", 2, 2),
            "A landscape page.",
        };
        Assert.Equal(expected, pages.Select(p => Kit.NormalizeText(p.Text)).ToArray());
        Assert.True(pages[5].Width > pages[5].Height, "landscape page lost its orientation");
        var rendered = await PdfCheck.RenderAllAsync(merged);
        Assert.Equal(6, rendered.Count);
        Assert.All(rendered, p => Assert.True(PdfCheck.InkFraction(p) > 0.001));
        Kit.Record("pdf", "a.pdf(3) + b.pdf(2) + c.pdf(1, landscape)", "Merge", "PdfPig text per page + Windows.Data.Pdf", "6 pages = 3+2+1, text order preserved, landscape kept, all render");
    }

    [Fact]
    public async Task Merge_of_text_and_image_pdfs_renders_like_the_originals()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", Fixtures.PdfText(2));
        var b = tmp.WriteFile("b.pdf", Fixtures.PdfImageHeavy(2));
        var merged = Kit.One(await Kit.RunAsync("Merge", a, b));
        var rm = await PdfCheck.RenderAllAsync(merged, 500);
        var ra = await PdfCheck.RenderAllAsync(a, 500);
        var rb = await PdfCheck.RenderAllAsync(b, 500);
        Assert.Equal(4, rm.Count);
        var originals = ra.Concat(rb).ToList();
        for (var i = 0; i < 4; i++)
            Assert.True(PixMetrics.Psnr(originals[i], rm[i]) >= 40, $"merged page {i + 1} does not look like its source page");
        Kit.Record("pdf", "text(2) + image(2)", "Merge", "Windows.Data.Pdf render comparison", "4 pages, each renders like its source page (>= 40 dB)");
    }

    // ------------------------------------------------------------------ Compress

    [Fact]
    public async Task Compress_of_a_heavy_pdf_is_smaller_and_every_page_still_renders()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("heavy.pdf", Fixtures.PdfImageHeavy(3));

        var output = Kit.One(await Kit.RunAsync("Compress", pdf));

        Assert.Equal(tmp.Combine("heavy compressed.pdf"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(pdf).Length, "not smaller");
        var pages = PdfCheck.Read(output);
        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.Equal(1, p.Images));
        var before = await PdfCheck.RenderAllAsync(pdf, 500);
        var after = await PdfCheck.RenderAllAsync(output, 500);
        Assert.Equal(3, after.Count);
        for (var i = 0; i < 3; i++)
        {
            var p = PixMetrics.Psnr(before[i], after[i]);
            Assert.True(p >= 25, $"page {i + 1} lost quality: {p:0.0} dB");
        }
        Kit.Record("pdf", "heavy.pdf (3 photo pages)", "Compress", "PdfPig + Windows.Data.Pdf render comparison",
            $"{new FileInfo(pdf).Length / 1024} KB -> {new FileInfo(output).Length / 1024} KB, 3 pages render, >= 25 dB per page");
    }

    [Fact]
    public async Task Compress_of_a_text_pdf_keeps_the_text()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("words.pdf", LabelledPdf("Gamma", 3));
        var result = await Kit.TryRunAsync("Compress", null, pdf);
        if (result.Failures.Count == 1)
        {
            // Allowed: a small text PDF may already be as small as it gets; nothing must be left behind.
            Assert.Contains("already small", result.Failures[0].Message);
            Assert.Single(Directory.GetFiles(tmp.Path));
            Kit.Record("pdf", "words.pdf (3 text pages)", "Compress", "refusal check", "refused: already small (nothing written)");
            return;
        }
        var output = Kit.One(result.Outputs.Select(o => o.Path).ToList());
        Assert.True(new FileInfo(output).Length < new FileInfo(pdf).Length);
        Assert.Equal(PdfCheck.Texts(pdf).Select(Kit.NormalizeText), PdfCheck.Texts(output).Select(Kit.NormalizeText));
        Assert.Equal(3, (await PdfCheck.RenderAllAsync(output)).Count);
        Kit.Record("pdf", "words.pdf (3 text pages)", "Compress", "PdfPig text + Windows.Data.Pdf", "smaller, text identical, 3 pages render");
    }
}
