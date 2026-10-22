namespace Shiftvert.Cli.Tests;

/// <summary>Exit codes and syntax handling, exactly as PLAN §3.6, through the real executable.</summary>
public class CliUsageTests
{
    [Fact]
    public void The_executable_is_where_the_tests_expect_it()
    {
        Assert.True(File.Exists(CliProcess.ExePath), CliProcess.ExePath);
    }

    [Theory]
    [InlineData()]
    [InlineData("--bogus")]
    [InlineData("--list")]
    [InlineData("--run")]
    [InlineData("--run", "Zip")]
    [InlineData("--run", "Zip", "--nonsense", "a.txt")]
    public void Usage_errors_exit_2_and_print_usage_to_stderr(params string[] args)
    {
        var r = CliProcess.Run(args);
        Assert.Equal(2, r.ExitCode);
        Assert.Equal("", r.Stdout);
        Assert.Contains("usage:", r.Stderr);
    }

    [Fact]
    public void List_prints_comma_separated_titles_and_exits_0()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());

        var r = CliProcess.Run("--list", png);

        Assert.Equal(0, r.ExitCode);
        var titles = r.Stdout.Trim().Split(", ");
        Assert.Contains("JPG", titles);
        Assert.DoesNotContain("PNG", titles); // already a PNG
        Assert.Equal("Zip", titles[^1]);
    }

    [Fact]
    public void List_for_a_folder_or_mixed_selection_is_only_zip()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("stuff");
        Assert.Equal("Zip", CliProcess.Run("--list", folder).Stdout.Trim());
        Assert.Equal("Zip", CliProcess.Run("--list", tmp.WriteFile("a.png", Fixtures.Png()), tmp.WriteFile("b.pdf", Fixtures.PdfStub())).Stdout.Trim());
    }

    [Fact]
    public void Unknown_title_lists_the_actions_and_exits_1()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Frobnicate", tmp.WriteFile("a.txt", "x"));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("unknown action", r.Stderr);
        Assert.Contains("Zip", r.Stderr);
    }

    [Fact]
    public void Missing_input_exits_1()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Zip", tmp.Combine("nope.txt"));
        Assert.Equal(1, r.ExitCode);
        Assert.StartsWith("error:", r.Stderr);
    }

    [Fact]
    public void Trim_without_range_exits_1_with_syntax_help()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Trim", tmp.WriteFile("clip.wav", Fixtures.SineWav(0.1)));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("--range m:ss-m:ss", r.Stderr);
    }

    [Fact]
    public void Crop_without_crop_exits_1_with_syntax_help()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Crop", tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("--crop X,Y,W,H", r.Stderr);
    }

    [Fact]
    public void Unicode_paths_round_trip_through_stdout()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("répört – 報告.txt", "x");
        var r = CliProcess.Run("--run", "Zip", file);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(tmp.Combine("répört – 報告.zip"), Assert.Single(r.StdoutLines));
    }
}
