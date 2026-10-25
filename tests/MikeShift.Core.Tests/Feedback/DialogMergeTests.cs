using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;

namespace MikeShift.Core.Tests.Feedback;

/// <summary>Several failed jobs while one failure dialog is open become one dialog, not one topmost window each.</summary>
public class DialogMergeTests
{
    private static DialogContent Single(string file, string reason) =>
        FeedbackPlan.DialogFor(ActionResult.Failed(1, @"C:\in\" + file, reason))!;

    private static DialogContent Batch(int files) =>
        FeedbackPlan.DialogFor(new ActionResult
        {
            Total = files,
            Failures = [.. Enumerable.Range(1, files).Select(i => new ActionFailure($@"C:\in\f{i}.png", "This image is damaged."))],
        })!;

    [Fact]
    public void Failure_dialogs_are_marked_as_job_problems_and_other_dialogs_are_not()
    {
        Assert.True(Single("a.pdf", "This PDF is damaged and can't be read.").JobProblem);
        Assert.False(DialogContent.Simple("Title", "Text").JobProblem);
        Assert.False(new DialogContent(FeedbackCopy.ElevatedTitle, FeedbackCopy.ElevatedBody, [], null, DialogSeverity.Warning,
            [new DialogButton("Quit MikeShift", DialogCommand.QuitApp)]).JobProblem);
    }

    [Fact]
    public void One_notice_is_shown_as_it_is()
    {
        var one = Single("a.pdf", "This PDF is damaged and can't be read.");
        Assert.Same(one, DialogMerge.Combine([one], jobCount: 1));
    }

    [Fact]
    public void Two_failed_jobs_become_one_dialog_listing_both()
    {
        var merged = DialogMerge.Combine([Single("a.pdf", "This PDF is damaged and can't be read."), Single("b.png", "This image is damaged.")], jobCount: 2);

        Assert.Equal("2 jobs had problems", merged.Title);
        Assert.Equal(DialogSeverity.Error, merged.Severity);
        Assert.Equal(2, merged.Items.Count);
        Assert.Contains("a.pdf", merged.Items[0].Name);
        Assert.Contains("b.png", merged.Items[1].Name);
        Assert.Single(merged.Buttons);
        Assert.Equal(DialogCommand.Close, merged.Buttons[0].Command);
        Assert.True(merged.JobProblem);
    }

    [Fact]
    public void A_batch_and_a_single_failure_keep_every_file_row()
    {
        var merged = DialogMerge.Combine([Batch(3), Single("z.pdf", "This PDF is damaged and can't be read.")], jobCount: 2);
        Assert.Equal(4, merged.Items.Count);
        Assert.Equal("f1.png", merged.Items[0].Name);
        Assert.Contains("z.pdf", merged.Items[3].Name);
    }

    [Fact]
    public void A_partial_failure_alone_is_a_warning_and_with_an_error_the_aggregate_is_an_error()
    {
        var partial = FeedbackPlan.DialogFor(new ActionResult
        {
            Total = 2,
            Outputs = [new ActionOutput(@"C:\in\a.heic", @"C:\in\a.png", false)],
            Failures = [new ActionFailure(@"C:\in\b.heic", "This image is damaged.")],
        })!;
        Assert.Equal(DialogSeverity.Warning, DialogMerge.Combine([partial, partial], jobCount: 2).Severity);
        Assert.Equal(DialogSeverity.Error, DialogMerge.Combine([partial, Single("x.pdf", "This PDF is damaged and can't be read.")], jobCount: 2).Severity);
    }

    [Fact]
    public void The_list_is_capped_and_the_job_count_counts_every_job()
    {
        var notices = Enumerable.Range(0, 30).Select(i => Single($"f{i}.png", "This image is damaged.")).ToArray();
        var merged = DialogMerge.Combine(notices, jobCount: 120); // 90 older notices were dropped by the caller

        Assert.Equal("120 jobs had problems", merged.Title);
        Assert.Equal(DialogMerge.MaxItems + 1, merged.Items.Count);
        Assert.Equal($"…and {30 - DialogMerge.MaxItems} more", merged.Items[^1].Name);
    }

    [Fact]
    public void Nothing_to_combine_is_an_error() =>
        Assert.Throws<ArgumentException>(() => DialogMerge.Combine([], 0));
}
