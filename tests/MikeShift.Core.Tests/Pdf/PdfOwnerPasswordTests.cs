using MikeShift.Core.Pdf;

namespace MikeShift.Core.Tests.Pdf;

/// <summary>A PDF with only an owner password opens without a password but carries print/copy restrictions: every operation refuses it like any encrypted PDF.</summary>
public class PdfOwnerPasswordTests
{
    private const string Expected = "This PDF is password-protected.";

    private static string Locked(TempDir tmp) => tmp.WriteFile("restricted.pdf", Fixtures.PdfOwnerPasswordOnly());

    private static string[] Files(TempDir tmp) =>
        Directory.GetFileSystemEntries(tmp.Path).Select(p => Path.GetFileName(p)!).Where(n => n != "__downloads").Order().ToArray();

    [Fact]
    public async Task Split_refuses_an_owner_password_only_pdf()
    {
        using var tmp = new TempDir();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.SplitAsync(Locked(tmp), default));
        Assert.Equal(Expected, ex.Message);
        Assert.Equal(["restricted.pdf"], Files(tmp));
    }

    [Fact]
    public async Task Merge_refuses_an_owner_password_only_pdf_and_names_it()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("good.pdf", Fixtures.PdfText(1));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeAsync([good, Locked(tmp)], default));
        Assert.Contains("restricted.pdf", ex.Message);
        Assert.Contains("password", ex.Message);
        Assert.False(File.Exists(tmp.Combine("Merged.pdf")));
    }

    [Fact]
    public async Task Compress_refuses_an_owner_password_only_pdf()
    {
        using var tmp = new TempDir();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.CompressAsync(Locked(tmp), default));
        Assert.Equal(Expected, ex.Message);
    }

    [Fact]
    public async Task Text_extraction_refuses_an_owner_password_only_pdf()
    {
        using var tmp = new TempDir();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(Locked(tmp), default));
        Assert.Equal(Expected, ex.Message);
        Assert.Equal(["restricted.pdf"], Files(tmp));
    }

    [Fact]
    public async Task Page_rendering_refuses_an_owner_password_only_pdf()
    {
        using var tmp = new TempDir();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToImagesAsync(Locked(tmp), PdfPageFormat.Png, default));
        Assert.Equal(Expected, ex.Message);
        Assert.Equal(["restricted.pdf"], Files(tmp));
    }

    [Fact]
    public void An_unrestricted_pdf_is_still_accepted()
    {
        Assert.False(PdfGuard.IsEncryptedFile(Fixtures.PdfText(1)));
        Assert.True(PdfGuard.IsEncryptedFile(Fixtures.PdfOwnerPasswordOnly()));
    }
}
