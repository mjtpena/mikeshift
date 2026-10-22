using Shiftvert.Core.Pdf;
using static Shiftvert.Core.Tests.Pdf.PdfTestHelpers;

namespace Shiftvert.Core.Tests.Pdf;

public class ImagesToPdfTests
{
    private static void AssertSize((double Width, double Height) actual, (double Width, double Height) expected)
    {
        Assert.InRange(actual.Width, expected.Width - 1, expected.Width + 1);
        Assert.InRange(actual.Height, expected.Height - 1, expected.Height + 1);
    }

    [Fact]
    public async Task A_portrait_image_becomes_one_portrait_page_of_the_regional_paper_size()
    {
        using var tmp = new TempDir();
        var img = tmp.WriteFile("portrait.jpg", Fixtures.PdfSourceJpeg(900, 1400));
        using var guard = OriginalsGuard.Watch(img);

        var output = await PdfConverter.ImageToPdfAsync(img, default);

        Assert.Equal(tmp.Combine("portrait.pdf"), output);
        Assert.Equal(1, PageCount(output));
        AssertSize(PageSize(output), ExpectedPortraitPage());
    }

    [Fact]
    public async Task A_landscape_image_rotates_the_page()
    {
        using var tmp = new TempDir();
        var img = tmp.WriteFile("wide.png", Fixtures.PdfSourcePng(1600, 900));

        var output = await PdfConverter.ImageToPdfAsync(img, default);

        var (pw, ph) = ExpectedPortraitPage();
        AssertSize(PageSize(output), (ph, pw));
    }

    [Fact]
    public async Task The_page_actually_shows_the_image()
    {
        using var tmp = new TempDir();
        var img = tmp.WriteFile("photo.jpg", Fixtures.PdfSourceJpeg(1200, 800));
        var pdf = await PdfConverter.ImageToPdfAsync(img, default);

        var png = await PdfConverter.ToImagesAsync(pdf, PdfPageFormat.Jpg, default);

        // A full-colour photo on a page makes a large JPEG; a blank page would be a few KB.
        Assert.True(new FileInfo(png).Length > 60_000);
    }

    [Fact]
    public async Task Transparent_pngs_are_flattened_on_white()
    {
        using var tmp = new TempDir();
        var img = tmp.WriteFile("alpha.png", Fixtures.PdfSourcePng(400, 400, alpha: true));
        var output = await PdfConverter.ImageToPdfAsync(img, default);
        Assert.Equal(1, PageCount(output));
    }

    [Fact]
    public async Task Every_tiff_frame_is_a_page()
    {
        using var tmp = new TempDir();
        var tif = tmp.WriteFile("scan.tif", Fixtures.PdfSourceTiff(frames: 3, 500, 400));

        var output = await PdfConverter.ImageToPdfAsync(tif, default);

        Assert.Equal(3, PageCount(output));
    }

    [Fact]
    public async Task Huge_images_are_embedded_at_no_more_than_3508_pixels()
    {
        using var tmp = new TempDir();
        var img = tmp.WriteFile("huge.jpg", Fixtures.PdfSourceJpeg(4200, 1400, quality: 0.5));

        var output = await PdfConverter.ImageToPdfAsync(img, default);

        using var doc = Open(output);
        var maxWidth = 0;
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            if (obj is PdfSharp.Pdf.PdfDictionary d && d.Stream is not null && d.Elements.GetName("/Subtype") == "/Image")
                maxWidth = Math.Max(maxWidth, d.Elements.GetInteger("/Width"));
        }
        Assert.Equal(3508, maxWidth);
    }

    [Fact]
    public async Task Merge_images_makes_Merged_pdf_beside_the_first_with_a_page_per_image()
    {
        using var tmp = new TempDir();
        tmp.CreateDir("x");
        var a = tmp.WriteFile("x/a.jpg", Fixtures.PdfSourceJpeg(800, 600));
        var b = tmp.WriteFile("y/b.png", Fixtures.PdfSourcePng(600, 900));
        var c = tmp.WriteFile("y/c.tif", Fixtures.PdfSourceTiff(2, 300, 300));
        using var guard = OriginalsGuard.Watch(a, b, c);

        var output = await PdfConverter.MergeImagesAsync([a, b, c], default);

        Assert.Equal(tmp.Combine("x", "Merged.pdf"), output);
        Assert.Equal(4, PageCount(output));
        var (pw, ph) = ExpectedPortraitPage();
        AssertSize(PageSize(output, 0), (ph, pw)); // landscape jpg
        AssertSize(PageSize(output, 1), (pw, ph)); // portrait png
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        using var tmp = new TempDir();
        var bogus = tmp.WriteFile("bogus.jpg", Fixtures.Noise(4096));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ImageToPdfAsync(bogus, default));
        Assert.Contains("bogus.jpg", ex.Message);
        Assert.False(File.Exists(tmp.Combine("bogus.pdf")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Orientation_is_applied_to_pixels(int orientation)
    {
        // 3 x 2 image, pixels numbered 0..5 row by row (one gray channel).
        var src = new PdfPixels.Raster([0, 1, 2, 3, 4, 5], 3, 2, 1);

        var r = PdfPixels.ApplyOrientation(src, orientation);

        var expected = orientation switch
        {
            1 => (3, 2, new byte[] { 0, 1, 2, 3, 4, 5 }),
            2 => (3, 2, new byte[] { 2, 1, 0, 5, 4, 3 }),         // mirrored horizontally
            3 => (3, 2, new byte[] { 5, 4, 3, 2, 1, 0 }),         // rotated 180
            4 => (3, 2, new byte[] { 3, 4, 5, 0, 1, 2 }),         // mirrored vertically
            5 => (2, 3, new byte[] { 0, 3, 1, 4, 2, 5 }),         // transposed
            6 => (2, 3, new byte[] { 3, 0, 4, 1, 5, 2 }),         // rotated 90 clockwise
            7 => (2, 3, new byte[] { 5, 2, 4, 1, 3, 0 }),         // transversed
            _ => (2, 3, new byte[] { 2, 5, 1, 4, 0, 3 }),         // rotated 90 counter-clockwise
        };
        Assert.Equal(expected.Item1, r.Width);
        Assert.Equal(expected.Item2, r.Height);
        Assert.Equal(expected.Item3, r.Data);
    }

    [Fact]
    public void Downscale_averages_pixels()
    {
        var src = new PdfPixels.Raster([0, 100, 200, 100, 50, 50, 50, 50], 4, 2, 1);
        var r = PdfPixels.Downscale(src, 2, 1);
        Assert.Equal(2, r.Width);
        Assert.Equal(1, r.Height);
        Assert.Equal([(byte)((0 + 100 + 50 + 50 + 2) / 4), (byte)((200 + 100 + 50 + 50 + 2) / 4)], r.Data);
    }
}
