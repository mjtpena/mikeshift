using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Tests;

public class ActionResultTests
{
    [Fact]
    public void Toast_texts_follow_the_plan()
    {
        Assert.Equal("photo 2.png", new ActionResult
        {
            Total = 1,
            Outputs = [new ActionOutput(@"C:\a\photo.heic", @"C:\a\photo 2.png", false)],
        }.ToastText);

        Assert.Equal("3 files saved", new ActionResult
        {
            Total = 3,
            Outputs = [.. Enumerable.Range(1, 3).Select(i => new ActionOutput($@"C:\a\{i}.heic", $@"C:\a\{i}.png", false))],
        }.ToastText);

        Assert.Equal("2 saved, 1 skipped", new ActionResult
        {
            Total = 3,
            Outputs = [.. Enumerable.Range(1, 2).Select(i => new ActionOutput($@"C:\a\{i}.heic", $@"C:\a\{i}.png", false))],
            Failures = [new ActionFailure(@"C:\a\3.heic", "bad")],
        }.ToastText);
    }

    [Fact]
    public void Toast_appends_already_and_downloads_notes()
    {
        var r = new ActionResult
        {
            Total = 3,
            Outputs = [new ActionOutput(@"C:\a\1.jpg", @"C:\dl\1.png", true)],
            AlreadyInFormat = 2,
            AlreadyLabel = "PNG",
        };
        Assert.Equal("1.png · 2 already PNG · in Downloads", r.ToastText);
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
