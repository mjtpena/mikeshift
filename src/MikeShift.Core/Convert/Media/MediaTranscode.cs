using MikeShift.Core.Actions;
using System.Runtime.InteropServices;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace MikeShift.Core.Media;

/// <summary>
/// The one transcode primitive every Media Foundation based conversion goes through: file -> file with a profile,
/// optional trim, cancellation and progress. Callers hand it the temp path from <see cref="Naming.CommitAsync"/>, so a
/// failed or cancelled job never leaves a partial output.
/// </summary>
internal static class MediaTranscode
{
    /// <summary>
    /// Transcodes <paramref name="source"/> into the (not yet existing) file <paramref name="destination"/>.
    /// Hardware acceleration is tried first; a failure retries once in software.
    /// </summary>
    /// <param name="what">Plain-language description of the target for error messages ("MP4", "MP3"...).</param>
    public static async Task RunAsync(
        string source,
        string destination,
        MediaEncodingProfile profile,
        TimeRange? trim,
        string what,
        CancellationToken ct,
        IProgress<double>? progress = null,
        Guid sourceVideoSubtype = default)
    {
        try
        {
            await RunOnceAsync(source, destination, profile, trim, what, hardware: true, ct, progress, sourceVideoSubtype).ConfigureAwait(false);
        }
        catch (ConversionException) when (!ct.IsCancellationRequested)
        {
            // Hardware encoders are the usual suspect on odd machines: retry once in software on a clean destination.
            TryReset(destination);
            await RunOnceAsync(source, destination, profile, trim, what, hardware: false, ct, progress, sourceVideoSubtype).ConfigureAwait(false);
        }
    }

    private static bool TryReset(string path)
    {
        try { File.WriteAllBytes(path, []); return true; }
        catch (IOException) { return false; }
    }

    private static async Task RunOnceAsync(
        string source, string destination, MediaEncodingProfile profile, TimeRange? trim, string what,
        bool hardware, CancellationToken ct, IProgress<double>? progress, Guid sourceVideoSubtype)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(destination))
            await using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }

        try
        {
            var src = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source)).AsTask(ct).ConfigureAwait(false);
            var dst = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(destination)).AsTask(ct).ConfigureAwait(false);

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware };
            if (trim is { } t)
            {
                transcoder.TrimStartTime = t.Start;
                transcoder.TrimStopTime = t.End;
            }

            var prepared = await transcoder.PrepareFileTranscodeAsync(src, dst, profile).AsTask(ct).ConfigureAwait(false);
            if (!prepared.CanTranscode)
                throw new ConversionException(DescribeFailure(prepared.FailureReason, what, sourceVideoSubtype));

            var op = prepared.TranscodeAsync();
            await op.AsTask(ct, progress is null ? null : new Progress<double>(p => progress.Report(p / 100.0))).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new ConversionException($"Windows couldn't convert this file to {what}.", ex);
        }

        if (new FileInfo(destination).Length == 0)
            throw new ConversionException($"Windows couldn't convert this file to {what}.");
    }

    private static string DescribeFailure(TranscodeFailureReason reason, string what, Guid sourceVideoSubtype) =>
        reason switch
        {
            TranscodeFailureReason.CodecNotFound when sourceVideoSubtype == Windows.Win32.PInvoke.MFVideoFormat_HEVC =>
                "This video uses HEVC, which needs the free \"HEVC Video Extensions\" from the Microsoft Store.",
            TranscodeFailureReason.CodecNotFound => $"This PC has no codec to convert this file to {what}.",
            TranscodeFailureReason.InvalidProfile => $"This PC can't create {what} files with these settings.",
            _ => $"Windows can't convert this file to {what}.",
        };

    // ------------------------------------------------------------ profiles

    /// <summary>Largest even value not above <paramref name="v"/> (H.264 and WMV encoders want even sizes), at least 2.</summary>
    internal static uint Even(double v) => (uint)Math.Max(2, (int)v & ~1);

    /// <summary>Fits <paramref name="width"/> x <paramref name="height"/> into <paramref name="maxHeight"/> rows (never upscales); even sizes.</summary>
    internal static (uint W, uint H) FitHeight(int width, int height, int maxHeight)
    {
        if (width <= 0 || height <= 0) return (1280, 720);
        if (height <= maxHeight) return (Even(width), Even(height));
        var scale = (double)maxHeight / height;
        return (Even(width * scale), Even(maxHeight));
    }

    /// <summary>A bits-per-second figure for H.264/WMV at <paramref name="bitsPerPixel"/> (0.15 = good quality) clamped to 0.4..25 Mbps.</summary>
    internal static uint VideoBitrate(uint width, uint height, double fps, double bitsPerPixel) =>
        (uint)Math.Clamp(width * (double)height * fps * bitsPerPixel, 400_000, 25_000_000);

    /// <summary>MP4 (H.264 + AAC) at the source size (or fitted into <paramref name="maxHeight"/>).</summary>
    public static MediaEncodingProfile Mp4(MediaInfo info, int maxHeight, double bitsPerPixel, uint? maxVideoBitrate = null)
    {
        var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        ConfigureVideo(p, info, maxHeight, bitsPerPixel, maxVideoBitrate);
        if (!info.HasAudio) p.Audio = null;
        return p;
    }

    /// <summary>WMV at the source size.</summary>
    public static MediaEncodingProfile Wmv(MediaInfo info, int maxHeight, double bitsPerPixel)
    {
        var p = MediaEncodingProfile.CreateWmv(VideoEncodingQuality.HD720p);
        ConfigureVideo(p, info, maxHeight, bitsPerPixel, null);
        if (!info.HasAudio) p.Audio = null;
        return p;
    }

    private static void ConfigureVideo(MediaEncodingProfile p, MediaInfo info, int maxHeight, double bitsPerPixel, uint? maxBitrate)
    {
        var (w, h) = FitHeight(info.Width, info.Height, maxHeight);
        var fps = info.FrameRate is > 1 and <= 120 ? Math.Min(info.FrameRate, 60) : 30;
        p.Video.Width = w;
        p.Video.Height = h;
        // Rational frame rate; keep the common NTSC ratios exact.
        var (num, den) = Math.Abs(fps - 29.97) < 0.02 ? (30000u, 1001u)
            : Math.Abs(fps - 23.976) < 0.02 ? (24000u, 1001u)
            : Math.Abs(fps - 59.94) < 0.02 ? (60000u, 1001u)
            : ((uint)Math.Round(fps * 1000), 1000u);
        p.Video.FrameRate.Numerator = num;
        p.Video.FrameRate.Denominator = den;
        var bitrate = VideoBitrate(w, h, fps, bitsPerPixel);
        p.Video.Bitrate = maxBitrate is { } cap ? Math.Min(bitrate, Math.Max(cap, 300_000)) : bitrate;
    }

    /// <summary>An audio-only profile: AAC in M4A.</summary>
    public static MediaEncodingProfile M4a(uint bitrate, uint sampleRate, uint channels)
    {
        var p = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
        p.Audio.Subtype = "AAC";
        if (sampleRate > 0) p.Audio.SampleRate = sampleRate;
        if (channels > 0) p.Audio.ChannelCount = channels;
        if (bitrate > 0) p.Audio.Bitrate = bitrate;
        return p;
    }

    public static MediaEncodingProfile Mp3(uint sampleRate = 0, uint channels = 0)
    {
        var p = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
        if (sampleRate > 0) p.Audio.SampleRate = sampleRate;
        if (channels > 0) p.Audio.ChannelCount = channels;
        return p;
    }

    public static MediaEncodingProfile Flac(uint sampleRate, uint channels, uint bits)
    {
        var p = MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High);
        if (sampleRate > 0) p.Audio.SampleRate = sampleRate;
        if (channels > 0) p.Audio.ChannelCount = channels;
        if (bits > 0) p.Audio.BitsPerSample = bits;
        return p;
    }
}
