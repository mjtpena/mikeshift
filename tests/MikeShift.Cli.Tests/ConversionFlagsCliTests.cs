namespace MikeShift.Cli.Tests;

/// <summary>The conversion-setting flags through the real executable: exit codes, output location and effect.</summary>
public class ConversionFlagsCliTests
{
    [Theory]
    [InlineData("--quality", "30")]
    [InlineData("--quality", "abc")]
    [InlineData("--compress", "max")]
    [InlineData("--resize", "60")]
    [InlineData("--gif-seconds", "12")]
    public void A_bad_value_exits_2_prints_the_reason_and_usage_and_converts_nothing(string flag, string value)
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());

        var r = CliProcess.Run("--run", "JPG", flag, value, png);

        Assert.Equal(2, r.ExitCode);
        Assert.Equal("", r.Stdout);
        Assert.Contains($"error: {flag} must be", r.Stderr);
        Assert.Contains("usage:", r.Stderr);
        Assert.Equal(["a.png"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public void An_out_folder_that_does_not_exist_exits_2()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "JPG", "--out", tmp.Combine("nowhere"), tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("--out:", r.Stderr);
        Assert.False(Directory.Exists(tmp.Combine("nowhere")));
    }

    [Fact]
    public void Out_and_resize_work_together_through_the_exe()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("converted");
        var jpg = tmp.WriteFile("photo.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 1, gps: false));

        var r = CliProcess.Run("--run", "Resize", "--resize", "25", "--out", dest, jpg);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(Path.Combine(dest, "photo 25%.jpg"), Assert.Single(r.StdoutLines));
        Assert.True(File.Exists(Path.Combine(dest, "photo 25%.jpg")));
        Assert.Equal(["photo.jpg"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public void List_shows_the_resize_percentage_that_was_asked_for()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());

        Assert.Contains("Resize 75%", CliProcess.Run("--list", "--resize", "75", png).Stdout);
        Assert.Contains("Resize 50%", CliProcess.Run("--list", png).Stdout);
    }

    [Fact]
    public void The_usage_text_lists_the_flags()
    {
        var r = CliProcess.Run("--bogus");
        Assert.Equal(2, r.ExitCode);
        foreach (var flag in new[] { "--quality", "--compress", "--resize", "--gif-seconds", "--out" })
            Assert.Contains(flag, r.Stderr);
    }
}
