using Shiftvert.Core;
using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Tests;

public class EachTests
{
    private static readonly ActionContext Ctx = new(Capabilities.All);

    private static string[] Inputs(int n, string ext = "png") =>
        Enumerable.Range(1, n).Select(i => $@"C:\in\file{i}.{ext}").ToArray();

    private static Each.FileStep Echo(Func<string, bool>? fail = null) => (input, _) =>
        fail?.Invoke(input) == true
            ? throw new ConversionException("bad " + Path.GetFileName(input))
            : Task.FromResult(Path.ChangeExtension(input, "out"));

    [Fact]
    public async Task All_succeed_in_order_and_sequentially()
    {
        var running = 0;
        var maxParallel = 0;
        var order = new List<string>();
        var result = await Each.RunAsync(Inputs(4), Ctx, async (input, ct) =>
        {
            maxParallel = Math.Max(maxParallel, Interlocked.Increment(ref running));
            await Task.Delay(5, ct);
            order.Add(Path.GetFileName(input));
            Interlocked.Decrement(ref running);
            return Path.ChangeExtension(input, "out");
        }, CancellationToken.None);

        Assert.Equal(1, maxParallel);
        Assert.Equal(["file1.png", "file2.png", "file3.png", "file4.png"], order);
        Assert.Equal(4, result.Outputs.Count);
        Assert.Empty(result.Failures);
        Assert.True(result.Succeeded);
        Assert.Equal("4 files saved", result.ToastText);
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_rest_and_is_listed()
    {
        var result = await Each.RunAsync(Inputs(3), Ctx, Echo(p => p.EndsWith("file2.png")), CancellationToken.None);

        Assert.Equal(2, result.Outputs.Count);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad file2.png", failure.Message);
        Assert.True(result.PartialFailure);
        Assert.False(result.AllFailed);
        Assert.Equal("2 saved, 1 skipped", result.ToastText);
        Assert.Equal(["file2.png: bad file2.png"], result.FailureLines);
    }

    [Fact]
    public async Task All_failing_reports_the_first_reason_with_the_count()
    {
        var result = await Each.RunAsync(Inputs(3), Ctx, Echo(_ => true), CancellationToken.None);

        Assert.True(result.AllFailed);
        Assert.Equal("None of the 3 files could be converted: bad file1.png", result.AllFailedMessage);
        Assert.Equal(result.AllFailedMessage, result.ToastText);
    }

    [Fact]
    public async Task A_single_failing_file_reports_just_the_reason()
    {
        var result = await Each.RunAsync(Inputs(1), Ctx, Echo(_ => true), CancellationToken.None);
        Assert.Equal("bad file1.png", result.AllFailedMessage);
    }

    [Fact]
    public async Task Partial_failure_list_is_capped_at_six()
    {
        var result = await Each.RunAsync(Inputs(10), Ctx, Echo(p => !p.EndsWith("file10.png")), CancellationToken.None);

        Assert.Single(result.Outputs);
        Assert.Equal(9, result.Failures.Count);
        Assert.Equal(7, result.FailureLines.Count);
        Assert.Equal("…and 3 more", result.FailureLines[^1]);
        Assert.Equal("file1.png: bad file1.png", result.FailureLines[0]);
    }

    [Fact]
    public async Task Unexpected_exceptions_become_failures_not_crashes()
    {
        var result = await Each.RunAsync(Inputs(2), Ctx, (input, _) =>
            input.EndsWith("1.png") ? throw new UnauthorizedAccessException() : Task.FromResult(input + ".out"), CancellationToken.None);

        Assert.Single(result.Outputs);
        Assert.Equal("Access to the file was denied.", Assert.Single(result.Failures).Message);
    }

    [Fact]
    public async Task Files_already_in_the_target_format_are_skipped_and_counted_separately()
    {
        string[] inputs = [@"C:\in\a.png", @"C:\in\b.jpg", @"C:\in\c.PNG"];
        var converted = new List<string>();
        var result = await Each.RunAsync(inputs, Ctx, (input, _) =>
        {
            converted.Add(input);
            return Task.FromResult(Path.ChangeExtension(input, "png"));
        }, CancellationToken.None, skipExtensions: new HashSet<string> { "png" }, alreadyLabel: "PNG");

        Assert.Equal([@"C:\in\b.jpg"], converted);
        Assert.Equal(2, result.AlreadyInFormat);
        Assert.Single(result.Outputs);
        Assert.Equal("b.png · 2 already PNG", result.ToastText);
        Assert.False(result.AllFailed);
    }

    [Fact]
    public async Task Everything_already_converted_is_a_quiet_non_failure()
    {
        var result = await Each.RunAsync([@"C:\in\a.png"], Ctx, Echo(), CancellationToken.None,
            skipExtensions: new HashSet<string> { "png" }, alreadyLabel: "PNG");
        Assert.Empty(result.Outputs);
        Assert.Empty(result.Failures);
        Assert.False(result.AllFailed);
        Assert.Equal("1 already PNG", result.ToastText);
    }

    [Fact]
    public async Task Cancellation_between_files_keeps_earlier_outputs_and_stops()
    {
        using var cts = new CancellationTokenSource();
        var attempted = new List<string>();
        var result = await Each.RunAsync(Inputs(5), Ctx, async (input, ct) =>
        {
            attempted.Add(Path.GetFileName(input));
            if (attempted.Count == 2) await cts.CancelAsync();
            return Path.ChangeExtension(input, "out");
        }, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Outputs.Count);
        Assert.Equal(2, attempted.Count);
        Assert.False(result.AllFailed);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_cancelled_dialog_stops_the_batch_but_keeps_files_already_saved()
    {
        var interaction = new FakeInteraction(crop: null); // user presses Cancel in the crop editor
        var ctx = new ActionContext(Capabilities.All, interaction);
        var calls = 0;
        var action = ActionDef.PerFile("Crop", WheelPage.Tool, async (input, c, ct) =>
        {
            calls++;
            if (calls == 2) await c.RequireCropAsync(input, ct); // second file: user cancels
            return Path.ChangeExtension(input, "cropped");
        }, interactive: true);

        var result = await action.Run(Inputs(4), ctx, CancellationToken.None);

        Assert.True(result.Cancelled);
        Assert.Single(result.Outputs);
        Assert.Equal(2, calls);
        Assert.Equal(1, interaction.CropRequests);
    }

    [Fact]
    public async Task Output_in_a_different_directory_is_flagged_as_downloads_fallback()
    {
        var result = await Each.RunAsync([@"C:\in\a.png", @"C:\in\b.png"], Ctx, (input, _) =>
            Task.FromResult(input.EndsWith("a.png") ? @"C:\in\a.jpg" : @"C:\Users\me\Downloads\b.jpg"), CancellationToken.None);

        Assert.False(result.Outputs[0].UsedDownloadsFallback);
        Assert.True(result.Outputs[1].UsedDownloadsFallback);
        Assert.True(result.AnyDownloadsFallback);
        Assert.Equal("2 files saved · in Downloads", result.ToastText);
    }

    [Fact]
    public async Task Folder_outputs_next_to_the_input_are_not_fallbacks()
    {
        var result = await Each.RunAsync([@"C:\in\doc.pdf"], Ctx,
            (_, _) => Task.FromResult(@"C:\in\doc pages"), CancellationToken.None);
        Assert.False(result.AnyDownloadsFallback);
        Assert.Equal("doc pages", result.ToastText);
    }

    [Fact]
    public async Task Progress_reports_each_file_name()
    {
        var seen = new List<string>();
        var ctx = new ActionContext(Capabilities.All, progress: new SyncProgress(seen.Add));
        await Each.RunAsync(Inputs(2), ctx, Echo(), CancellationToken.None);
        Assert.Equal(["file1.png", "file2.png"], seen);
    }

    [Fact]
    public async Task PerFile_action_skips_hideFor_extensions_and_labels_them_with_its_title()
    {
        var action = ActionDef.PerFile("PNG", WheelPage.Format,
            (input, _, _) => Task.FromResult(Path.ChangeExtension(input, "png")), hideFor: ["png"]);
        var result = await action.Run([@"C:\in\a.png", @"C:\in\b.gif"], new ActionContext(Capabilities.All), CancellationToken.None);
        Assert.Equal(1, result.AlreadyInFormat);
        Assert.Equal("PNG", result.AlreadyLabel);
        Assert.Single(result.Outputs);
    }

    [Fact]
    public async Task SingleOutput_action_maps_failures_and_cancellation_to_results()
    {
        var ok = ActionDef.SingleOutput("Merge", WheelPage.Tool, (p, _, _) => Task.FromResult(@"C:\in\Merged.pdf"));
        var fail = ActionDef.SingleOutput("Merge", WheelPage.Tool, (_, _, _) => throw new ConversionException("encrypted"));
        var cancel = ActionDef.SingleOutput("Merge", WheelPage.Tool, (_, _, _) => throw new OperationCanceledException());
        string[] two = [@"C:\in\a.pdf", @"C:\in\b.pdf"];
        var ctx = new ActionContext(Capabilities.All);

        var r1 = await ok.Run(two, ctx, CancellationToken.None);
        Assert.Equal(@"C:\in\Merged.pdf", Assert.Single(r1.Outputs).Path);
        Assert.Equal(@"C:\in\a.pdf", r1.Outputs[0].Input);

        var r2 = await fail.Run(two, ctx, CancellationToken.None);
        Assert.Equal("encrypted", r2.AllFailedMessage?.Replace("None of the 2 files could be converted: ", ""));

        var r3 = await cancel.Run(two, ctx, CancellationToken.None);
        Assert.True(r3.Cancelled);
    }

    private sealed class SyncProgress(Action<string> onReport) : IProgress<string>
    {
        public void Report(string value) => onReport(value);
    }
}
