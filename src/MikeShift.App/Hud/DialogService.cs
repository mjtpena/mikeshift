using MikeShift.Core.Feedback;

namespace MikeShift.App.Hud;

/// <summary>Failure and warning dialogs. Never blocks the caller.</summary>
public interface IDialogService
{
    /// <summary>Shows <paramref name="content"/> in a Fluent dialog window and returns at once. UI thread only.</summary>
    void Show(DialogContent content);
}

/// <summary>
/// Shows <see cref="NoticeWindow"/>s (UX review D-01): themed, non-modal, topmost, so a failure never holds up the toasts and chimes
/// of the jobs that are still running. Button commands (Show in folder, Quit MikeShift) are handled here.
/// <para>
/// At most one failure dialog is open at a time: while it is, the failures of later jobs are merged into it ("3 jobs had problems", see
/// <see cref="DialogMerge"/>) instead of stacking another topmost window each.
/// </para>
/// </summary>
public sealed class FluentDialogService : IDialogService
{
    /// <summary>Failure notices kept for the merged dialog; older ones are dropped (the job count still includes them).</summary>
    private const int MaxMergedNotices = 50;

    // Weak, so a closed dialog is not kept alive for the life of the app (it holds a whole XAML window).
    private WeakReference<NoticeWindow>? _last;

    private NoticeWindow? _failureWindow;
    private readonly List<DialogContent> _failures = [];
    private int _failedJobs;

    /// <summary>The most recent dialog if it still exists (tests and the capture harness).</summary>
    internal NoticeWindow? Last => _last is not null && _last.TryGetTarget(out var window) ? window : null;

    public void Show(DialogContent content)
    {
        try
        {
            if (content.JobProblem && _failureWindow is { IsOpen: true } open)
            {
                _failedJobs++;
                _failures.Add(content);
                if (_failures.Count > MaxMergedNotices) _failures.RemoveAt(0);
                open.Update(DialogMerge.Combine(_failures, _failedJobs));
                return;
            }

            var window = new NoticeWindow(content, Execute);
            _last = new WeakReference<NoticeWindow>(window);
            if (content.JobProblem)
            {
                _failureWindow = window;
                _failures.Clear();
                _failures.Add(content);
                _failedJobs = 1;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_failureWindow, window)) _failureWindow = null;
                };
            }
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            // A dialog is never worth a crash; the log has the reason.
            Wheel.WheelLog.Error("dialog failed", ex);
        }
    }

    private static void Execute(DialogButton button)
    {
        switch (button.Command)
        {
            case DialogCommand.ShowInFolder when button.Path is { } path:
                ShellReveal.ShowInFolder(path);
                break;
            case DialogCommand.QuitApp:
                App.Current.Quit();
                break;
        }
    }
}
