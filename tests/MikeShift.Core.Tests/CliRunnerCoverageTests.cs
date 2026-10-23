using MikeShift.Core.Cli;

namespace MikeShift.Core.Tests;

/// <summary>
/// In-process coverage of <see cref="CliRunner"/>: every exit path, argument-parsing corner and note line.
/// (The sibling <c>CliRunnerTests</c> hold the basic contract; the child-process tests live in MikeShift.Cli.Tests.)
/// </summary>
public class CliRunnerCoverageTests
{
    private static async Task<(int Exit, string Out, string Err)> RunWith(string[] args, CancellationToken ct)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = await CliRunner.RunAsync(args, o, e, Capabilities.All, ct);
        return (exit, o.ToString(), e.ToString());
    }

    /// <summary>A PNG whose header claims 300 megapixels: classified as an image, refused by the converter's size guard.</summary>
    private static byte[] TooBigPng() => Fixtures.PngHeaderOnly(20_000, 15_000);

    private static Task<(int Exit, string Out, string Err)> Run(params string[] args) => RunWith(args, default);

    // ------------------------------------------------------------------ argument parsing

    [Fact]
    public async Task No_arguments_prints_usage_without_an_error_line()
    {
        var (exit, stdout, stderr) = await Run();
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("usage:", stderr);
        Assert.DoesNotContain("error:", stderr);
    }

    [Theory]
    [InlineData(new[] { "--list" }, "no files given")]
    [InlineData(new[] { "--run", "Zip" }, "no files given")]
    [InlineData(new[] { "--run" }, "--run needs an action title")]
    [InlineData(new[] { "--run", "Zip", "--crop" }, "--crop needs a value")]
    [InlineData(new[] { "--run", "Zip", "--range" }, "--range needs a value")]
    [InlineData(new[] { "--list", "--run", "Zip", "a.txt" }, "use either --list or --run")]
    [InlineData(new[] { "--run", "Zip", "--list", "a.txt" }, "use either --list or --run")]
    [InlineData(new[] { "--run", "Zip", "--run", "PNG", "a.txt" }, "use either --list or --run")]
    [InlineData(new[] { "--list", "--range", "0:01-0:02", "a.txt" }, "--range and --crop only apply to --run")]
    [InlineData(new[] { "--list", "--crop", "1,2,3,4", "a.txt" }, "--range and --crop only apply to --run")]
    [InlineData(new[] { "-x", "a.txt" }, "unknown option -x")]
    [InlineData(new[] { "--LIST", "a.txt" }, "unknown option --LIST")]
    public async Task Each_usage_mistake_names_the_problem_and_prints_usage(string[] args, string message)
    {
        var (exit, stdout, stderr) = await Run(args);
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: " + message, stderr);
        Assert.EndsWith(CliRunner.UsageText, stderr);
    }

    [Fact]
    public async Task Flags_after_the_double_dash_are_file_names_and_a_lone_dash_is_a_file_name()
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--list", "-");
        Assert.Equal(1, exit);
        Assert.Equal("error: not found: -" + Environment.NewLine, stderr);

        var (exit2, _, stderr2) = await Run("--list", "--", "--range");
        Assert.Equal(1, exit2);
        Assert.Contains("not found: --range", stderr2);
    }

    [Fact]
    public async Task An_unusable_path_is_an_error_not_a_crash()
    {
        var (exit, stdout, stderr) = await Run("--list", "bad\0name.png");
        Assert.Equal(1, exit);
        Assert.Equal("", stdout);
        Assert.Contains("invalid path", stderr);
    }

    [Fact]
    public async Task List_for_a_folder_selection_is_just_zip_and_a_second_missing_file_is_reported_by_name()
    {
        using var tmp = new TempDir();
        var (exit, stdout, _) = await Run("--list", tmp.CreateDir("folder"));
        Assert.Equal(0, exit);
        Assert.Equal("Zip", stdout.Trim());

        var (exit2, _, stderr2) = await Run("--list", tmp.WriteFile("ok.txt", "x"), tmp.Combine("gone.txt"));
        Assert.Equal(1, exit2);
        Assert.Contains("not found: " + tmp.Combine("gone.txt"), stderr2);
    }

    // ------------------------------------------------------------------ --range / --crop parsing

    [Theory]
    [InlineData("0:05-")]
    [InlineData("-0:05")]
    [InlineData("1-2-3")]
    [InlineData("5")]
    [InlineData("0:05 0:09")]
    [InlineData("0:05--0:09")]
    [InlineData("a-b")]
    [InlineData("1:xx-2:00")]
    [InlineData("0:05-1:2:3:4")]
    public async Task Unreadable_ranges_say_so_and_show_the_syntax(string range)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Trim", "--range", range, tmp.WriteFile("c.wav", Fixtures.SineWav(0.1)));
        Assert.Equal(1, exit);
        Assert.Contains($"can't read the range \"{range}\"", stderr);
        Assert.Contains(CliRunner.RangeSyntax, stderr);
    }

    [Theory]
    [InlineData("0:05-0:05")]
    [InlineData("0:09-0:03")]
    public async Task A_range_that_ends_before_it_starts_is_rejected_with_its_own_message(string range)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Trim", "--range", range, tmp.WriteFile("c.wav", Fixtures.SineWav(0.1)));
        Assert.Equal(1, exit);
        Assert.Contains($"the range \"{range}\" ends before it starts", stderr);
    }

    [Theory]
    [InlineData("10,10")]
    [InlineData("1,2,3,4,5")]
    [InlineData("-1,2,3,4")]
    [InlineData("1.5,2,3,4")]
    [InlineData("1,2,3,4@")]
    [InlineData("1,2,3,4@5x")]
    [InlineData("1,2,3,4@5x6x7")]
    [InlineData("1,2,3,4@x6")]
    [InlineData("1,2,3,4@-5x6")]
    public async Task Unreadable_crops_say_so_and_show_the_syntax(string crop)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Crop", "--crop", crop, tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, exit);
        Assert.Contains($"can't read the crop \"{crop}\"", stderr);
        Assert.Contains(CliRunner.CropSyntax, stderr);
    }

    [Theory]
    [InlineData("1,2,0,4", "the crop width and height must be positive")]
    [InlineData("1,2,3,0", "the crop width and height must be positive")]
    [InlineData("1,2,3,4@0x5", "the crop output size must be positive")]
    [InlineData("1,2,3,4@5x0", "the crop output size must be positive")]
    public async Task Zero_sizes_in_a_crop_get_a_specific_message(string crop, string message)
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Crop", "--crop", crop, tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, exit);
        Assert.Contains("error: " + message, stderr);
    }

    [Fact]
    public async Task Range_and_crop_are_only_parsed_after_the_action_is_known()
    {
        using var tmp = new TempDir();
        var (exit, _, stderr) = await Run("--run", "Frobnicate", "--range", "garbage", tmp.WriteFile("a.png", Fixtures.Png()));
        Assert.Equal(1, exit);
        Assert.Contains("unknown action", stderr);
        Assert.DoesNotContain("can't read the range", stderr);
    }

    [Theory]
    [InlineData(" 0:00 - 0:00.05 ")]
    [InlineData("0-0.05")]
    [InlineData("0:00:00-0:00:00.05")]
    public async Task A_valid_range_in_any_spelling_trims_a_wav(string range)
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("clip.wav", Fixtures.SineWav(0.2));
        using var guard = OriginalsGuard.Watch(wav);

        var (exit, stdout, stderr) = await Run("--run", "Trim", "--range", range, wav);

        Assert.Equal(0, exit);
        Assert.Equal("", stderr);
        var output = stdout.Trim();
        Assert.True(File.Exists(output));
        Assert.NotEqual(wav, output);
        Assert.True(new FileInfo(output).Length < new FileInfo(wav).Length);
    }

    [Fact]
    public async Task A_valid_crop_with_an_output_size_scales_the_result_and_the_x_may_be_upper_case()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        using var guard = OriginalsGuard.Watch(png);

        var (exit, stdout, stderr) = await Run("--run", "Crop", "--crop", "8,8,32,24@16X12", png);

        Assert.Equal(0, exit);
        Assert.Equal("", stderr);
        var decoded = Images.ImageTestUtil.Decode(stdout.Trim());
        Assert.Equal((16, 12), (decoded.Width, decoded.Height));
    }

    [Fact]
    public async Task A_crop_without_an_output_size_keeps_the_cropped_size()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var (exit, stdout, _) = await Run("--run", "Crop", "--crop", " 0 , 0 , 20 , 10 ", png);
        Assert.Equal(0, exit);
        var decoded = Images.ImageTestUtil.Decode(stdout.Trim());
        Assert.Equal((20, 10), (decoded.Width, decoded.Height));
    }

    // ------------------------------------------------------------------ result reporting

    [Fact]
    public async Task Running_with_a_cancelled_token_ends_with_the_cancelled_error_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.txt", "x");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (exit, stdout, stderr) = await RunWith(["--run", "Zip", file], cts.Token);

        Assert.Equal(1, exit);
        Assert.Equal("", stdout);
        Assert.Equal("error: cancelled" + Environment.NewLine, stderr);
        Assert.False(File.Exists(tmp.Combine("a.zip")));
    }

    [Fact]
    public async Task Files_already_in_the_target_format_are_skipped_with_a_note_and_the_rest_converted()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());
        var jpg = tmp.WriteFile("b.jpg", Fixtures.QuadrantJpeg());

        var (exit, stdout, stderr) = await Run("--run", "PNG", png, jpg);

        Assert.Equal(0, exit);
        Assert.Equal(tmp.Combine("b.png"), stdout.Trim());
        Assert.Equal("note: 1 already PNG" + Environment.NewLine, stderr);
    }

    [Fact]
    public async Task An_output_that_had_to_go_to_downloads_is_announced_on_stderr()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("readonly");
        var file = tmp.WriteFile("readonly/a.txt", "x");

        (int Exit, string Out, string Err) r;
        using (TempDir.DenyWrites(folder))
            r = await Run("--run", "Zip", file);

        Assert.Equal(0, r.Exit);
        Assert.Equal(tmp.Combine("__downloads", "a.zip"), r.Out.Trim());
        Assert.Contains("note: saved in Downloads (the original's folder is not writable)", r.Err);
    }

    [Fact]
    public async Task A_batch_where_one_file_fails_prints_the_good_output_names_the_bad_file_and_exits_1()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("good.png", Fixtures.PngRgb(32, 24));
        var bad = tmp.WriteFile("bad.png", TooBigPng()); // over the megapixel limit
        using var guard = OriginalsGuard.Watch(good, bad);

        var (exit, stdout, stderr) = await Run("--run", "JPG", good, bad);

        Assert.Equal(1, exit);
        Assert.Equal(tmp.Combine("good.jpg"), stdout.Trim());
        Assert.StartsWith("error: bad.png: ", stderr);
        Assert.DoesNotContain("   at ", stderr);
        Assert.Equal(["bad.png", "good.jpg", "good.png"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public async Task When_every_file_fails_the_single_summary_line_is_printed_and_nothing_is_written()
    {
        using var tmp = new TempDir();
        var bad1 = tmp.WriteFile("bad1.png", TooBigPng());
        var bad2 = tmp.WriteFile("bad2.png", TooBigPng());

        var (exit, stdout, stderr) = await Run("--run", "JPG", bad1, bad2);

        Assert.Equal(1, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: None of the 2 files could be converted: ", stderr);
        Assert.Single(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["bad1.png", "bad2.png"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public async Task Action_titles_are_matched_ignoring_case_and_padding()
    {
        using var tmp = new TempDir();
        var (exit, stdout, _) = await Run("--run", "  zIp ", tmp.WriteFile("a.txt", "x"));
        Assert.Equal(0, exit);
        Assert.Equal(tmp.Combine("a.zip"), stdout.Trim());
    }

    [Fact]
    public async Task The_default_capabilities_overload_runs_with_the_machines_real_capabilities()
    {
        using var tmp = new TempDir();
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = await CliRunner.RunAsync(["--list", tmp.WriteFile("a.txt", "x")], o, e);
        Assert.Equal(0, exit);
        Assert.Equal("", e.ToString());
        Assert.EndsWith("Zip", o.ToString().Trim());
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData(" 1:00 ", 60.0)]
    [InlineData("0", 0.0)]
    [InlineData("100:00", 6000.0)]
    [InlineData("1:00:00.5", 3600.5)]
    public void Times_accept_padding_and_fractions_on_the_last_segment(string text, double seconds)
    {
        Assert.True(CliRunner.TryParseTime(text, out var t));
        Assert.Equal(seconds, t.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("1:-5")]
    [InlineData("-5")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("::")]
    [InlineData("1:2:3:")]
    [InlineData("0x10")]
    [InlineData("١:٢٣")]
    public void Times_reject_signs_exponents_and_stray_characters(string text) =>
        Assert.False(CliRunner.TryParseTime(text, out _));
}
