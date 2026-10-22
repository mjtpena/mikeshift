using Shiftvert.App.Jobs;

namespace Shiftvert.App.Editors;

/// <summary>
/// Plugs the Crop editor and the Trim dialog into the app (PLAN §3.5). Call <see cref="Register"/> once from
/// <c>App.OnLaunched</c>, after the services exist.
/// </summary>
public static class EditorRegistration
{
    /// <summary>Path of the file the dev flags write their result to.</summary>
    public static string DevResultPath => Path.Combine(Path.GetTempPath(), "shiftvert-editor-result.txt");

    /// <summary>
    /// Sets <see cref="UiInteraction.CropEditor"/> and <see cref="UiInteraction.TrimEditor"/>. The handlers are called on a
    /// thread-pool thread (actions run there), so they hop to the UI thread to create the windows, and complete when
    /// the window closes. Cancelling the token closes the window and yields null.
    /// </summary>
    public static void Register(UiInteraction interaction)
    {
        interaction.CropEditor = (path, ct) => interaction.OnUiThreadAsync(() => CropEditorWindow.ShowAsync(path, ct));
        interaction.TrimEditor = (path, ct) => interaction.OnUiThreadAsync(() => TrimDialog.ShowAsync(path, ct));
    }

    /// <summary>
    /// Dev/test hook: <c>--crop &lt;image&gt;</c> or <c>--trim &lt;media&gt;</c> opens that editor standalone. The result is
    /// written to <see cref="DevResultPath"/> and the app quits when the window closes (unless <paramref name="whenDone"/>
    /// says otherwise). Call on the UI thread. Returns true if a flag was found.
    /// </summary>
    public static bool TryHandleDevArgs(string[] args, Action? whenDone = null)
    {
        var crop = ValueAfter(args, "--crop");
        var trim = ValueAfter(args, "--trim");
        if (crop is null && trim is null) return false;

        whenDone ??= () => App.Current.Quit();
        _ = RunDevAsync(crop, trim, whenDone);
        return true;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task RunDevAsync(string? crop, string? trim, Action whenDone)
    {
        string text;
        try
        {
            if (crop is not null)
            {
                var choice = await CropEditorWindow.ShowAsync(crop, CancellationToken.None);
                text = choice is null ? "cancelled" : $"crop {choice.Rect.X},{choice.Rect.Y} {choice.Rect.W}x{choice.Rect.H} -> {choice.OutputWidth}x{choice.OutputHeight}";
            }
            else
            {
                var range = await TrimDialog.ShowAsync(trim!, CancellationToken.None);
                text = range is null ? "cancelled" : $"range {range.Value.Start.TotalSeconds:0.###}-{range.Value.End.TotalSeconds:0.###}";
            }
        }
        catch (Exception ex)
        {
            text = "error " + ex;
        }

        try { File.WriteAllText(DevResultPath, text); }
        catch (IOException) { /* dev aid only */ }
        whenDone();
    }
}
