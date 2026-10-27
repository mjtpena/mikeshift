using System.Globalization;
using System.Text.RegularExpressions;

namespace MikeShift.Core.Feedback;

/// <summary>
/// The words of the video "Fit 10 MB" and "Fit 25 MB" tools: the tile titles, the name of the file they write, the toast while they run and
/// their refusals. In one place so the wheel, the command line and the tests agree, and so the numbers in a message can never disagree with
/// the converter (<c>VideoConverter.FitAsync</c>).
/// </summary>
public static partial class SizeTargetCopy
{
    /// <summary>The tile title, also the CLI name: "Fit 10 MB".</summary>
    public static string Title(int megabytes) => $"Fit {megabytes.ToString(CultureInfo.InvariantCulture)} MB";

    /// <summary>What the output name gets after the original's: <c>clip.mp4</c> becomes <c>clip 10 MB.mp4</c>.</summary>
    public static string Suffix(int megabytes) => $" {megabytes.ToString(CultureInfo.InvariantCulture)} MB";

    /// <summary>The toast title while it runs: "Shrinking to 10 MB…" (<paramref name="withObject"/>) or "Shrinking" for "Shrinking 2 of 5…".</summary>
    public static string Gerund(int megabytes, bool withObject) =>
        withObject ? $"Shrinking to {megabytes.ToString(CultureInfo.InvariantCulture)} MB" : "Shrinking";

    /// <summary>The screen-reader description of the tile.</summary>
    public static string Describe(int megabytes) =>
        $"Save a copy that is at most {megabytes.ToString(CultureInfo.InvariantCulture)} MB, small enough to attach";

    /// <summary>"Fit 10 MB" gives 10; null for any other title.</summary>
    public static int? MegabytesIn(string title)
    {
        var m = FitTitle().Match(title.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var mb) ? mb : null;
    }

    /// <summary>
    /// The refusal for a video too long to fit at a watchable quality, said before anything is encoded, with roughly how long a video can be
    /// at that size so the person knows how much to trim.
    /// </summary>
    public static string TooLong(int megabytes, TimeSpan longest) =>
        $"This video is too long to fit in {megabytes.ToString(CultureInfo.InvariantCulture)} MB and still be watchable. " +
        $"At that size a video can run about {Length(longest)}. Trim it first, then try again.";

    /// <summary>The refusal when the file does not say how long it is (nothing to work the bitrate out from).</summary>
    public const string UnknownLength = "Windows can't tell how long this video is, so MikeShift can't aim for a size.";

    /// <summary>Both encodes came out over the limit: nothing is saved rather than a file that does not fit.</summary>
    public static string CouldNotFit(int megabytes) =>
        $"MikeShift couldn't get this video under {megabytes.ToString(CultureInfo.InvariantCulture)} MB, so nothing was saved. Trim it, then try again.";

    /// <summary>"4 minutes", "1 minute", "50 seconds": rounded down, so the advice is never too generous.</summary>
    public static string Length(TimeSpan length)
    {
        var minutes = (int)Math.Floor(length.TotalMinutes);
        if (minutes >= 2) return $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes";
        if (minutes == 1) return "1 minute";
        var seconds = (int)Math.Floor(length.TotalSeconds);
        if (seconds >= 10) seconds = seconds / 10 * 10;
        return seconds == 1 ? "1 second" : $"{Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture)} seconds";
    }

    [GeneratedRegex(@"^Fit\s+(\d{1,4})\s*MB$", RegexOptions.IgnoreCase)]
    private static partial Regex FitTitle();
}
