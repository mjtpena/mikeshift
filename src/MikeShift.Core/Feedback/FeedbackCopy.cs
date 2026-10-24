using System.Text.RegularExpressions;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Feedback;

/// <summary>
/// The words MikeShift uses outside the wheel (UX review "Proposed copy", X-01): sentence case, gerunds for progress, "couldn't"
/// for failures. One place so the toast, dialogs and the tests agree.
/// </summary>
public static partial class FeedbackCopy
{
    /// <summary>The product name.</summary>
    public const string AppName = "MikeShift";

    /// <summary>Tray tooltip (T-04).</summary>
    public const string TrayTooltip = "MikeShift - Shift-drag files to convert";

    /// <summary>Tray tooltip for the chosen arming key: "MikeShift - Ctrl-drag files to convert".</summary>
    public static string TrayTooltipFor(Interaction.ArmKey key) => $"{AppName} - {Interaction.ToolsKeys.Label(key)}-drag files to convert";

    /// <summary>The one-line gesture hint in the tray menu header: "Shift-drag files to convert".</summary>
    public static string TrayHintFor(Interaction.ArmKey key) => $"{Interaction.ToolsKeys.Label(key)}-drag files to convert";

    /// <summary>The reassurance every onboarding surface repeats.</summary>
    public const string OriginalsSafe = "Your originals are never moved or changed. Press Esc to cancel.";

    /// <summary>Toast when the hotkey is pressed without anything selected in File Explorer.</summary>
    public const string SelectFilesFirst = "Select files in File Explorer first";

    /// <summary>Toast: the files were all in the target format already.</summary>
    public const string AlreadyThatFormat = "These files are already in that format";

    /// <summary>Click mode when no tile applies (D-04).</summary>
    public const string NothingAvailable = "MikeShift can't convert these files. You can still zip them.";

    /// <summary>The hint appended to a reason about a damaged file.</summary>
    public const string TryAnotherApp = "Try opening it in another app.";

    /// <summary>Elevation warning title (D-03).</summary>
    public const string ElevatedTitle = "MikeShift is running as administrator";

    /// <summary>Elevation warning body (D-03).</summary>
    public const string ElevatedBody =
        "Windows blocks drag and drop between normal and administrator apps, so the wheel won't appear. " +
        "Quit MikeShift and start it again without \"Run as administrator\".";

    /// <summary>"Converting to PNG…" / "Compressing…": the toast title while a job runs.</summary>
    public static string StartTitle(ActionDef action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Page == WheelPage.Format) return $"Converting to {action.Title}…";
        return ToolGerund(action.Title, withObject: true) + "…";
    }

    /// <summary>"Converting 2 of 5…" / "Compressing 2 of 5…": per-file progress of a batch.</summary>
    public static string ProgressTitle(ActionDef action, int done, int total)
    {
        ArgumentNullException.ThrowIfNull(action);
        var verb = action.Page == WheelPage.Format ? "Converting" : ToolGerund(action.Title, withObject: false);
        return $"{verb} {Math.Min(Math.Max(done, 1), Math.Max(total, 1))} of {total}…";
    }

    /// <summary>"2 more in progress" (the second line while other jobs run).</summary>
    public static string MoreInProgress(int others) => $"{others} more in progress";

    private static string ToolGerund(string title, bool withObject) => title switch
    {
        "Compress" => "Compressing",
        _ when ActionCatalog.ResizePercentIn(title) is { } percent => withObject ? $"Resizing to {percent}%" : "Resizing",
        "Crop" => "Cropping",
        "Strip EXIF" => withObject ? "Removing EXIF data" : "Cleaning",
        "Trim" => "Trimming",
        "Split" => "Splitting",
        "Merge" or "Merge PDF" => "Merging",
        "Zip" => "Zipping",
        _ => title,
    };

    /// <summary>
    /// Shortens the long, sentence-style reasons converters produce for the failure list ("This image is too large (300 megapixels).
    /// MikeShift handles up to 200 megapixels." becomes "Too large (300 megapixels; the limit is 200)."). Anything else is unchanged.
    /// </summary>
    public static string ShortReason(string reason)
    {
        var m = TooLarge().Match(reason);
        return m.Success ? $"Too large ({m.Groups[1].Value} megapixels; the limit is {m.Groups[2].Value})." : reason;
    }

    /// <summary>Appends the "Try opening it in another app." hint to a reason about a damaged file.</summary>
    public static string WithHint(string reason) =>
        reason.Contains("damaged", StringComparison.OrdinalIgnoreCase) && !reason.Contains("another app", StringComparison.OrdinalIgnoreCase)
            ? reason.TrimEnd() + " " + TryAnotherApp
            : reason;

    [GeneratedRegex(@"too large \((\d+) megapixels\)\. MikeShift handles up to (\d+) megapixels", RegexOptions.IgnoreCase)]
    private static partial Regex TooLarge();
}
