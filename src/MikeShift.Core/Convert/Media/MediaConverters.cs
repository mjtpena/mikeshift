using MikeShift.Core.Actions;
using Windows.Media.MediaProperties;

namespace MikeShift.Core.Media;

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
/// Video conversions. Every method returns the path it wrote via <c>Naming.CommitAsync</c> and
/// throws <see cref="ConversionException"/> with a user-facing message for expected failures.
/// </summary>
/// <remarks>
/// MP4/M4V re-wrap H.264/AAC sources losslessly through <see cref="StreamCopy"/> (source reader -> sink writer) and fall back to
/// a full <c>MediaTranscoder</c> transcode for everything else (HEVC, MPEG-2, WMV...) or if the copy fails. WMV, GIF, Compress
/// and Trim always re-encode. Trim is not a stream copy, because a copy can only cut on key frames and the
/// trim dialog promises frame-accurate ranges (see docs/limitations.md).
/// </remarks>
public static class VideoConverter
{
    private const double ConvertBitsPerPixel = 0.15;
    private const double CompressBitsPerPixel = 0.07;

    /// <summary>Re-encode to MP4, M4V, WMV or animated GIF (first 15 s); output <c>name.ext</c>.</summary>
    public static Task<string> ToFormatAsync(string input, VideoFormat format, CancellationToken ct) =>
        ToFormatAsync(input, format, ConversionOptions.Default, ct);

    /// <summary>As above; a GIF runs at most <paramref name="options"/>'s <see cref="ConversionOptions.GifSeconds"/>.</summary>
    public static async Task<string> ToFormatAsync(string input, VideoFormat format, ConversionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var info = await MediaCommon.RequireVideoAsync(input, ct).ConfigureAwait(false);
        var ext = format switch { VideoFormat.Mp4 => ".mp4", VideoFormat.M4v => ".m4v", VideoFormat.Wmv => ".wmv", _ => ".gif" };

        return await MediaCommon.CommitAsync(input, ext, "", async (temp, token) =>
        {
            switch (format)
            {
                case VideoFormat.Gif:
                    await Task.Run(() => GifExporter.Export(input, temp, options.GifSeconds, token), token).ConfigureAwait(false);
                    break;
                case VideoFormat.Wmv:
                    await MediaTranscode.RunAsync(input, temp, MediaTranscode.Wmv(info, info.Height, ConvertBitsPerPixel), null,
                        "WMV", token, sourceVideoSubtype: info.VideoSubtype).ConfigureAwait(false);
                    break;
                default:
                    // Already H.264/AAC (MOV, MKV, M4V, MP4...): re-wrap without touching the encoded data when possible.
                    if (await Task.Run(() => StreamCopy.TryRewrapToMp4(input, info, temp, token), token).ConfigureAwait(false))
                        break;
                    token.ThrowIfCancellationRequested();
                    await MediaTranscode.RunAsync(input, temp, MediaTranscode.Mp4(info, info.Height, ConvertBitsPerPixel), null,
                        format == VideoFormat.Mp4 ? "MP4" : "M4V", token, sourceVideoSubtype: info.VideoSubtype).ConfigureAwait(false);
                    break;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Extract the audio track (M4A and MP3 are offered); requires an audio stream.</summary>
    public static async Task<string> ExtractAudioAsync(string input, AudioFormat format, CancellationToken ct)
    {
        if (format is not (AudioFormat.M4a or AudioFormat.Mp3))
            throw new ConversionException("Only M4A and MP3 can be extracted from a video.");

        var info = await MediaCommon.ReadInfoAsync(input, ct).ConfigureAwait(false);
        if (!info.HasAudio) throw new ConversionException("This video has no audio.");

        return await MediaCommon.CommitAsync(input, format == AudioFormat.M4a ? ".m4a" : ".mp3", "", (temp, token) =>
            format == AudioFormat.M4a
                ? MediaCommon.EncodeAacAsync(input, temp, info, 256_000, token)
                : MediaCommon.WithTranscodableSource(input, info, token, src => MediaCommon.EncodeMp3Async(src, temp, info, token), flatten16: true), ct).ConfigureAwait(false);
    }

    /// <summary>Compress to 720p MP4, " compressed"; keep only if smaller.</summary>
    public static async Task<string> CompressAsync(string input, CancellationToken ct)
    {
        var info = await MediaCommon.RequireVideoAsync(input, ct).ConfigureAwait(false);
        var originalBytes = new FileInfo(input).Length;

        // Never ask for more than ~60 % of what the source already spends on video.
        uint? cap = null;
        if (info.Duration > TimeSpan.Zero)
        {
            var totalBps = originalBytes * 8.0 / info.Duration.TotalSeconds;
            cap = (uint)Math.Clamp((totalBps - 128_000) * 0.6, 300_000, 25_000_000);
        }

        return await MediaCommon.CommitAsync(input, ".mp4", " compressed", async (temp, token) =>
        {
            await MediaTranscode.RunAsync(input, temp, MediaTranscode.Mp4(info, 720, CompressBitsPerPixel, cap), null,
                "MP4", token, sourceVideoSubtype: info.VideoSubtype).ConfigureAwait(false);
            if (new FileInfo(temp).Length >= originalBytes)
                throw new ConversionException("This video is already small enough.");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Trim to <paramref name="range"/>, " trimmed" (re-encoded: MP4, or WMV for WMV sources).</summary>
    public static async Task<string> TrimAsync(string input, TimeRange range, CancellationToken ct)
    {
        var info = await MediaCommon.RequireVideoAsync(input, ct).ConfigureAwait(false);
        var trim = MediaCommon.ValidateRange(range, info.Duration);
        var wmv = Kind.ExtensionOf(input) is "wmv" or "asf";

        // Keep the source's quality level: roughly its own video bitrate (at least the "convert" quality).
        var sourceBps = info.Duration > TimeSpan.Zero ? new FileInfo(input).Length * 8.0 / info.Duration.TotalSeconds : 0;
        var bpp = ConvertBitsPerPixel;
        if (sourceBps > 0 && info.Width > 0 && info.Height > 0 && info.FrameRate > 0)
            bpp = Math.Clamp((sourceBps - 128_000) / (info.Width * (double)info.Height * Math.Min(info.FrameRate, 60)), ConvertBitsPerPixel, 0.4);

        return await MediaCommon.CommitAsync(input, wmv ? ".wmv" : ".mp4", " trimmed", (temp, token) =>
            wmv
                ? MediaTranscode.RunTrimmedViaCompositionAsync(input, temp, MediaTranscode.Wmv(info, info.Height, bpp), trim, "WMV", token)
                : MediaTranscode.RunAsync(input, temp, MediaTranscode.Mp4(info, info.Height, bpp),
                    trim, "MP4", token, sourceVideoSubtype: info.VideoSubtype), ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Audio conversions. Same rules as <see cref="VideoConverter"/>. WAV and AIFF are
/// written by hand from PCM (so AIFF in/out works everywhere); M4A, FLAC and MP3 go through <c>MediaTranscoder</c>.
/// </summary>
public static class AudioConverter
{
    /// <summary>Convert to M4A (AAC 44.1 kHz 256 kbps), WAV (PCM 16/24-bit), AIFF (own writer), FLAC or MP3.</summary>
    public static async Task<string> ToFormatAsync(string input, AudioFormat format, CancellationToken ct)
    {
        var info = await MediaCommon.ReadInfoAsync(input, ct).ConfigureAwait(false);
        if (!info.HasAudio) throw new ConversionException("This file has no audio.");

        switch (format)
        {
            case AudioFormat.Wav:
            case AudioFormat.Aiff:
                return await MediaCommon.CommitAsync(input, format == AudioFormat.Wav ? ".wav" : ".aiff", "", (temp, token) =>
                    Task.Run(() => MediaCommon.WritePcm(input, info, temp, format == AudioFormat.Wav, -1, -1, false, token), token), ct)
                    .ConfigureAwait(false);

            case AudioFormat.M4a:
                return await MediaCommon.CommitAsync(input, ".m4a", "", (temp, token) =>
                    MediaCommon.WithTranscodableSource(input, info, token, src => MediaCommon.EncodeAacAsync(src, temp, info, 256_000, token)),
                    ct).ConfigureAwait(false);

            case AudioFormat.Mp3:
                return await MediaCommon.CommitAsync(input, ".mp3", "", (temp, token) =>
                    MediaCommon.WithTranscodableSource(input, info, token, src =>
                        MediaCommon.EncodeMp3Async(src, temp, info, token), flatten16: true), ct).ConfigureAwait(false);

            default:
                return await MediaCommon.CommitAsync(input, ".flac", "", (temp, token) =>
                    MediaCommon.WithTranscodableSource(input, info, token, src => MediaCommon.EncodeFlacAsync(src, temp, info, token)),
                    ct).ConfigureAwait(false);
        }
    }

    /// <summary>Compress: AAC 48 kbps x max(2, channels), " compressed"; keep only if smaller.</summary>
    public static async Task<string> CompressAsync(string input, CancellationToken ct)
    {
        var info = await MediaCommon.ReadInfoAsync(input, ct).ConfigureAwait(false);
        if (!info.HasAudio) throw new ConversionException("This file has no audio.");
        var originalBytes = new FileInfo(input).Length;
        var bitrate = (uint)(48_000 * Math.Max(2, info.AudioChannels));

        return await MediaCommon.CommitAsync(input, ".m4a", " compressed", (temp, token) =>
            MediaCommon.WithTranscodableSource(input, info, token, async src =>
            {
                await MediaCommon.EncodeAacAsync(src, temp, info, bitrate, token, keepSampleRate: true).ConfigureAwait(false);
                if (new FileInfo(temp).Length >= originalBytes)
                    throw new ConversionException("This audio is already small enough.");
            }), ct).ConfigureAwait(false);
    }

    /// <summary>Trim to <paramref name="range"/>, " trimmed": lossless cut for WAV/AIFF, otherwise AAC M4A.</summary>
    public static async Task<string> TrimAsync(string input, TimeRange range, CancellationToken ct)
    {
        var info = await MediaCommon.ReadInfoAsync(input, ct).ConfigureAwait(false);
        if (!info.HasAudio) throw new ConversionException("This file has no audio.");
        var trim = MediaCommon.ValidateRange(range, info.Duration);

        var lossless = MediaCommon.IsPcmContainer(input, out var isWav);
        if (lossless)
        {
            var ext = isWav ? ".wav" : (Kind.ExtensionOf(input) == "aif" ? ".aif" : ".aiff");
            return await MediaCommon.CommitAsync(input, ext, " trimmed", (temp, token) =>
                Task.Run(() => MediaCommon.WritePcm(input, info, temp, isWav, trim.Start.TotalSeconds, trim.Duration.TotalSeconds,
                    true, token), token), ct).ConfigureAwait(false);
        }

        return await MediaCommon.CommitAsync(input, ".m4a", " trimmed", (temp, token) =>
            MediaCommon.WithTranscodableSource(input, info, token, src =>
                MediaCommon.EncodeAacAsync(src, temp, info, 256_000, token, trim: trim)), ct).ConfigureAwait(false);
    }
}
