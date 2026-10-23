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
/// </summary>
public sealed class FluentDialogService : IDialogService
{
    /// <summary>The most recent dialog (tests and the capture harness).</summary>
    internal NoticeWindow? Last { get; private set; }

    public void Show(DialogContent content)
    {
        try
        {
            var window = new NoticeWindow(content, Execute);
            Last = window;
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
