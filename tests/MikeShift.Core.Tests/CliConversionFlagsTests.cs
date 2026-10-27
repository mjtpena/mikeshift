using MikeShift.Core;
using MikeShift.Core.Cli;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests;

/// <summary>The command-line mirror of the conversion settings: <c>--quality</c>, <c>--compress</c>, <c>--resize</c>, <c>--gif-seconds</c>, <c>--max-size</c>, <c>--out</c>.</summary>
public class CliConversionFlagsTests
{
    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = await CliRunner.RunAsync(args, o, e, Capabilities.All with { HeifEncode = false });
        return (exit, o.ToString(), e.ToString());
    }

    private static long Size(string path) => new FileInfo(path).Length;

    [Fact]
    public void The_usage_text_documents_every_flag_and_its_choices()
    {
        foreach (var flag in new[] { "--quality N", "--compress LEVEL", "--resize P", "--gif-seconds N", "--max-size MB", "--out DIR" })
            Assert.Contains(flag, CliRunner.UsageText);
        Assert.Contains("50 to 100", CliRunner.UsageText);
        Assert.Contains("light, medium or strong", CliRunner.UsageText);
        Assert.Contains("25, 50 or 75", CliRunner.UsageText);
        Assert.Contains("5, 10, 15 or 30", CliRunner.UsageText);
        Assert.Contains("1 to 2000", CliRunner.UsageText);
    }

    [Theory]
    [InlineData("--quality", "49", "--quality must be")]
    [InlineData("--quality", "101", "--quality must be")]
    [InlineData("--quality", "high", "--quality must be")]
    [InlineData("--quality", "", "--quality must be")]
    [InlineData("--quality", "-60", "--quality must be")]
    [InlineData("--compress", "extreme", "--compress must be")]
    [InlineData("--compress", "1", "--compress must be")]
    [InlineData("--resize", "30", "--resize must be")]
    [InlineData("--resize", "0", "--resize must be")]
    [InlineData("--resize", "half", "--resize must be")]
    [InlineData("--gif-seconds", "20", "--gif-seconds must be")]
    [InlineData("--gif-seconds", "0", "--gif-seconds must be")]
    [InlineData("--gif-seconds", "15s", "--gif-seconds must be")]
    [InlineData("--max-size", "0", "--max-size must be")]
    [InlineData("--max-size", "2001", "--max-size must be")]
    [InlineData("--max-size", "9.5", "--max-size must be")]
    [InlineData("--max-size", "-10", "--max-size must be")]
    [InlineData("--max-size", "ten", "--max-size must be")]
    [InlineData("--max-size", "", "--max-size must be")]
    public async Task A_bad_value_is_a_usage_error_naming_the_flag(string flag, string value, string message)
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.png", Fixtures.PngRgb());

        var (exit, stdout, stderr) = await Run("--run", "Zip", flag, value, file);

        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.Contains("error: " + message, stderr);
        Assert.Contains("usage:", stderr);
        Assert.Equal(["a.png"], FileNames(tmp.Path)); // nothing was converted
    }

    [Theory]
    [InlineData("--quality")]
    [InlineData("--compress")]
    [InlineData("--resize")]
    [InlineData("--gif-seconds")]
    [InlineData("--max-size")]
    [InlineData("--out")]
    public async Task A_flag_without_its_value_is_a_usage_error(string flag)
    {
        var (exit, stdout, stderr) = await Run("--run", "Zip", "a.png", flag);
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.Contains($"{flag} needs", stderr);
    }

    [Fact]
    public async Task Out_must_be_an_existing_folder()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.png", Fixtures.PngRgb());

        var (missingExit, _, missingErr) = await Run("--out", tmp.Combine("nope"), "--run", "JPG", file);
        var (fileExit, _, fileErr) = await Run("--out", file, "--run", "JPG", file);

        Assert.Equal(2, missingExit);
        Assert.Contains("--out:", missingErr);
        Assert.Contains("does not exist", missingErr);
        Assert.Equal(2, fileExit);
        Assert.Contains("is a file", fileErr);
        Assert.False(Directory.Exists(tmp.Combine("nope"))); // not created behind the user's back
        Assert.Equal(["a.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task Quality_changes_the_size_of_a_jpg()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("photo.png", Fixtures.PhotoPng(900, 600));

        var low = await Run("--run", "JPG", "--quality", "50", file);
        var normal = await Run("--run", "JPG", file);
        var high = await Run("--quality", "100", "--run", "JPG", file); // flags may come before --run too

        Assert.All(new[] { low, normal, high }, r => Assert.Equal(0, r.Exit));
        Assert.True(Size(low.Out.Trim()) < Size(normal.Out.Trim()));
        Assert.True(Size(normal.Out.Trim()) < Size(high.Out.Trim()));
        Assert.Equal(File.ReadAllBytes(normal.Out.Trim()), File.ReadAllBytes(
            (await Run("--run", "JPG", "--quality", "90", file)).Out.Trim())); // 90 is the default
    }

    [Fact]
    public async Task Compress_levels_are_ordered_by_size()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("wide.png", Fixtures.PhotoPng(4200, 700));

        var light = await Run("--run", "Compress", "--compress", "light", file);
        var medium = await Run("--run", "Compress", "--compress", "MEDIUM", file);
        var strong = await Run("--run", "Compress", "--compress", "strong", file);
        var plain = await Run("--run", "Compress", file);

        Assert.All(new[] { light, medium, strong, plain }, r => Assert.Equal(0, r.Exit));
        Assert.True(Size(light.Out.Trim()) > Size(medium.Out.Trim()));
        Assert.True(Size(medium.Out.Trim()) > Size(strong.Out.Trim()));
        Assert.Equal(File.ReadAllBytes(medium.Out.Trim()), File.ReadAllBytes(plain.Out.Trim())); // the default is Medium
    }

    [Fact]
    public async Task Resize_flag_title_and_listing_agree()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb(400, 300));

        var listed = await Run("--list", "--resize", "75", file);
        Assert.Contains("Resize 75%", listed.Out);
        Assert.DoesNotContain("Resize 50%", listed.Out);
        Assert.Contains("Resize 50%", (await Run("--list", file)).Out);

        var viaFlag = await Run("--run", "Resize", "--resize", "25", file);
        var viaTitle = await Run("--run", "Resize 75%", file);
        var legacy = await Run("--run", "Resize 50%", file);
        var viaBoth = await Run("--run", "Resize 50%", "--resize", "50", file);

        Assert.Equal(tmp.Combine("pic 25%.png"), viaFlag.Out.Trim());
        Assert.Equal(tmp.Combine("pic 75%.png"), viaTitle.Out.Trim());
        Assert.Equal(tmp.Combine("pic 50%.png"), legacy.Out.Trim());
        Assert.Equal(tmp.Combine("pic 50% 2.png"), viaBoth.Out.Trim()); // title and flag agree: fine (a second 50 % copy, so the name is taken)
        Assert.Equal((100, 75), (Decode(viaFlag.Out.Trim()).Width, Decode(viaFlag.Out.Trim()).Height));
        Assert.Equal((300, 225), (Decode(viaTitle.Out.Trim()).Width, Decode(viaTitle.Out.Trim()).Height));
    }

    [Fact]
    public async Task A_title_and_a_resize_flag_that_disagree_are_a_usage_error()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb(400, 300));

        var (exit, stdout, stderr) = await Run("--run", "Resize 25%", "--resize", "75", file);

        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.Contains("disagree", stderr);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Max_size_flag_title_and_listing_agree()
    {
        using var tmp = new TempDir();
        var mp4 = Media.MediaTestSupport.CopyMp4(tmp);

        var plain = await Run("--list", mp4);
        Assert.Contains("Fit 10 MB, Fit 25 MB", plain.Out);
        var listed = await Run("--list", "--max-size", "8", mp4);
        Assert.Contains("Fit 8 MB", listed.Out);
        Assert.DoesNotContain("Fit 10 MB", listed.Out);

        var viaFlag = await Run("--run", "Fit", "--max-size", "8", mp4);
        var viaTitle = await Run("--run", "Fit 3 MB", mp4);
        var fixedSize = await Run("--run", "fit 10 mb", mp4);
        var viaBoth = await Run("--run", "Fit 25 MB", "--max-size", "25MB", mp4);
        // Any spelling the size is read from names the same tile.
        var squeezed = await Run("--run", "Fit 4MB", mp4);
        var spaced = await Run("--run", "fit  5 mb", mp4);
        var padded = await Run("--run", "Fit 006 MB", mp4);

        Assert.Equal(tmp.Combine("clip 8 MB.mp4"), viaFlag.Out.Trim());
        Assert.Equal(tmp.Combine("clip 3 MB.mp4"), viaTitle.Out.Trim());
        Assert.Equal(tmp.Combine("clip 10 MB.mp4"), fixedSize.Out.Trim());
        Assert.Equal(tmp.Combine("clip 25 MB.mp4"), viaBoth.Out.Trim());
        Assert.Equal(tmp.Combine("clip 4 MB.mp4"), squeezed.Out.Trim());
        Assert.Equal(tmp.Combine("clip 5 MB.mp4"), spaced.Out.Trim());
        Assert.Equal(tmp.Combine("clip 6 MB.mp4"), padded.Out.Trim());
        Assert.InRange(Size(viaTitle.Out.Trim()), 1, 3_000_000);
    }

    [Theory]
    [InlineData("Fit 10 MB", "--max-size", "25")]
    [InlineData("Fit 8 MB", "--max-size", "9")]
    public async Task A_fit_title_and_a_max_size_that_disagree_are_a_usage_error(string title, string flag, string value)
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb(40, 30));

        var (exit, stdout, stderr) = await Run("--run", title, flag, value, file);

        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.Contains("disagree", stderr);
    }

    [Theory]
    [InlineData("Fit 0 MB")]
    [InlineData("Fit 2001 MB")]
    public async Task A_fit_title_outside_the_sizes_is_a_usage_error(string title)
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb(40, 30));

        var (exit, _, stderr) = await Run("--run", title, file);

        Assert.Equal(2, exit);
        Assert.Contains("1 to 2000 MB", stderr);
    }

    [Fact]
    public async Task Out_puts_the_result_in_that_folder()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("converted");
        var file = tmp.WriteFile(@"in\pic.png", Fixtures.PngRgb());

        var (exit, stdout, stderr) = await Run("--run", "JPG", "--out", dest, file);

        Assert.Equal(0, exit);
        Assert.Equal(Path.Combine(dest, "pic.jpg"), stdout.Trim());
        Assert.DoesNotContain("Downloads", stderr);
        Assert.Equal(["pic.png"], FileNames(tmp.Combine("in")));
    }

    [Fact]
    public async Task An_out_folder_that_cannot_be_written_falls_back_to_downloads_and_says_so()
    {
        using var tmp = new TempDir();
        var locked = tmp.CreateDir("locked");
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        (int Exit, string Out, string Err) r;
        using (TempDir.DenyWrites(locked))
            r = await Run("--run", "JPG", "--out", locked, file);

        Assert.Equal(0, r.Exit);
        Assert.Equal(Path.Combine(tmp.Downloads, "pic.jpg"), r.Out.Trim());
        Assert.Contains($"note: saved in Downloads ({locked} is not writable)", r.Err);
    }

    [Fact]
    public async Task Without_flags_nothing_changes()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var (exit, stdout, stderr) = await Run("--run", "JPG", file);

        Assert.Equal(0, exit);
        Assert.Equal(tmp.Combine("pic.jpg"), stdout.Trim());
        Assert.Equal("", stderr);
    }

}
