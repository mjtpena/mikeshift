namespace MikeShift.Core.Feedback;

/// <summary>One optional format/codec row in Settings.</summary>
/// <param name="Name">What the user sees, e.g. "HEIC / HEIF output".</param>
/// <param name="Available">Whether this machine supports it right now.</param>
/// <param name="Hint">How to get it when missing (Store extension), or empty.</param>
/// <param name="StoreProductId">Microsoft Store product id of the free extension that adds it, or null (nothing to install).</param>
public sealed record CodecStatus(string Name, bool Available, string Hint, string? StoreProductId = null)
{
    /// <summary>"Available" or "Not installed": the state as text, so it never depends on a colour or glyph.</summary>
    public string StateText => Available ? "Available" : "Not installed";

    /// <summary>What a screen reader reads for the row: "WebM files, not installed".</summary>
    public string AccessibleName => $"{Name}, {(Available ? "available" : "not installed")}";

    /// <summary><c>ms-windows-store://pdp/?ProductId=...</c> for the extension, or null.</summary>
    public string? StoreUri => StoreProductId is null ? null : $"ms-windows-store://pdp/?ProductId={StoreProductId}";
}

/// <summary>Turns <see cref="Capabilities"/> into the rows of the Settings → Format support section.</summary>
public static class CodecSummary
{
    /// <summary>Hint shown under the list.</summary>
    public const string StoreHint = "Some formats need a free extension from the Microsoft Store. After installing one, restart MikeShift.";

    /// <summary>Shown instead of the hint when nothing is missing.</summary>
    public const string AllAvailable = "Everything MikeShift can use is available on this PC.";

    /// <summary>The rows, in display order.</summary>
    public static IReadOnlyList<CodecStatus> From(Capabilities c) =>
    [
        new("HEIC photos (iPhone) input", c.HeifDecode && c.HevcDecode,
            "Install \"HEIF Image Extensions\" (free) and \"HEVC Video Extensions\" from the Microsoft Store.",
            c.HeifDecode ? CodecHelp.HevcProductId : CodecHelp.HeifProductId),
        new("HEIC / HEIF output", c.HeifEncode, "Install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store.", CodecHelp.HeifProductId),
        new("WebP output", c.WebpEncode, "Install \"WebP Image Extensions\" from the Microsoft Store.", CodecHelp.WebpProductId),
        new("AVIF input", c.AvifDecode, "Install \"AV1 Video Extension\" from the Microsoft Store.", CodecHelp.Av1ProductId),
        new(OcrCopy.FormatSupportName, c.Ocr, OcrCopy.FormatSupportHint),
        new("H.264 video output", c.H264Encode, "Not available on this edition of Windows."),
        new("HEVC (H.265) video input", c.HevcDecode, "Install \"HEVC Video Extensions\" from the Microsoft Store.", CodecHelp.HevcProductId),
        new("VP9 video input", c.Vp9Decode, "Install \"VP9 Video Extensions\" from the Microsoft Store.", "9N4D0MSMP0PT"),
        new("AV1 video input", c.Av1Decode, "Install \"AV1 Video Extension\" from the Microsoft Store.", CodecHelp.Av1ProductId),
        new("WebM files", c.WebmOpen, "Install \"Web Media Extensions\" from the Microsoft Store.", "9N5TDP8VCMHS"),
        new("AAC audio output", c.AacEncode, "Not available on this edition of Windows."),
        new("MP3 audio output", c.Mp3Encode, "Not available on this edition of Windows."),
        new("FLAC audio output", c.FlacEncode, "Requires Windows 10 1703 or later."),
        new("Word documents (.doc, best-fidelity PDF)", c.WordInstalled, "Install Microsoft Word to enable. It is only used when \"Use Microsoft Word\" is on."),
    ];

    /// <summary>The summary line of the expander: "2 not installed" or "All available".</summary>
    public static string Headline(IReadOnlyList<CodecStatus> rows)
    {
        var missing = rows.Count(r => !r.Available);
        return missing == 0 ? "All available" : $"{missing} not installed";
    }

    /// <summary>Rows with the missing ones first (what the user came to fix), then the available ones; order otherwise unchanged.</summary>
    public static IReadOnlyList<CodecStatus> MissingFirst(IReadOnlyList<CodecStatus> rows) =>
        [.. rows.Where(r => !r.Available), .. rows.Where(r => r.Available)];

    /// <summary>True when the two snapshots differ in any row, i.e. an extension was installed or removed since the first probe.</summary>
    public static bool Differs(Capabilities before, Capabilities after) =>
        !From(before).Select(r => r.Available).SequenceEqual(From(after).Select(r => r.Available));
}
