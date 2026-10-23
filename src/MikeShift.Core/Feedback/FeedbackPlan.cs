using MikeShift.Core.Actions;

namespace MikeShift.Core.Feedback;

/// <summary>
/// What the App should tell the user when a job finishes: an optional HUD toast, an optional dialog, and whether to play the chime.
/// Pure so it can be unit-tested without WinUI (PLAN §1.1 feedback, §3.3; wording per the UX review's "Proposed copy").
/// </summary>
/// <param name="Toast">Content for the HUD toast, or null to show none (and hide the "working" toast).</param>
/// <param name="Dialog">Failure dialog, or null.</param>
/// <param name="Chime">Play the chime.</param>
public sealed record FeedbackPlan(ToastContent? Toast, DialogContent? Dialog, bool Chime)
{
    /// <summary>
    /// Builds the plan for a finished job.
    /// </summary>
    /// <param name="result">The action's outcome.</param>
    /// <param name="othersRunning">How many OTHER jobs are still running (adds "N more in progress").</param>
    /// <param name="chimeEnabled">The <c>chime</c> preference.</param>
    /// <param name="toastEnabled">The <c>showsToast</c> preference.</param>
    public static FeedbackPlan For(ActionResult result, int othersRunning, bool chimeEnabled, bool toastEnabled)
    {
        ArgumentNullException.ThrowIfNull(result);

        var dialog = DialogFor(result);

        ToastContent? toast = null;
        if (toastEnabled && !result.AllFailed)
            toast = ToastFor(result, othersRunning);

        // Chime = success: something was written and nothing failed or was cancelled.
        var chime = chimeEnabled && result.Outputs.Count > 0 && result.Succeeded;
        return new FeedbackPlan(toast, dialog, chime);
    }

    /// <summary>The toast shown the moment a non-interactive job starts: "Converting to PNG…", "Compressing…".</summary>
    public static ToastContent StartToast(ActionDef action, int othersRunning)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ToastContent(FeedbackCopy.StartTitle(action), Others(othersRunning), ToastKind.Progress);
    }

    /// <summary>The toast for a batch in progress: "Converting 2 of 5…" with the file name underneath.</summary>
    public static ToastContent ProgressToast(ActionDef action, string currentFile, int done, int total, int othersRunning)
    {
        ArgumentNullException.ThrowIfNull(action);
        var detail = string.Join(" · ", new[] { currentFile, Others(othersRunning) }.Where(s => !string.IsNullOrEmpty(s)));
        return new ToastContent(FeedbackCopy.ProgressTitle(action, done, total), detail.Length == 0 ? null : detail, ToastKind.Progress);
    }

    /// <summary>The toast when a job ended with nothing to show but others still run.</summary>
    public static ToastContent StillWorkingToast(int othersRunning) =>
        new("Still working…", FeedbackCopy.MoreInProgress(othersRunning), ToastKind.Progress);

    private static string? Others(int othersRunning) => othersRunning > 0 ? FeedbackCopy.MoreInProgress(othersRunning) : null;

    /// <summary>
    /// The toast for a result: "Saved photo 2.png" over "Next to the original", "Saved 2 of 3" over "1 couldn't be converted",
    /// "These files are already in that format". Carries the first output's path for the "Show in folder" button.
    /// </summary>
    public static ToastContent ToastFor(ActionResult result, int othersRunning = 0)
    {
        ArgumentNullException.ThrowIfNull(result);

        var details = new List<string>();
        string title;
        var kind = ToastKind.Success;
        string? reveal = result.Outputs.Count > 0 ? result.Outputs[0].Path : null;

        if (result.Outputs.Count > 0 && result.Failures.Count == 0)
        {
            title = result.Outputs.Count == 1
                ? "Saved " + System.IO.Path.GetFileName(result.Outputs[0].Path.TrimEnd('\\', '/'))
                : $"Saved {result.Outputs.Count} files";
            details.Add(LocationText(result));
        }
        else if (result.Outputs.Count > 0)
        {
            title = $"Saved {result.Outputs.Count} of {Math.Max(result.Total, result.Outputs.Count + result.Failures.Count)}";
            details.Add(result.Failures.Count == 1 ? "1 couldn't be converted" : $"{result.Failures.Count} couldn't be converted");
            kind = ToastKind.Warning;
        }
        else if (result.Failures.Count > 0)
        {
            title = AllFailedTitle(result);
            kind = ToastKind.Warning;
        }
        else if (result.Cancelled)
        {
            title = "Cancelled";
            kind = ToastKind.Info;
        }
        else if (result.AlreadyInFormat > 0)
        {
            title = AlreadyTitle(result);
            kind = ToastKind.Info;
        }
        else
        {
            title = "Nothing to convert";
            kind = ToastKind.Info;
        }

        if (result.Outputs.Count > 0 && result.AlreadyInFormat > 0)
            details.Add($"{result.AlreadyInFormat} already {result.AlreadyLabel ?? "converted"}");
        if (result.Outputs.Count > 0 && result.AnyDownloadsFallback && kind == ToastKind.Success) kind = ToastKind.Warning;
        if (othersRunning > 0) details.Add(FeedbackCopy.MoreInProgress(othersRunning));

        var detail = string.Join(" · ", details.Where(d => d.Length > 0));
        return new ToastContent(title, detail.Length == 0 ? null : detail, kind, reveal);
    }

    private static string AlreadyTitle(ActionResult r) =>
        r.AlreadyLabel is null
            ? FeedbackCopy.AlreadyThatFormat
            : r.AlreadyInFormat == 1 ? $"This file is already {r.AlreadyLabel}" : $"These files are already {r.AlreadyLabel}";

    /// <summary>"Next to the original(s)" or the Downloads explanation.</summary>
    private static string LocationText(ActionResult r)
    {
        var plural = r.Outputs.Count > 1;
        if (!r.AnyDownloadsFallback) return plural ? "Next to the originals" : "Next to the original";
        var all = r.Outputs.All(o => o.UsedDownloadsFallback);
        return all ? "In Downloads (that folder isn't writable)" : "Some are in Downloads (that folder isn't writable)";
    }

    private static string AllFailedTitle(ActionResult r) =>
        r.Total <= 1
            ? $"Couldn't convert {System.IO.Path.GetFileName(r.Failures[0].Input)}"
            : $"None of the {r.Total} files could be converted";

    /// <summary>
    /// The dialog for failures. All inputs failed: "Couldn't convert corrupt.pdf" (one file) or "None of the 3 files could be
    /// converted" with a list. Partial: "2 of 3 files converted" with up to <see cref="ActionResult.MaxListedFailures"/> failure
    /// rows and a "Show in folder" button. Null if nothing failed.
    /// </summary>
    public static DialogContent? DialogFor(ActionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return BuildDialog(result) is { } dialog ? dialog with { JobProblem = true } : null;
    }

    private static DialogContent? BuildDialog(ActionResult result)
    {

        if (result.AllFailed)
        {
            var title = AllFailedTitle(result);
            if (result.Total <= 1)
                return new DialogContent(title, FeedbackCopy.WithHint(FeedbackCopy.ShortReason(result.Failures[0].Message)),
                    [], null, DialogSeverity.Error, [new DialogButton("Close", DialogCommand.Close)]);

            return new DialogContent(title, null, ItemsFor(result), null, DialogSeverity.Error,
                [new DialogButton("Close", DialogCommand.Close)]);
        }

        if (result.PartialFailure)
        {
            var total = Math.Max(result.Total, result.Outputs.Count + result.Failures.Count);
            return new DialogContent(
                $"{result.Outputs.Count} of {total} files converted",
                null,
                ItemsFor(result),
                PartialFooter(result),
                DialogSeverity.Warning,
                [new DialogButton("Show in folder", DialogCommand.ShowInFolder, result.Outputs[0].Path), new DialogButton("Close", DialogCommand.Close)]);
        }

        return null;
    }

    private static List<DialogItem> ItemsFor(ActionResult result)
    {
        var items = result.Failures.Take(ActionResult.MaxListedFailures)
            .Select(f => new DialogItem(System.IO.Path.GetFileName(f.Input), FeedbackCopy.ShortReason(f.Message)))
            .ToList();
        if (result.Failures.Count > ActionResult.MaxListedFailures)
            items.Add(new DialogItem($"…and {result.Failures.Count - ActionResult.MaxListedFailures} more", null));
        return items;
    }

    private static string PartialFooter(ActionResult r)
    {
        var n = r.Outputs.Count;
        if (r.AnyDownloadsFallback)
            return r.Outputs.All(o => o.UsedDownloadsFallback)
                ? (n == 1 ? "The converted file is in Downloads." : $"The {n} converted files are in Downloads.")
                : "Some converted files are in Downloads because their folder isn't writable. The rest are next to the originals.";
        return n == 1 ? "The converted file is next to the original." : $"The {n} converted files are next to the originals.";
    }
}
