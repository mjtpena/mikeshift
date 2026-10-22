using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Feedback;

/// <summary>A modal error dialog the App should show (all-fail or partial failure).</summary>
/// <param name="Title">Window title.</param>
/// <param name="Body">Body text; may span several lines.</param>
public sealed record FailureDialog(string Title, string Body);

/// <summary>
/// What the App should tell the user when a job finishes: an optional HUD toast, an optional modal dialog, and whether to
/// play the chime. Pure so it can be unit-tested without WinUI (PLAN §1.1 feedback, §3.3).
/// </summary>
/// <param name="ToastText">Text for the HUD toast, or null to show none (and hide the "working" toast).</param>
/// <param name="Dialog">Modal dialog, or null.</param>
/// <param name="Chime">Play the chime.</param>
public sealed record FeedbackPlan(string? ToastText, FailureDialog? Dialog, bool Chime)
{
    /// <summary>
    /// Builds the plan for a finished job.
    /// </summary>
    /// <param name="result">The action's outcome.</param>
    /// <param name="othersRunning">How many OTHER jobs are still running (adds "· N still working…").</param>
    /// <param name="chimeEnabled">The <c>chime</c> preference.</param>
    /// <param name="toastEnabled">The <c>showsToast</c> preference.</param>
    public static FeedbackPlan For(ActionResult result, int othersRunning, bool chimeEnabled, bool toastEnabled)
    {
        ArgumentNullException.ThrowIfNull(result);

        var dialog = DialogFor(result);

        string? toast = null;
        if (toastEnabled && !result.AllFailed)
            toast = WithBackgroundJobs(ResultText(result), othersRunning);

        // Chime = success: something was written and nothing failed or was cancelled.
        var chime = chimeEnabled && result.Outputs.Count > 0 && result.Succeeded;
        return new FeedbackPlan(toast, dialog, chime);
    }

    /// <summary>The text shown the moment a non-interactive job starts: "Compress…", "Convert to PNG…".</summary>
    public static string StartText(ActionDef action, int othersRunning)
    {
        ArgumentNullException.ThrowIfNull(action);
        return WithBackgroundJobs(action.DropDescription + "…", othersRunning);
    }

    /// <summary>Appends " · N still working…" when other jobs run in the background.</summary>
    public static string WithBackgroundJobs(string text, int othersRunning) =>
        othersRunning > 0 ? $"{text} · {othersRunning} still working…" : text;

    /// <summary>The toast text for a result; "Nothing to convert" if the job did nothing at all.</summary>
    public static string ResultText(ActionResult result)
    {
        var text = result.ToastText;
        return text.Length > 0 ? text : "Nothing to convert";
    }

    /// <summary>
    /// The modal dialog for failures. All inputs failed: <see cref="ActionResult.AllFailedMessage"/>
    /// ("None of the N files could be converted: ..."). Partial: "N saved, M skipped" and up to
    /// <see cref="ActionResult.MaxListedFailures"/> failure lines. Null if nothing failed.
    /// </summary>
    public static FailureDialog? DialogFor(ActionResult result)
    {
        if (result.AllFailed)
            return new FailureDialog("Shiftvert", result.AllFailedMessage!);

        if (result.PartialFailure)
        {
            var head = $"{result.Outputs.Count} saved, {result.Failures.Count} skipped:";
            return new FailureDialog("Shiftvert", head + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, result.FailureLines));
        }

        return null;
    }
}
