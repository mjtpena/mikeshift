namespace MikeShift.Cli.Tests;

/// <summary>PDF actions end to end through the real executable.</summary>
public class PdfCliTests
{
    [Fact]
    public void List_on_a_pdf_offers_the_pdf_actions_and_zip_last()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(2));

        var r = CliProcess.Run("--list", pdf);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("PNG, JPG, TXT, Compress, Split, Zip", r.Stdout.Trim());
    }

    [Fact]
    public void List_on_two_pdfs_adds_merge()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", Fixtures.PdfText(1));
        var b = tmp.WriteFile("b.pdf", Fixtures.PdfText(1));

        var r = CliProcess.Run("--list", a, b);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("PNG, JPG, TXT, Compress, Split, Merge, Zip", r.Stdout.Trim());
    }

    [Fact]
    public void Run_Split_prints_the_folder_and_creates_one_pdf_per_page()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("report.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(pdf);

        var r = CliProcess.Run("--run", "Split", pdf);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var folder = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("report split"), folder);
        Assert.Equal(3, Directory.GetFiles(folder, "*.pdf").Length);
    }

    [Fact]
    public void Run_Merge_makes_Merged_pdf()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", Fixtures.PdfText(1));
        var b = tmp.WriteFile("b.pdf", Fixtures.PdfText(2));

        var r = CliProcess.Run("--run", "Merge", a, b);

        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("Merged.pdf"), output);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Run_PNG_renders_pages()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("one.pdf", Fixtures.PdfText(1));

        var r = CliProcess.Run("--run", "PNG", pdf);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(tmp.Combine("one.png"), Assert.Single(r.StdoutLines));
    }

    [Fact]
    public void Run_TXT_on_a_scan_exits_1_with_the_scan_message()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScan());

        var r = CliProcess.Run("--run", "TXT", pdf);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Stdout);
        Assert.Contains("probably a scan", r.Stderr);
    }

    [Fact]
    public void Run_Compress_on_an_encrypted_pdf_exits_1()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());

        var r = CliProcess.Run("--run", "Compress", pdf);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("password-protected", r.Stderr);
    }

    [Fact]
    public void Run_Compress_on_an_image_heavy_pdf_makes_a_compressed_copy()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("photos.pdf", Fixtures.PdfImageHeavy(2));

        var r = CliProcess.Run("--run", "Compress", pdf);

        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("photos compressed.pdf"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(pdf).Length);
    }
}
