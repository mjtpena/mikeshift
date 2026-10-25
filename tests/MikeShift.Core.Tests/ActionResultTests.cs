using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests;

public class ActionResultTests
{
    [Fact]
    public void Downloads_fallback_is_reported()
    {
        var r = new ActionResult
        {
            Total = 3,
            Outputs = [new ActionOutput(@"C:\a\1.jpg", @"C:\dl\1.png", true)],
            AlreadyInFormat = 2,
            AlreadyLabel = "PNG",
        };
        Assert.True(r.AnyDownloadsFallback);
    }

    [Fact]
    public void Failure_lines_are_capped_with_a_remainder_line()
    {
        var failures = Enumerable.Range(1, 8).Select(i => new ActionFailure($@"C:\a\f{i}.png", "nope")).ToList();
        var r = new ActionResult { Total = 9, Failures = failures, Outputs = [new ActionOutput(@"C:\a\ok.png", @"C:\a\ok.jpg", false)] };
        Assert.Equal(ActionResult.MaxListedFailures + 1, r.FailureLines.Count);
        Assert.Equal("…and 2 more", r.FailureLines[^1]);
    }

    [Fact]
    public void AllFailed_is_false_when_cancelled_or_when_something_was_written()
    {
        Assert.False(new ActionResult { Total = 2, Failures = [new ActionFailure("a", "x")], Cancelled = true }.AllFailed);
        Assert.False(new ActionResult
        {
            Total = 2,
            Failures = [new ActionFailure("a", "x")],
            Outputs = [new ActionOutput("b", "b.out", false)],
        }.AllFailed);
        Assert.Null(new ActionResult { Total = 1 }.AllFailedMessage);
    }
}
