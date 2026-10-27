namespace MikeShift.Cli.Tests;

/// <summary>QR code and Read QR code end to end through the real executable.</summary>
public class QrCodeCliTests
{
    private const string Link = "https://example.com/menu?table=12";

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray();

    [Fact]
    public void List_offers_QR_code_for_a_shortcut_and_Read_QR_code_for_a_picture()
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", $"[InternetShortcut]\r\nURL={Link}\r\n");
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        Assert.Equal("QR code, Zip", CliProcess.Run("--list", url).Stdout.Trim());
        Assert.Contains("Read QR code", CliProcess.Run("--list", png).Stdout.Trim().Split(", "));
    }

    [Fact]
    public void A_shortcut_becomes_a_png_that_Read_QR_code_turns_back_into_the_address()
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", $"[InternetShortcut]\r\nURL={Link}\r\nIconIndex=0\r\n");
        using var guard = OriginalsGuard.Watch(url);

        var made = CliProcess.Run("--run", "QR code", url);

        Assert.Equal(0, made.ExitCode);
        Assert.Equal(tmp.Combine("site qr.png"), Assert.Single(made.StdoutLines));

        var read = CliProcess.Run("--run", "Read QR code", tmp.Combine("site qr.png"));

        Assert.Equal(0, read.ExitCode);
        Assert.Equal(tmp.Combine("site qr qr.txt"), Assert.Single(read.StdoutLines));
        Assert.Equal(Link, File.ReadAllText(tmp.Combine("site qr qr.txt")));
        guard.AssertUnchanged();
    }

    [Fact]
    public void A_text_file_becomes_a_code_too()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("note.txt", "Meet at the north gate at 9.");

        var r = CliProcess.Run("--run", "QR code", txt);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(tmp.Combine("note qr.png"), Assert.Single(r.StdoutLines));
    }

    [Fact]
    public void Text_over_the_capacity_exits_1_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("long.txt", new string('x', 3000));

        var r = CliProcess.Run("--run", "QR code", txt);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("too much for a QR code", r.Stderr);
        Assert.Equal(["long.txt"], FileNames(tmp));
    }

    [Fact]
    public void A_picture_without_a_code_exits_1_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("colours.png", Fixtures.PngRgb(400, 300));

        var r = CliProcess.Run("--run", "Read QR code", png);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("Couldn't find a QR code in this picture.", r.Stderr);
        Assert.Equal(["colours.png"], FileNames(tmp));
    }
}
