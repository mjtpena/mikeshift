using MikeShift.Core.Pdf;
using static MikeShift.Core.Tests.Pdf.PdfTestHelpers;

namespace MikeShift.Core.Tests.Pdf;

public class PdfToImagesTests
{
    [Fact]
    public async Task One_page_gives_a_png_beside_the_pdf()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("single.pdf", Fixtures.PdfText(1));
        using var guard = OriginalsGuard.Watch(pdf);

        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default);

        Assert.Equal(tmp.Combine("single.png"), output);
        var (w, h) = PngSize(output);
        // A4 is 794 x 1123 DIPs; rendered at 2x.
        Assert.InRange(w, 1500, 1700);
        Assert.InRange(h, 2150, 2350);
    }

    [Fact]
    public async Task Several_pages_go_into_a_pages_folder_with_zero_padded_names()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(pdf);

        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default);

        Assert.Equal(tmp.Combine("doc pages"), output);
        var names = Directory.GetFiles(output).Select(p => Path.GetFileName(p)!).Order().ToArray();
        Assert.Equal(["doc 01.png", "doc 02.png", "doc 03.png"], names);
        foreach (var f in Directory.GetFiles(output)) PngSize(f);
    }

    [Fact]
    public async Task Pages_are_not_blank_white_images()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(1));
        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Jpg, default);

        Assert.Equal(tmp.Combine("doc.jpg"), output);
        Assert.True(IsJpeg(output));
        // Text makes the JPEG noticeably bigger than a pure white page would be.
        Assert.True(new FileInfo(output).Length > 20_000, "page looks blank");
    }

    [Fact]
    public async Task Jpg_pages_are_jpegs()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(2));
        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Jpg, default);

        var files = Directory.GetFiles(output);
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.True(IsJpeg(f), f));
        Assert.All(files, f => Assert.EndsWith(".jpg", f));
    }

    [Fact]
    public async Task Huge_pages_are_capped_at_8000_pixels_on_the_long_side()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("big.pdf", Fixtures.PdfHugePage());

        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default);

        var (w, h) = PngSize(output);
        Assert.Equal(8000, Math.Max(w, h));
        Assert.InRange(Math.Min(w, h), 3990, 4010); // aspect ratio 2:1 kept
    }

    [Fact]
    public async Task Existing_output_is_never_overwritten()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(1));
        tmp.WriteFile("doc.png", "precious");

        var output = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default);

        Assert.Equal(tmp.Combine("doc 2.png"), output);
        Assert.Equal("precious", File.ReadAllText(tmp.Combine("doc.png")));
    }

    [Fact]
    public async Task Encrypted_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default));
        Assert.Contains("password", ex.Message);
        Assert.Equal(["locked.pdf"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public async Task Corrupt_pdf_is_refused_with_a_clear_message()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("broken.pdf", Fixtures.PdfCorrupt());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Png, default));
        Assert.Contains("damaged", ex.Message);
    }

    [Theory]
    [InlineData(300, 300, 600, 600)]
    [InlineData(794, 1123, 1588, 2246)]
    [InlineData(10000, 5000, 8000, 4000)]
    [InlineData(2000, 9000, 1778, 8000)]
    public void Target_size_is_2x_capped_at_8000(double w, double h, int expectedW, int expectedH)
    {
        var (tw, th) = PdfRender.TargetSize(w, h);
        Assert.Equal(expectedW, tw);
        Assert.Equal(expectedH, th);
    }

    [Theory]
    [InlineData(1, 1, "doc 01.png")]
    [InlineData(9, 12, "doc 09.png")]
    [InlineData(10, 120, "doc 010.png")]
    [InlineData(7, 100, "doc 007.png")]
    public void Numbered_names_pad_to_the_page_count_width(int number, int total, string expected)
    {
        Assert.Equal(expected, PdfConverter.NumberedName("doc", number, total, ".png"));
    }
}
