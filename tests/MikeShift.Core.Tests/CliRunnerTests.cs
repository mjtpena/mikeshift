using MikeShift.Core;
using MikeShift.Core.Cli;

namespace MikeShift.Core.Tests;

/// <summary>In-process tests of the CLI parser/dispatcher (the child-process tests live in MikeShift.Cli.Tests).</summary>
public class CliRunnerTests
{
    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = await CliRunner.RunAsync(args, o, e, Capabilities.All);
        return (exit, o.ToString(), e.ToString());
    }

    [Theory]
    [InlineData()]
    [InlineData("--bogus")]
    [InlineData("--help")]
    [InlineData("--list")]
    [InlineData("--run")]
    [InlineData("--run", "Zip")]
    [InlineData("--list", "--run", "Zip", "a.txt")]
    [InlineData("--range", "0:01-0:02", "a.txt")]
    [InlineData("--run", "Zip", "--range")]
    [InlineData("--run", "Zip", "--wat", "a.txt")]
    public async Task Bad_command_lines_print_usage_and_exit_2(params string[] args)
    {
        var (exit, stdout, stderr) = await Run(args);
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.Contains("usage:", stderr);
    }

    [Fact]
    public async Task List_prints_comma_separated_titles_for_the_selection()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());
        var (exit, stdout, _) = await Run("--list", png);
        Assert.Equal(0, exit);
        var titles = stdout.Trim().Split(", ");
        Assert.DoesNotContain("PNG", titles);
        Assert.Contains("JPG", titles);
        Assert.Equal("Zip", titles[^1]);
    }

    [Fact]
    public async Task List_for_mixed_files_is_just_zip()
    {
        using var tmp = new TempDir();
        var (exit, stdout, _) = await Run("--list", tmp.WriteFile("a.png", Fixtures.Png()), tmp.WriteFile("b.txt", "x"));
        Assert.Equal(0, exit);
        Assert.Equal("Zip", stdout.Trim());
    }

    [Fact]
    public async Task Missing_file_is_an_error_exit_1()
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--list", tmp.Combine("nope.png"));
        Assert.Equal(1, exit);
        Assert.StartsWith("error: not found", stderr);
    }

    [Fact]
    public async Task Run_zip_prints_the_output_path_and_exits_0()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.txt", "x");
        var (exit, stdout, stderr) = await Run("--run", "zip", file);
        Assert.Equal(0, exit);
        Assert.Equal("", stderr);
        Assert.Equal(tmp.Combine("a.zip"), stdout.Trim());
        Assert.True(File.Exists(tmp.Combine("a.zip")));
    }

    [Fact]
    public async Task Double_dash_allows_file_names_that_look_like_flags()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("--odd.txt", "x");
        var (exit, stdout, _) = await Run("--run", "Zip", "--", file);
        Assert.Equal(0, exit);
        Assert.Equal(tmp.Combine("--odd.zip"), stdout.Trim());
    }

    [Fact]
    public async Task Unknown_title_lists_the_available_actions_and_exits_1()
    {
        using var tmp = new TempDir();
        var (exit, stdout, stderr) = await Run("--run", "Frobnicate", tmp.WriteFile("a.txt", "x"));
        Assert.Equal(1, exit);
        Assert.Equal("", stdout);
        Assert.Contains("unknown action \"Frobnicate\"", stderr);
        Assert.Contains("Available: PDF, DOCX, RTF, HTML, ODT, Zip", stderr); // TXT hidden: the file is already .txt
    }

    [Fact]
    public async Task Trim_without_range_exits_1_with_syntax_help()
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Trim", tmp.WriteFile("clip.wav", Fixtures.SineWav(0.1)));
        Assert.Equal(1, exit);
        Assert.Contains("--range m:ss-m:ss", stderr);
    }

    [Fact]
    public async Task Crop_without_crop_exits_1_with_syntax_help()
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Crop", tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, exit);
        Assert.Contains("--crop X,Y,W,H", stderr);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("0:05")]
    [InlineData("0:09-0:03")]
    [InlineData("a:b-c:d")]
    public async Task Invalid_range_exits_1_with_syntax_help(string range)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Trim", "--range", range, tmp.WriteFile("clip.wav", Fixtures.SineWav(0.1)));
        Assert.Equal(1, exit);
        Assert.Contains("--range m:ss-m:ss", stderr);
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,2,0,4")]
    [InlineData("a,b,c,d")]
    [InlineData("1,2,3,4@5")]
    [InlineData("1,2,3,4@0x5")]
    public async Task Invalid_crop_exits_1_with_syntax_help(string crop)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Crop", "--crop", crop, tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, exit);
        Assert.Contains("--crop X,Y,W,H", stderr);
    }

    [Theory]
    [InlineData("0:05", 5.0)]
    [InlineData("1:30", 90.0)]
    [InlineData("0:01.5", 1.5)]
    [InlineData("1:02:03", 3723.0)]
    [InlineData("12", 12.0)]
    [InlineData("0:00.250", 0.25)]
    public void Time_syntax(string text, double seconds)
    {
        Assert.True(CliRunner.TryParseTime(text, out var t));
        Assert.Equal(seconds, t.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.5:00")]
    [InlineData("1:2:3:4")]
    [InlineData("x")]
    public void Time_syntax_rejects(string text) => Assert.False(CliRunner.TryParseTime(text, out _));
}
