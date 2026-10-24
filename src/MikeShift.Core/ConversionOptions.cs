using System.Globalization;

namespace MikeShift.Core;

/// <summary>How hard the picture Compress tool squeezes (Settings, <c>--compress</c>).</summary>
public enum CompressStrength
{
    /// <summary>Keeps more detail: long side at most 4000 px, quality 0.75.</summary>
    Light,

    /// <summary>The default and what MikeShift always did: long side at most 3000 px, quality 0.6.</summary>
    Medium,

    /// <summary>Smallest files: long side at most 2000 px, quality 0.45.</summary>
    Strong,
}

/// <summary>
/// The few conversion settings a user can change: picture quality, how hard Compress squeezes, the Resize percentage, how
/// long a video GIF may run and where converted files are put. Immutable and always valid: every property rejects a value outside its range
/// (<see cref="ArgumentOutOfRangeException"/> / <see cref="ArgumentException"/>), so a converter never has to re-check. <see cref="Default"/> is
/// exactly what MikeShift did before the settings existed, so nothing changes for anyone who never opens them.
/// <para>Carried by <see cref="Actions.ActionContext.Options"/>; the wheel, the Explorer menu and the command line all build one the same way
/// (<c>Preferences.Conversions</c> in the app, the <c>--quality</c>... flags in <see cref="Cli.CliRunner"/>).</para>
/// </summary>
public sealed record ConversionOptions
{
    /// <summary>Lowest JPEG/WebP/HEIC quality that can be chosen; below this pictures fall apart.</summary>
    public const int MinQuality = 50;

    /// <summary>Highest quality (not lossless: JPEG and WebP are lossy at 100 too).</summary>
    public const int MaxQuality = 100;

    /// <summary>The quality picture conversions always used.</summary>
    public const int DefaultQuality = 90;

    /// <summary>The Resize percentages offered.</summary>
    public static IReadOnlyList<int> ResizeChoices { get; } = [25, 50, 75];

    /// <summary>The Resize tool's original (and default) percentage.</summary>
    public const int DefaultResizePercent = 50;

    /// <summary>How long a GIF made from a video may run, in seconds.</summary>
    public static IReadOnlyList<int> GifSecondsChoices { get; } = [5, 10, 15, 30];

    /// <summary>The GIF length that was always used.</summary>
    public const int DefaultGifSeconds = 15;

    /// <summary>The settings before anyone changed anything.</summary>
    public static ConversionOptions Default { get; } = new();

    private readonly int _quality = DefaultQuality;
    private readonly CompressStrength _compress = CompressStrength.Medium;
    private readonly int _resizePercent = DefaultResizePercent;
    private readonly int _gifSeconds = DefaultGifSeconds;
    private readonly string? _outputFolder;

    /// <summary>JPEG, WebP and HEIC quality, <see cref="MinQuality"/> to <see cref="MaxQuality"/> (default 90). PNG, TIFF, BMP and GIF are lossless or palette formats and ignore it.</summary>
    public int Quality
    {
        get => _quality;
        init => _quality = value is >= MinQuality and <= MaxQuality
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Quality), value, $"Quality must be {MinQuality} to {MaxQuality}.");
    }

    /// <summary>How hard Compress squeezes a picture (default <see cref="CompressStrength.Medium"/>).</summary>
    public CompressStrength Compress
    {
        get => _compress;
        init => _compress = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Compress), value, "Unknown compress strength.");
    }

    /// <summary>The percentage the Resize tool scales a picture to: 25, 50 or 75 (default 50).</summary>
    public int ResizePercent
    {
        get => _resizePercent;
        init => _resizePercent = ResizeChoices.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(ResizePercent), value, "Resize must be 25, 50 or 75 percent.");
    }

    /// <summary>The longest a GIF made from a video may run: 5, 10, 15 or 30 seconds (default 15).</summary>
    public int GifSeconds
    {
        get => _gifSeconds;
        init => _gifSeconds = GifSecondsChoices.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(GifSeconds), value, "A GIF can run 5, 10, 15 or 30 seconds.");
    }

    /// <summary>
    /// A folder to put every converted file in, as a full path; null (the default) puts each result next to its original. A folder that
    /// does not exist or cannot be written to falls back to Downloads for that run, exactly as an unwritable original folder does.
    /// </summary>
    public string? OutputFolder
    {
        get => _outputFolder;
        init => _outputFolder = Normalize(value);
    }

    // ------------------------------------------------------------ derived values the converters read

    /// <summary><see cref="Quality"/> as the 0 to 1 number the encoders take (90 gives 0.9).</summary>
    public double QualityFraction => _quality / 100.0;

    /// <summary>The longest side, in pixels, Compress allows: 4000, 3000 or 2000.</summary>
    public int CompressMaxLongSide => MaxLongSideFor(_compress);

    /// <summary>The JPEG/WebP/HEIC quality (0 to 1) Compress encodes at: 0.75, 0.6 or 0.45.</summary>
    public double CompressQuality => QualityFor(_compress);

    /// <summary>Size and quality step of a Compress strength.</summary>
    public static int MaxLongSideFor(CompressStrength strength) => strength switch
    {
        CompressStrength.Light => 4000,
        CompressStrength.Strong => 2000,
        _ => 3000,
    };

    /// <summary>The Compress encoder quality (0 to 1) of a strength.</summary>
    public static double QualityFor(CompressStrength strength) => strength switch
    {
        CompressStrength.Light => 0.75,
        CompressStrength.Strong => 0.45,
        _ => 0.6,
    };

    /// <summary>The tool's title for a Resize percentage: "Resize 50%".</summary>
    public static string ResizeTitle(int percent) => $"Resize {percent}%";

    // ------------------------------------------------------------ parsing (CLI flags and stored preferences)

    /// <summary>Reads a quality such as "85". Only whole numbers in range count.</summary>
    public static bool TryParseQuality(string? text, out int quality) =>
        TryParseInt(text, out quality) && quality is >= MinQuality and <= MaxQuality;

    /// <summary>Reads "light", "medium" or "strong" (any case).</summary>
    public static bool TryParseCompress(string? text, out CompressStrength strength)
    {
        // Exact names only: Enum.TryParse would also take "1" and "light,strong".
        switch (text?.Trim().ToLowerInvariant())
        {
            case "light": strength = CompressStrength.Light; return true;
            case "medium": strength = CompressStrength.Medium; return true;
            case "strong": strength = CompressStrength.Strong; return true;
            default: strength = default; return false;
        }
    }

    /// <summary>Reads 25, 50 or 75, with or without a trailing percent sign.</summary>
    public static bool TryParseResizePercent(string? text, out int percent)
    {
        percent = 0;
        var t = text?.Trim();
        if (t is { Length: > 1 } && t[^1] == '%') t = t[..^1];
        return TryParseInt(t, out percent) && ResizeChoices.Contains(percent);
    }

    /// <summary>Reads 5, 10, 15 or 30.</summary>
    public static bool TryParseGifSeconds(string? text, out int seconds) =>
        TryParseInt(text, out seconds) && GifSecondsChoices.Contains(seconds);

    /// <summary>
    /// Checks a folder given by a person (CLI <c>--out</c>, a stored setting): a full path of an existing folder. Returns the normalised path,
    /// or null with a reason in <paramref name="error"/>.
    /// </summary>
    public static string? ValidateOutputFolder(string? text, out string? error)
    {
        error = null;
        try
        {
            var normalized = Normalize(text);
            if (normalized is null) { error = "the folder name is empty"; return null; }
            if (File.Exists(normalized)) { error = $"\"{normalized}\" is a file, not a folder"; return null; }
            if (!Directory.Exists(normalized)) { error = $"the folder \"{normalized}\" does not exist"; return null; }
            return normalized;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// The options a set of stored strings describe, each one falling back to its default when it is missing or unreadable: an older or
    /// hand-edited settings file never breaks conversions. <paramref name="outputFolder"/> is taken as is (it is checked when a file is written).
    /// </summary>
    public static ConversionOptions FromStored(string? quality, string? compress, string? resize, string? gifSeconds, string? outputFolder)
    {
        string? folder;
        try { folder = Normalize(outputFolder); }
        catch (ArgumentException) { folder = null; }
        return new ConversionOptions
        {
            Quality = TryParseQuality(quality, out var q) ? q : DefaultQuality,
            Compress = TryParseCompress(compress, out var c) ? c : CompressStrength.Medium,
            ResizePercent = TryParseResizePercent(resize, out var r) ? r : DefaultResizePercent,
            GifSeconds = TryParseGifSeconds(gifSeconds, out var g) ? g : DefaultGifSeconds,
            OutputFolder = folder,
        };
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        return text is not null && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Null/blank means "next to the original"; anything else must be a full path.</summary>
    private static string? Normalize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var trimmed = folder.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
            throw new ArgumentException("The output folder must be a full path.", nameof(OutputFolder));
        var full = Path.GetFullPath(trimmed);
        var root = Path.GetPathRoot(full);
        return root is not null && full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
}
