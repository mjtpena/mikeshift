namespace Shiftvert.Core.Feedback;

/// <summary>One optional format/codec row in Settings.</summary>
/// <param name="Name">What the user sees, e.g. "HEIC / HEIF output".</param>
/// <param name="Available">Whether this machine supports it right now.</param>
/// <param name="Hint">How to get it when missing (Store extension), or empty.</param>
public sealed record CodecStatus(string Name, bool Available, string Hint);

/// <summary>Turns <see cref="Capabilities"/> into the rows of the Settings → Formats section.</summary>
public static class CodecSummary
{
    /// <summary>Hint shown under the list.</summary>
    public const string StoreHint =
        "Missing formats can usually be enabled with free Microsoft Store extensions: HEIF Image Extensions, " +
        "HEVC Video Extensions, AV1 Video Extension and WebP Image Extensions. Restart Shiftvert after installing one.";

    /// <summary>The rows, in display order.</summary>
    public static IReadOnlyList<CodecStatus> From(Capabilities c) =>
    [
        new("HEIC / HEIF output", c.HeifEncode, "Install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store."),
        new("WebP output", c.WebpEncode, "Install \"WebP Image Extensions\" from the Microsoft Store."),
        new("AVIF input", c.AvifDecode, "Install \"AV1 Video Extension\" from the Microsoft Store."),
        new("H.264 video output", c.H264Encode, "Not available on this edition of Windows."),
        new("HEVC (H.265) video input", c.HevcDecode, "Install \"HEVC Video Extensions\" from the Microsoft Store."),
        new("VP9 video input", c.Vp9Decode, "Install \"VP9 Video Extensions\" from the Microsoft Store."),
        new("AV1 video input", c.Av1Decode, "Install \"AV1 Video Extension\" from the Microsoft Store."),
        new("WebM files", c.WebmOpen, "Install \"Web Media Extensions\" from the Microsoft Store."),
        new("AAC audio output", c.AacEncode, "Not available on this edition of Windows."),
        new("MP3 audio output", c.Mp3Encode, "Not available on this edition of Windows."),
        new("FLAC audio output", c.FlacEncode, "Requires Windows 10 1703 or later."),
        new("Word documents (.doc, best-fidelity PDF)", c.WordCom, "Install Microsoft Word to enable."),
    ];
}
