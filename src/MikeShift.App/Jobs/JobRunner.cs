using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using MikeShift.App.Hud;
using MikeShift.App.Settings;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;

namespace MikeShift.App.Jobs;

/// <summary>Raised on the UI thread when a job finished.</summary>
/// <param name="JobId">The id returned by <see cref="JobRunner.Enqueue"/>.</param>
/// <param name="Action">What ran.</param>
/// <param name="Result">The outcome (cancelled / all failed / partial / success).</param>
public sealed record JobFinishedEventArgs(int JobId, ActionDef Action, ActionResult Result);

/// <summary>
/// Runs actions off the UI thread and tells the user how it went (PLAN §3.3): HUD toast, optional chime, a non-blocking dialog
/// for failures. Several jobs may run at once ("N still working…"). Thread-safe; <see cref="Enqueue"/> may be called from
/// any thread. Decisions about WHAT to say live in <see cref="FeedbackPlan"/> (unit-tested in Core).
/// </summary>
public sealed class JobRunner
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Preferences _prefs;
    private readonly IHud _hud;
    private readonly IDialogService _dialogs;
    private readonly ChimePlayer _chime;
    private readonly Func<Capabilities> _capabilities;
    private readonly JobLedger _ledger = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<int, Task> _running = new();

    public JobRunner(
        DispatcherQueue dispatcher, Preferences prefs, IHud hud, IDialogService dialogs, ChimePlayer chime,
        Func<Capabilities>? capabilities = null)
    {
        _dispatcher = dispatcher;
        _prefs = prefs;
        _hud = hud;
        _dialogs = dialogs;
        _chime = chime;
        _capabilities = capabilities ?? (() => Capabilities.Current);
    }

    /// <summary>Raised on the UI thread after every job (after the toast/dialog were issued).</summary>
    public event EventHandler<JobFinishedEventArgs>? JobFinished;

    /// <summary>Number of jobs currently running.</summary>
    public int RunningCount => _ledger.RunningCount;

    /// <summary>
    /// Output paths of the most recent job that wrote anything. W3 uses this for the Shift-click rule
    /// ("previously produced files are excluded from the next selection", PLAN §5.3 item 3).
    /// </summary>
    public IReadOnlyList<string> LastOutputs => _ledger.LastOutputs;

    /// <summary>When that job finished; null if no job has produced output yet.</summary>
    public DateTimeOffset? LastFinishedAt => _ledger.LastFinishedAt;

    /// <summary>
    /// Starts <paramref name="action"/> over <paramref name="paths"/> and returns the job id immediately.
    /// Non-interactive actions toast right away ("Compress…", "Convert to PNG…"); interactive ones (Crop, Trim) toast after
    /// the editor returned. <paramref name="interaction"/> defaults to the App's <see cref="UiInteraction"/> via
    /// <see cref="DefaultInteraction"/>.
    /// </summary>
    public int Enqueue(ActionDef action, IReadOnlyList<string> paths, IInteraction? interaction = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(paths);

        var snapshot = paths.ToArray();
        var id = _ledger.Begin();

        if (!action.Interactive)
            ShowToast(FeedbackPlan.StartToast(action, _ledger.OthersRunning(id)));

        IInteraction inner = interaction ?? DefaultInteraction ?? NoInteraction.Instance;
        var notifying = new NotifyingInteraction(inner, () =>
            _dispatcher.TryEnqueue(() => ShowToast(FeedbackPlan.StartToast(action, _ledger.OthersRunning(id)))));

        var progress = new ToastProgress(this, action, id, snapshot.Length);
        var ct = _shutdown.Token;

        var task = Task.Run(async () =>
        {
            ActionResult result;
            try
            {
                // Read here, not on the UI thread: the first read waits for the startup capability probe.
                var ctx = new ActionContext(_capabilities(), notifying, progress);
                result = await action.Run(snapshot, ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new ActionResult { Total = snapshot.Length, Cancelled = true };
            }
            catch (Exception ex)
            {
                result = ActionResult.Failed(snapshot.Length, snapshot.Length > 0 ? snapshot[0] : "", ex.Message);
            }

            if (!_dispatcher.TryEnqueue(() => Finish(id, action, result)))
                _ledger.Complete(id, result); // dispatcher already shut down (app exiting)
        });
        _running[id] = task;
        _ = task.ContinueWith(_ => _running.TryRemove(id, out Task? _), TaskScheduler.Default);

        return id;
    }

    /// <summary>The interaction used when <see cref="Enqueue"/> is called without one. Set at startup to the <see cref="UiInteraction"/>.</summary>
    public IInteraction? DefaultInteraction { get; set; }

    /// <summary>Cancels running jobs (app exit). Outputs already written are kept.</summary>
    public void Shutdown() => _shutdown.Cancel();

    /// <summary>
    /// Cancels running jobs, then waits (without blocking the caller's thread) up to <paramref name="timeout"/> for them to
    /// unwind, so their <c>finally</c> blocks delete temp files before the process exits.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout)
    {
        Shutdown();
        var pending = _running.Values.ToArray();
        if (pending.Length == 0) return;
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(timeout)).ConfigureAwait(true);
    }

    private void Finish(int id, ActionDef action, ActionResult result)
    {
        _ledger.Complete(id, result);
        var others = _ledger.RunningCount;
        var plan = FeedbackPlan.For(result, others, _prefs.Chime, _prefs.ShowsToast);

        if (plan.Toast is { } toast) _hud.Show(toast);
        else if (others > 0 && _prefs.ShowsToast) _hud.Show(FeedbackPlan.StillWorkingToast(others));
        else _hud.Hide();

        if (plan.Chime) _chime.Play();
        if (_prefs.ShowInFolderAfter && result.Outputs.Count > 0) ShellReveal.ShowInFolder(result.Outputs[0].Path);
        JobFinished?.Invoke(this, new JobFinishedEventArgs(id, action, result));

        // The dialog does not block: later jobs keep their toasts and chimes.
        if (plan.Dialog is { } dialog)
            _dialogs.Show(dialog);
    }

    private void ShowToast(ToastContent content)
    {
        if (_prefs.ShowsToast) _hud.Show(content);
    }

    /// <summary>Turns per-file progress ("photo.heic") into toast updates for multi-file jobs.</summary>
    private sealed class ToastProgress : IProgress<string>
    {
        private readonly JobRunner _owner;
        private readonly ActionDef _action;
        private readonly int _id;
        private readonly int _total;
        private int _seen;

        public ToastProgress(JobRunner owner, ActionDef action, int id, int total)
        {
            _owner = owner;
            _action = action;
            _id = id;
            _total = total;
        }

        public void Report(string value)
        {
            // Single-file jobs keep the start text; batch actions (Merge, Zip) report their own title once;
            // interactive actions toast only after their editor returned (see NotifyingInteraction).
            if (_total <= 1 || _action.Interactive || string.Equals(value, _action.Title, StringComparison.OrdinalIgnoreCase)) return;
            var n = Interlocked.Increment(ref _seen);
            _owner._dispatcher.TryEnqueue(() =>
                _owner.ShowToast(FeedbackPlan.ProgressToast(_action, value, n, _total, _owner._ledger.OthersRunning(_id))));
        }
    }
}
