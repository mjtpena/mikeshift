using System.Text;
using MikeShift.Core.Hosting;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Hosting;

/// <summary>The small decisions the tray app makes about itself (dev switches, data folder, log size, back-off, welcome window, crash policy).</summary>
public class HostingTests
{
    // ---------------------------------------------------------------- dev switches

    [Theory]
    // Debug builds always honour them, packaged or not, with or without --dev.
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    // Release: only unpackaged AND --dev.
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]   // a packaged Release build ignores them even with --dev
    [InlineData(false, true, false, false)]
    public void Dev_switches_are_honoured_by_debug_builds_and_unpackaged_dev_runs_only(bool debug, bool packaged, bool devFlag, bool expected)
    {
        string[] args = devFlag ? ["MikeShift.exe", "--dev", "--scene", "toast-success"] : ["MikeShift.exe", "--scene", "toast-success"];
        Assert.Equal(expected, DevSwitchPolicy.IsEnabled(debug, packaged, args));
    }

    [Theory]
    [InlineData("--DEV")]
    [InlineData("-dev")]
    [InlineData("--dev=1")]
    [InlineData("dev")]
    public void Only_the_exact_dev_flag_opts_a_release_build_in(string almost) =>
        Assert.False(DevSwitchPolicy.IsEnabled(debugBuild: false, packaged: false, ["MikeShift.exe", almost]));

    [Fact]
    public void The_smoke_test_flag_is_not_a_dev_switch() =>
        Assert.False(DevSwitchPolicy.IsEnabled(debugBuild: false, packaged: false, ["MikeShift.exe", "--smoke-test"]));

    [Fact]
    public void Flag_values_are_the_argument_after_the_flag()
    {
        string[] args = ["MikeShift.exe", "--dev", "--scene", "toast-success", "--convert"];
        Assert.Equal("toast-success", DevSwitchPolicy.ValueAfter(args, "--scene"));
        Assert.Null(DevSwitchPolicy.ValueAfter(args, "--convert")); // last: no value
        Assert.Null(DevSwitchPolicy.ValueAfter(args, "--crop"));
    }

    // ---------------------------------------------------------------- data folder

    [Fact]
    public void Unpackaged_the_data_folder_is_under_local_app_data() =>
        Assert.Equal(@"C:\Users\me\AppData\Local\MikeShift", AppDataPaths.Resolve(null, @"C:\Users\me\AppData\Local"));

    [Fact]
    public void Packaged_the_data_folder_is_the_real_one_inside_the_package_local_cache()
    {
        const string cache = @"C:\Users\me\AppData\Local\Packages\MikeShift_abc123\LocalCache";
        Assert.Equal(cache + @"\Local\MikeShift", AppDataPaths.Resolve(cache, @"C:\Users\me\AppData\Local"));
        Assert.Equal(@"C:\x\MikeShift", AppDataPaths.Resolve("  ", @"C:\x")); // blank cache: not packaged
    }

    // ---------------------------------------------------------------- log trimming

    private static string Line(int i) => $"12:00:00.000 [info ] [t1] line number {i:D6} {new string('x', 40)}{Environment.NewLine}";

    [Fact]
    public void A_small_log_is_left_alone()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("log.txt", Line(1) + Line(2));
        var before = File.ReadAllText(path);
        Assert.False(LogTrimmer.TrimIfLarge(path, maxBytes: 10_000));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(LogTrimmer.TrimIfLarge(tmp.Combine("missing.txt")));
    }

    [Fact]
    public void A_large_log_keeps_its_newest_lines_on_whole_line_boundaries()
    {
        using var tmp = new TempDir();
        var text = new StringBuilder();
        for (var i = 0; i < 400; i++) text.Append(Line(i));
        var path = tmp.WriteFile("log.txt", text.ToString());
        const long cap = 10_000;
        Assert.True(new FileInfo(path).Length > cap);

        Assert.True(LogTrimmer.TrimIfLarge(path, cap));

        var trimmed = File.ReadAllLines(path);
        Assert.True(new FileInfo(path).Length <= cap);
        Assert.Contains("trimmed", trimmed[0]);
        Assert.Equal(Line(399).TrimEnd(), trimmed[^1]);        // the newest line survived
        Assert.All(trimmed.Skip(1), l => Assert.Contains("line number", l)); // no cut-off first line
        Assert.DoesNotContain(trimmed, l => l.Contains("line number 000000"));
        Assert.False(LogTrimmer.TrimIfLarge(path, cap));       // now small enough
    }

    [Fact]
    public void A_log_that_grows_again_is_trimmed_again()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("log.txt");
        const long cap = 5_000;
        var cuts = 0;
        for (var i = 0; i < 600; i++)
        {
            File.AppendAllText(path, Line(i));
            if (i % LogTrimmer.CheckEveryWrites == 0 && LogTrimmer.TrimIfLarge(path, cap)) cuts++;
        }
        Assert.True(cuts >= 2);
        Assert.True(new FileInfo(path).Length < cap + LogTrimmer.CheckEveryWrites * (long)Line(0).Length);
    }

    // ---------------------------------------------------------------- failure back-off

    [Fact]
    public void Back_off_starts_at_50_ms_doubles_and_stops_at_one_second()
    {
        var backoff = new FailureBackoff();
        var delays = Enumerable.Range(0, 8).Select(_ => backoff.NextDelay().TotalMilliseconds).ToArray();
        Assert.Equal([50.0, 100.0, 200.0, 400.0, 800.0, 1000.0, 1000.0, 1000.0], delays);
    }

    [Fact]
    public void A_good_iteration_resets_the_back_off()
    {
        var backoff = new FailureBackoff();
        backoff.NextDelay();
        backoff.NextDelay();
        backoff.Reset();
        Assert.Equal(FailureBackoff.Initial, backoff.NextDelay());
    }

    // ---------------------------------------------------------------- welcome window

    [Theory]
    [InlineData(false, true, false, true)]   // first interactive launch
    [InlineData(false, false, false, true)]  // interactive launch, could not remember: still shown
    [InlineData(false, true, true, true)]    // first launch at login, remembered: shown once
    [InlineData(false, false, true, false)]  // at login and not remembered: it would pop up at every sign-in
    [InlineData(true, true, false, false)]   // already shown
    [InlineData(true, false, true, false)]
    public void The_welcome_window_never_repeats_at_login(bool already, bool remembered, bool atLogin, bool expected) =>
        Assert.Equal(expected, WelcomePolicy.ShouldShow(already, remembered, atLogin));

    // ---------------------------------------------------------------- unhandled exceptions

    [Theory]
    [InlineData(typeof(OutOfMemoryException), true)]
    [InlineData(typeof(InsufficientExecutionStackException), true)]
    [InlineData(typeof(AccessViolationException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(IOException), false)]
    [InlineData(typeof(System.Runtime.InteropServices.COMException), false)]
    public void Only_runtime_corruption_is_fatal(Type type, bool fatal) =>
        Assert.Equal(fatal, UnhandledPolicy.IsFatal((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void Ordinary_exceptions_are_handled_fatal_ones_are_not()
    {
        var policy = new UnhandledPolicy();
        var now = DateTime.UtcNow;
        Assert.True(policy.ShouldHandle(new InvalidOperationException(), now));
        Assert.False(policy.ShouldHandle(new OutOfMemoryException(), now));
    }

    [Fact]
    public void A_storm_of_exceptions_ends_the_app_instead_of_freezing_it()
    {
        var policy = new UnhandledPolicy();
        var t = DateTime.UtcNow;
        for (var i = 0; i < UnhandledPolicy.StormLimit; i++)
            Assert.True(policy.ShouldHandle(new InvalidOperationException(), t.AddMilliseconds(i)));
        Assert.False(policy.ShouldHandle(new InvalidOperationException(), t.AddMilliseconds(UnhandledPolicy.StormLimit)));

        // Spread over time the same number is fine.
        var calm = new UnhandledPolicy();
        for (var i = 0; i < UnhandledPolicy.StormLimit * 3; i++)
            Assert.True(calm.ShouldHandle(new InvalidOperationException(), t.AddSeconds(i * 1.0)));
    }

    // ---------------------------------------------------------------- UI Automation

    [Fact]
    public void The_uia_append_runtime_id_marker_is_the_documented_constant()
    {
        Assert.Equal(3, UiaRuntimeId.Append); // UiaAppendRuntimeId in UIAutomationCoreApi.h (was 0x2A)
        Assert.Equal([3, 1], UiaRuntimeId.ForTile(toolsPage: false, 0));
        Assert.Equal([3, 6], UiaRuntimeId.ForTile(toolsPage: false, 5));
        Assert.Equal([3, 1001], UiaRuntimeId.ForTile(toolsPage: true, 0));
        Assert.NotEqual(UiaRuntimeId.ForTile(false, 2)[1], UiaRuntimeId.ForTile(true, 2)[1]);
    }

    [Fact]
    public void A_uia_call_is_stale_after_the_wheel_was_shown_again_or_the_tile_changed()
    {
        string[] titles = ["Convert to PNG", "Convert to JPG", "Zip"];
        Assert.True(TileIdentity.StillCurrent(7, 7, 1, "Convert to JPG", titles));
        Assert.False(TileIdentity.StillCurrent(7, 8, 1, "Convert to JPG", titles));    // hidden and shown again
        Assert.False(TileIdentity.StillCurrent(7, 7, 1, "Compress", titles));          // page switched: index 1 is another tile
        Assert.False(TileIdentity.StillCurrent(7, 7, 3, "Zip", titles));               // out of range
        Assert.False(TileIdentity.StillCurrent(7, 7, -1, "Zip", titles));
        Assert.False(TileIdentity.StillCurrent(7, 7, 0, "Convert to PNG", []));        // wheel emptied
    }
}
