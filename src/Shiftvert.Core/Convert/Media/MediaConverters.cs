using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Media;

/// <summary>Output formats of the Video category.</summary>
public enum VideoFormat
{
    Mp4,
    M4v,
    /// <summary>Windows-only extra (no mac equivalent).</summary>
    Wmv,
    /// <summary>First 15 s, 10 fps, max side 480, looping.</summary>
    Gif,
}

/// <summary>Output formats of the Audio category (also used to extract audio from video).</summary>
public enum AudioFormat
{
    M4a,
    Wav,
    Aiff,
    Flac,
    Mp3,
}

/// <summary>
/// Video conversions (PLAN §1.2 Video rows). OWNED BY W2c (Media): implement the bodies; you may change signatures
/// inside this file (and the lambdas in <c>Actions/Catalog/VideoActions.cs</c>, which you also own).
/// <para>Every method returns the path it wrote via <c>Naming.CommitAsync</c> and throws
/// <see cref="ConversionException"/> with a user-facing message for expected failures.</para>
/// </summary>
public static class VideoConverter
{
    /// <summary>Re-encode/re-wrap to MP4, M4V, WMV or animated GIF; output <c>name.ext</c>.</summary>
    public static Task<string> ToFormatAsync(string input, VideoFormat format, CancellationToken ct) =>
        throw new NotImplementedException("W2c");

    /// <summary>Extract the audio track (M4A and MP3 are offered); requires an audio stream.</summary>
    public static Task<string> ExtractAudioAsync(string input, AudioFormat format, CancellationToken ct) =>
        throw new NotImplementedException("W2c");

    /// <summary>Compress to 720p MP4, " compressed"; keep only if smaller.</summary>
    public static Task<string> CompressAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2c");

    /// <summary>Trim to <paramref name="range"/>, " trimmed"; stream-copy where possible.</summary>
    public static Task<string> TrimAsync(string input, TimeRange range, CancellationToken ct) =>
        throw new NotImplementedException("W2c");
}

/// <summary>
/// Audio conversions (PLAN §1.2 Audio rows, §3.4 Media). OWNED BY W2c (Media); same rules as <see cref="VideoConverter"/>.
/// </summary>
public static class AudioConverter
{
    /// <summary>Convert to M4A (AAC 44.1 kHz 256 kbps), WAV (PCM 16/24-bit), AIFF (own writer), FLAC or MP3.</summary>
    public static Task<string> ToFormatAsync(string input, AudioFormat format, CancellationToken ct) =>
        throw new NotImplementedException("W2c");

    /// <summary>Compress: AAC 48 kbps x max(2, channels), " compressed"; keep only if smaller.</summary>
    public static Task<string> CompressAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2c");

    /// <summary>Trim to <paramref name="range"/>, " trimmed": lossless cut for WAV/AIFF, otherwise AAC M4A.</summary>
    public static Task<string> TrimAsync(string input, TimeRange range, CancellationToken ct) =>
        throw new NotImplementedException("W2c");
}
