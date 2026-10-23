using MikeShift.App.Hud;
using MikeShift.App.Tray;
using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;

namespace MikeShift.App;

/// <summary>
/// Dev/test hook: <c>MikeShift.exe --scene &lt;name&gt;</c> shows one UI state with canned data (no files are touched) so screenshots and
/// UI Automation checks can be taken of it. Run with <c>MIKESHIFT_NO_SINGLE_INSTANCE=1 MIKESHIFT_NO_HOOKS=1</c> to leave a real running
/// instance and the global input hooks alone, and <c>MIKESHIFT_THEME=light|dark</c> to force the XAML theme.
/// Scenes: <c>toast-progress</c>, <c>toast-progress-batch</c>, <c>toast-success</c>, <c>toast-success-many</c>, <c>toast-downloads</c>,
/// <c>toast-partial</c>, <c>toast-already</c>, <c>toast-long</c>, <c>dialog-single</c>, <c>dialog-partial</c>, <c>dialog-all</c>,
/// <c>dialog-elevated</c>, <c>tray-menu</c>, <c>picker</c>.
/// </summary>
internal static class DevScenes
{
    private static readonly ActionDef Png = new("PNG", WheelPage.Format, (_, _, _) => Task.FromResult(new ActionResult()));
    private static readonly ActionDef Compress = new("Compress", WheelPage.Tool, (_, _, _) => Task.FromResult(new ActionResult()));

    private static ActionOutput Out(string input, string output, bool downloads = false) => new(input, output, downloads);

    /// <summary>Runs the scene named after <c>--scene</c>, if any. UI thread. True when a scene was requested.</summary>
    public static bool TryRun(string[] args, AppServices services, TrayController tray)
    {
        var at = Array.IndexOf(args, "--scene");
        if (at < 0 || at + 1 >= args.Length) return false;
        Run(args[at + 1], services, tray);
        return true;
    }

    private static void Run(string scene, AppServices services, TrayController tray)
    {
        switch (scene)
        {
            case "toast-progress":
                services.Hud.Show(FeedbackPlan.StartToast(Png, 0));
                break;
            case "toast-progress-batch":
                services.Hud.Show(FeedbackPlan.ProgressToast(Png, "IMG_2045.heic", 2, 5, 1));
                break;
            case "toast-success":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult
                {
                    Total = 1,
                    Outputs = [Out(@"C:\Samples\photo.heic", @"C:\Samples\photo 2.png")],
                }));
                break;
            case "toast-success-many":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult
                {
                    Total = 3,
                    Outputs = [.. Enumerable.Range(1, 3).Select(i => Out($@"C:\Samples\{i}.heic", $@"C:\Samples\{i}.png"))],
                }));
                break;
            case "toast-downloads":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult
                {
                    Total = 1,
                    Outputs = [Out(@"C:\Locked\retro-photo.heic", @"C:\Users\me\Downloads\retro-photo.png", downloads: true)],
                }));
                break;
            case "toast-partial":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult
                {
                    Total = 3,
                    Outputs = [Out(@"C:\Samples\a.heic", @"C:\Samples\a.png"), Out(@"C:\Samples\b.heic", @"C:\Samples\b.png")],
                    Failures = [new ActionFailure(@"C:\Samples\c.heic", "This image is damaged.")],
                }));
                break;
            case "toast-already":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult { Total = 2, AlreadyInFormat = 2, AlreadyLabel = "PNG" }));
                break;
            case "toast-long":
                services.Hud.Show(FeedbackPlan.ToastFor(new ActionResult
                {
                    Total = 1,
                    Outputs = [Out(@"C:\Samples\long.heic",
                        @"C:\Samples\Holiday photos 2026 - Lisbon, Porto and the Douro valley (edited, final, really final) 4.png")],
                }));
                break;
            case "dialog-single":
                services.Dialogs.Show(FeedbackPlan.DialogFor(ActionResult.Failed(1, @"C:\Samples\corrupt.pdf", "This PDF is damaged and can't be read."))!);
                break;
            case "dialog-partial":
                services.Dialogs.Show(FeedbackPlan.DialogFor(new ActionResult
                {
                    Total = 3,
                    Outputs = [Out(@"C:\Samples\a.heic", @"C:\Samples\a.png"), Out(@"C:\Samples\b.heic", @"C:\Samples\b.png")],
                    Failures = [new ActionFailure(@"C:\Samples\huge-300mp.png",
                        "This image is too large (300 megapixels). MikeShift handles up to 200 megapixels.")],
                })!);
                break;
            case "dialog-all":
                services.Dialogs.Show(FeedbackPlan.DialogFor(new ActionResult
                {
                    Total = 3,
                    Failures =
                    [
                        new ActionFailure(@"C:\Samples\huge-300mp.png", "This image is too large (300 megapixels). MikeShift handles up to 200 megapixels."),
                        new ActionFailure(@"C:\Samples\corrupt.png", "Couldn't read this image. The file may be damaged or in a format Windows can't open."),
                        new ActionFailure(@"C:\Samples\locked.png", "Couldn't read \"locked.png\" because another program is using it."),
                    ],
                })!);
                break;
            case "dialog-elevated":
                services.Dialogs.Show(new DialogContent(FeedbackCopy.ElevatedTitle, FeedbackCopy.ElevatedBody, [], null, DialogSeverity.Warning,
                    [new DialogButton("Close", DialogCommand.Close), new DialogButton("Quit MikeShift", DialogCommand.QuitApp)]));
                break;
            case "tray-menu":
                // A freshly started process is not the foreground one, and the flyout closes when its window is deactivated: take the
                // foreground with a small off-screen anchor window first (a real right click comes with that right already).
                var anchor = new Microsoft.UI.Xaml.Window { Title = "MikeShift scene anchor" };
                anchor.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-32000, -32000, 120, 80));
                Editors.EditorWindows.ShowInForeground(anchor);
                services.Dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, tray.ShowMenuAtCursor);
                break;
            case "picker":
                tray.ConvertFiles();
                break;
        }
    }
}
