using Shiftvert.Core.Actions;
using Shiftvert.Core.Feedback;

namespace Shiftvert.Core.Tests.Feedback;

public class FeedbackTests
{
    private static ActionOutput Out(string name) => new($@"C:\a\{name}.heic", $@"C:\a\{name}.png", false);

    private static ActionResult Ok(int n) => new() { Total = n, Outputs = [.. Enumerable.Range(1, n).Select(i => Out(i.ToString()))] };

    private static ActionResult Failures(int total, int failures, int saved = 0) => new()
    {
        Total = total,
        Outputs = [.. Enumerable.Range(1, saved).Select(i => Out("ok" + i))],
        Failures = [.. Enumerable.Range(1, failures).Select(i => new ActionFailure($@"C:\a\bad{i}.heic", $"reason {i}"))],
    };

    [Fact]
    public void Success_shows_toast_and_chimes()
    {
        var plan = FeedbackPlan.For(Ok(3), 0, chimeEnabled: true, toastEnabled: true);
        Assert.Equal("3 files saved", plan.ToastText);
        Assert.Null(plan.Dialog);
        Assert.True(plan.Chime);
    }

    [Fact]
    public void Preferences_gate_toast_and_chime()
    {
        var plan = FeedbackPlan.For(Ok(1), 0, chimeEnabled: false, toastEnabled: false);
        Assert.Null(plan.ToastText);
        Assert.False(plan.Chime);
    }

    [Fact]
    public void Other_jobs_add_still_working_suffix()
    {
        var plan = FeedbackPlan.For(Ok(2), 2, true, true);
        Assert.Equal("2 files saved · 2 still working…", plan.ToastText);
        Assert.Equal("Compress… · 1 still working…", FeedbackPlan.WithBackgroundJobs("Compress…", 1));
    }

    [Fact]
    public void Start_text_uses_drop_description()
    {
        var tool = new ActionDef("Compress", WheelPage.Tool, (_, _, _) => Task.FromResult(new ActionResult()));
        var format = new ActionDef("PNG", WheelPage.Format, (_, _, _) => Task.FromResult(new ActionResult()));
        Assert.Equal("Compress…", FeedbackPlan.StartText(tool, 0));
        Assert.Equal("Convert to PNG… · 1 still working…", FeedbackPlan.StartText(format, 1));
    }

    [Fact]
    public void All_failed_is_a_dialog_without_toast_or_chime()
    {
        var plan = FeedbackPlan.For(Failures(3, 3), 0, true, true);
        Assert.Null(plan.ToastText);
        Assert.False(plan.Chime);
        Assert.Equal("None of the 3 files could be converted: reason 1", plan.Dialog!.Body);
    }

    [Fact]
    public void Single_file_failure_shows_just_the_reason()
    {
        var plan = FeedbackPlan.For(Failures(1, 1), 0, true, true);
        Assert.Equal("reason 1", plan.Dialog!.Body);
    }

    [Fact]
    public void Partial_failure_lists_at_most_six_and_keeps_toast_without_chime()
    {
        var plan = FeedbackPlan.For(Failures(10, 8, saved: 2), 0, true, true);
        Assert.Equal("2 saved, 8 skipped", plan.ToastText);
        Assert.False(plan.Chime);
        var lines = plan.Dialog!.Body.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2 saved, 8 skipped:", lines[0]);
        Assert.Equal(1 + 6 + 1, lines.Length); // header + 6 failures + "…and 2 more"
        Assert.Equal("bad1.heic: reason 1", lines[1]);
        Assert.Equal("…and 2 more", lines[^1]);
    }

    [Fact]
    public void Cancelled_with_nothing_written_has_no_chime_and_a_cancel_toast()
    {
        var plan = FeedbackPlan.For(new ActionResult { Total = 2, Cancelled = true }, 0, true, true);
        Assert.Equal("Cancelled", plan.ToastText);
        Assert.False(plan.Chime);
        Assert.Null(plan.Dialog);
    }

    [Fact]
    public void Nothing_at_all_reads_nothing_to_convert()
    {
        var plan = FeedbackPlan.For(new ActionResult { Total = 0 }, 0, true, true);
        Assert.Equal("Nothing to convert", plan.ToastText);
        Assert.False(plan.Chime);
    }

    [Fact]
    public void Already_in_format_note_survives()
    {
        var r = new ActionResult { Total = 2, Outputs = [Out("1")], AlreadyInFormat = 1, AlreadyLabel = "PNG" };
        Assert.Equal("1.png · 1 already PNG", FeedbackPlan.For(r, 0, true, true).ToastText);
    }
}

public class JobLedgerTests
{
    [Fact]
    public void Tracks_running_jobs_and_last_outputs()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var first = now;
        var ledger = new JobLedger(() => now);
        Assert.Null(ledger.LastFinishedAt);
        Assert.Empty(ledger.LastOutputs);

        var a = ledger.Begin();
        var b = ledger.Begin();
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(2, ledger.RunningCount);
        Assert.Equal(1, ledger.OthersRunning(a));

        ledger.Complete(a, new ActionResult
        {
            Total = 1,
            Outputs = [new ActionOutput(@"C:\x\a.heic", @"C:\x\a.png", false)],
        });
        Assert.Equal(1, ledger.RunningCount);
        Assert.Equal([@"C:\x\a.png"], ledger.LastOutputs);
        Assert.Equal(first, ledger.LastFinishedAt);
        Assert.Equal(0, ledger.OthersRunning(b));

        // A job that wrote nothing leaves LastOutputs alone.
        now = now.AddMinutes(1);
        ledger.Complete(b, new ActionResult { Total = 1, Cancelled = true });
        Assert.Equal(0, ledger.RunningCount);
        Assert.Equal([@"C:\x\a.png"], ledger.LastOutputs);
        Assert.Equal(first, ledger.LastFinishedAt);
    }

    [Fact]
    public void Complete_with_null_result_still_frees_the_slot()
    {
        var ledger = new JobLedger();
        var id = ledger.Begin();
        ledger.Complete(id, null);
        Assert.Equal(0, ledger.RunningCount);
    }
}

public class CodecSummaryTests
{
    [Fact]
    public void All_capabilities_report_available_and_none_report_missing()
    {
        Assert.All(CodecSummary.From(Capabilities.All), c => Assert.True(c.Available));
        Assert.All(CodecSummary.From(Capabilities.None), c => Assert.False(c.Available));
        Assert.Contains(CodecSummary.From(Capabilities.None), c => c.Name.Contains("WebP") && c.Hint.Contains("Store"));
    }
}
