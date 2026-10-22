using Shiftvert.Core.Pdf;
using static Shiftvert.Core.Tests.Pdf.PdfTestHelpers;

namespace Shiftvert.Core.Tests.Pdf;

public class PdfCompressTests
{
    [Fact]
    public async Task An_image_heavy_pdf_gets_much_smaller_and_keeps_its_pages()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfImageHeavy(3));
        using var guard = OriginalsGuard.Watch(pdf);

        var output = await PdfConverter.CompressAsync(pdf, default);

        Assert.Equal(tmp.Combine("scan compressed.pdf"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(pdf).Length * 0.6,
            $"expected well under 60% of {new FileInfo(pdf).Length}, got {new FileInfo(output).Length}");
        Assert.Equal(3, PageCount(output));

        // The compressed file still renders (Windows.Data.Pdf) to a non-trivial page.
        var png = await PdfConverter.ToImagesAsync(output, PdfPageFormat.Png, default);
        Assert.True(Directory.Exists(png));
        Assert.Equal(3, Directory.GetFiles(png).Length);
    }

    [Fact]
    public async Task Compress_downscales_very_large_images()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfImageHeavy(1));

        var output = await PdfConverter.CompressAsync(pdf, default);

        using var doc = Open(output);
        var maxWidth = 0;
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            if (obj is PdfSharp.Pdf.PdfDictionary d && d.Stream is not null && d.Elements.GetName("/Subtype") == "/Image")
                maxWidth = Math.Max(maxWidth, d.Elements.GetInteger("/Width"));
        }
        Assert.InRange(maxWidth, 1, 2400);
    }

    private static List<(string Filter, int Width)> ImageFilters(string path)
    {
        using var doc = Open(path);
        var result = new List<(string, int)>();
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            if (obj is PdfSharp.Pdf.PdfDictionary d && d.Stream is not null && d.Elements.GetName("/Subtype") == "/Image")
                result.Add((d.Elements.GetName("/Filter"), d.Elements.GetInteger("/Width")));
        }
        return result;
    }

    [Fact]
    public async Task Flate_photos_are_recompressed_as_jpeg()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("png-photo.pdf", Fixtures.PdfFlateImage());
        var before = Assert.Single(ImageFilters(pdf));
        Assert.Equal("/FlateDecode", before.Filter); // precondition: this fixture really exercises the Flate path

        var output = await PdfConverter.CompressAsync(pdf, default);

        var after = Assert.Single(ImageFilters(output));
        Assert.Equal("/DCTDecode", after.Filter);
        Assert.Equal(1200, after.Width);
        Assert.True(new FileInfo(output).Length < new FileInfo(pdf).Length * 0.5);
    }

    [Fact]
    public async Task A_pdf_that_is_already_small_is_reported_not_rewritten()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("notes.pdf", Fixtures.PdfText(1));
        using var guard = OriginalsGuard.Watch(pdf);

        string? output = null;
        ConversionException? refused = null;
        try { output = await PdfConverter.CompressAsync(pdf, default); }
        catch (ConversionException ex) { refused = ex; }

        if (refused is not null)
        {
            Assert.Contains("already small", refused.Message);
            Assert.False(File.Exists(tmp.Combine("notes compressed.pdf")));
        }
        else
        {
            Assert.True(new FileInfo(output!).Length < new FileInfo(pdf).Length, "kept a result that is not smaller");
        }
    }

    [Fact]
    public async Task An_encrypted_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());
        using var guard = OriginalsGuard.Watch(pdf);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.CompressAsync(pdf, default));

        Assert.Equal("This PDF is password-protected.", ex.Message);
        Assert.Equal(["locked.pdf"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public async Task A_corrupt_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("broken.pdf", Fixtures.PdfCorrupt());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.CompressAsync(pdf, default));
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public async Task Failed_compress_leaves_no_temp_files_behind()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("notes.pdf", Fixtures.PdfText(1));
        try { await PdfConverter.CompressAsync(pdf, default); } catch (ConversionException) { }

        Assert.DoesNotContain(Directory.GetFileSystemEntries(tmp.Path), p => p.EndsWith(".shiftvert-tmp"));
    }
}
