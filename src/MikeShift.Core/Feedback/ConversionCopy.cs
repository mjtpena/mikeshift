namespace MikeShift.Core.Feedback;

/// <summary>
/// The words of the Settings "Conversions" group, in one place (like <see cref="OnboardingCopy"/>) so the window only wires controls and the
/// wording is testable: what each choice does, in Windows sentence case, with the numbers taken from <see cref="ConversionOptions"/> so the text
/// can never disagree with what the converters do.
/// </summary>
public static class ConversionCopy
{
    /// <summary>The value beside the quality slider: "90 (default)" or "75".</summary>
    public static string QualityValue(int quality) =>
        quality == ConversionOptions.DefaultQuality ? $"{quality} (default)" : quality.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The Compress strength card's description for the chosen strength: what it does to the picture.</summary>
    public static string CompressDescription(CompressStrength strength) => strength switch
    {
        CompressStrength.Light =>
            $"Keeps more detail. Pictures are made at most {ConversionOptions.MaxLongSideFor(strength):N0} pixels on the longest side and saved at higher quality.",
        CompressStrength.Strong =>
            $"Makes the smallest files. Pictures are made at most {ConversionOptions.MaxLongSideFor(strength):N0} pixels on the longest side and saved at lower quality.",
        _ =>
            $"A balance of size and detail, and what MikeShift has always done. Pictures are made at most {ConversionOptions.MaxLongSideFor(strength):N0} pixels on the longest side.",
    };

    /// <summary>The Resize card's description: the tool saves a copy at this share, and where to find it by name.</summary>
    public static string ResizeDescription(int percent) =>
        $"The Resize tool saves a copy at {percent}% of the original width and height. It is called \"{ConversionOptions.ResizeTitle(percent)}\" in the wheel and the right-click menu.";

    /// <summary>The summary line under "Where converted files go".</summary>
    public static string OutputSummary(string? folder) => folder ?? "Next to the original";

    /// <summary>The explanation under the choices of "Where converted files go".</summary>
    public static string OutputHint(string? folder) => folder is null
        ? "Each converted file is saved in the same folder as the file it came from."
        : "Converted files are saved in the folder you chose. If it can't be used, they go to Downloads instead and the message says so.";

    /// <summary>The warning when the chosen folder is missing or cannot be written to.</summary>
    public static string FolderUnavailable(string folder) =>
        $"\"{folder}\" isn't available right now. Converted files will go to Downloads until it is.";

    /// <summary>The description of "Show the file in its folder after converting", which depends on where files go.</summary>
    public static string RevealDescription(string? folder) => folder is null
        ? "Converted files are saved next to the originals. This also opens File Explorer on the result."
        : "Converted files are saved in the folder you chose. This also opens File Explorer on the result.";

    /// <summary>Why a folder the person typed or picked cannot be used for converted files; null if it can.</summary>
    public static string? FolderProblem(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose a folder first.";
        try
        {
            if (!Directory.Exists(folder)) return FolderUnavailable(folder);
            // Creating and removing a marker is the only honest "can I write here" test; the real write goes through Naming.CommitAsync later.
            var probe = Path.Combine(folder, $".mikeshift-write-test-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FolderUnavailable(folder);
        }
    }
}
