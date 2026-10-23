using MikeShift.Core.Actions;
using Windows.Media.MediaProperties;

namespace MikeShift.Core.Media;

/// <summary>The shared plumbing behind <see cref="VideoConverter"/> and <see cref="AudioConverter"/>.</summary>
internal static class MediaCommon
{
    // ------------------------------------------------------------ inputs

    /// <summary>Reads the container/stream facts of <paramref name="input"/> off the calling thread.</summary>
    public static async Task<MediaInfo> ReadInfoAsync(string input, CancellationToken ct)
    {
        if (!File.Exists(input)) throw new ConversionException("The file could not be found.");
        var info = await Task.Run(() => MediaInfo.TryRead(input), ct).ConfigureAwait(false);
        return info ?? throw new ConversionException("Windows can't open this file.");
    }

    public static async Task<MediaInfo> RequireVideoAsync(string input, CancellationToken ct)
    {
        var info = await ReadInfoAsync(input, ct).ConfigureAwait(false);
        if (!info.HasVideo) throw new ConversionException("This file has no video.");
        return info;
    }

    /// <summary>Validates a trim range against the media duration and clamps its end to it.</summary>
    public static TimeRange ValidateRange(TimeRange range, TimeSpan duration)
    {
        if (range.Start < TimeSpan.Zero || range.End <= range.Start)
            throw new ConversionException("The trim range is empty.");
        if (duration > TimeSpan.Zero && range.Start >= duration)
            throw new ConversionException("The trim range starts after the end of the file.");
        return duration > TimeSpan.Zero && range.End > duration ? new TimeRange(range.Start, duration) : range;
    }

    // ------------------------------------------------------------ outputs

    /// <summary>Plans the output name and runs <paramref name="write"/> against the temp path (temp-then-move, never partial).</summary>
    public static async Task<string> CommitAsync(
        string input, string ext, string suffix, Func<string, CancellationToken, Task> write, CancellationToken ct)
    {
        var result = await Naming.CommitAsync(Naming.Plan(input, ext, suffix), write, ct).ConfigureAwait(false);
        return result.Path;
    }

    // ------------------------------------------------------------ PCM (WAV / AIFF)

    /// <summary>Opens any readable audio file as PCM: WAV and AIFF by their own readers, everything else through Media Foundation.</summary>
    public static IPcmSource OpenPcm(string input, MediaInfo info) =>
        (IPcmSource?)WavPcmSource.TryOpen(input) ?? (IPcmSource?)AiffPcmSource.TryOpen(input) ?? MfPcmSource.Open(input, info);

    /// <summary>True if <paramref name="input"/> is a PCM WAV or AIFF (so it can be cut losslessly).</summary>
    public static bool IsPcmContainer(string input, out bool isWav)
    {
        using (var wav = WavPcmSource.TryOpen(input))
        {
            if (wav is not null) { isWav = true; return true; }
        }
        using (var aiff = AiffPcmSource.TryOpen(input))
        {
            isWav = false;
            return aiff is not null;
        }
    }

    /// <summary>
    /// Writes <paramref name="input"/> as WAV or AIFF to <paramref name="dest"/>. <paramref name="startSeconds"/> &lt; 0 means from
    /// the start, <paramref name="durationSeconds"/> &lt;= 0 means to the end. <paramref name="keepBits"/> keeps the source depth
    /// (lossless trim); otherwise 16-bit, or 24-bit for deeper sources.
    /// </summary>
    public static void WritePcm(string input, MediaInfo info, string dest, bool wav, double startSeconds, double durationSeconds,
        bool keepBits, CancellationToken ct)
    {
        using var src = OpenPcm(input, info);
        if (startSeconds > 0)
            src.SkipFrames((long)Math.Round(startSeconds * src.SampleRate));
        var maxFrames = durationSeconds > 0 ? (long)Math.Round(durationSeconds * src.SampleRate) : -1;
        var bits = keepBits ? src.BitsPerSample : PcmFrames.OutputBitsFor(src.BitsPerSample);

        using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        if (wav) PcmWriter.WriteWav(src, fs, bits, maxFrames, ct);
        else PcmWriter.WriteAiff(src, fs, bits, maxFrames, ct);
    }

    // ------------------------------------------------------------ Media Foundation encoders

    /// <summary>
    /// Runs <paramref name="body"/> with a path Media Foundation can read: the input itself, or for AIFF (which Media Foundation
    /// has no source for) a temporary WAV decoded by the built-in AIFF reader, deleted afterwards.
    /// </summary>
    public static async Task WithTranscodableSource(string input, MediaInfo info, CancellationToken ct, Func<string, Task> body)
    {
        if (!AiffFile.LooksLikeAiff(input))
        {
            await body(input).ConfigureAwait(false);
            return;
        }

        var staged = Path.Combine(Path.GetTempPath(), $"mikeshift-{Guid.NewGuid():N}.wav");
        try
        {
            await Task.Run(() =>
            {
                using var src = AiffPcmSource.TryOpen(input) ?? throw new ConversionException("This AIFF file is damaged.");
                using var fs = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
                PcmWriter.WriteWav(src, fs, src.BitsPerSample, -1, ct);
            }, ct).ConfigureAwait(false);
            await body(staged).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(staged); } catch (Exception) { /* temp file; harmless if it lingers */ }
        }
    }

    /// <summary>
    /// AAC in M4A at (about) <paramref name="bitrate"/>: tries the requested bitrate, then the lower ones the AAC encoder
    /// supports, then the encoder's default, so an unsupported bitrate never fails the whole job.
    /// </summary>
    public static async Task EncodeAacAsync(string source, string destination, MediaInfo info, uint bitrate, CancellationToken ct,
        bool keepSampleRate = false, TimeRange? trim = null)
    {
        var rate = keepSampleRate && info.AudioSampleRate is 44100 or 48000 ? (uint)info.AudioSampleRate : 44100u;
        var channels = (uint)Math.Clamp(info.AudioChannels, 1, 2);
        var ladder = new[] { bitrate, 192_000u, 160_000u, 128_000u, 96_000u, 0u }.Where(b => b <= bitrate || b == 0).Distinct().ToArray();

        ConversionException? last = null;
        foreach (var rung in ladder)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.WriteAllBytes(destination, []);
                await MediaTranscode.RunAsync(source, destination, MediaTranscode.M4a(rung, rung == 0 ? 0 : rate, rung == 0 ? 0 : channels),
                    trim, "M4A", ct).ConfigureAwait(false);
                return;
            }
            catch (ConversionException ex)
            {
                last = ex;
            }
        }
        throw last ?? new ConversionException("Windows can't create M4A files on this PC.");
    }

    /// <summary>MP3 at the source's sample rate when MP3 supports it (32/44.1/48 kHz, else 44.1 kHz), stereo at most; encoder defaults as fallback.</summary>
    public static async Task EncodeMp3Async(string source, string destination, MediaInfo info, CancellationToken ct)
    {
        var rate = info.AudioSampleRate is 32000 or 44100 or 48000 ? (uint)info.AudioSampleRate : 44100u;
        var channels = (uint)Math.Clamp(info.AudioChannels, 1, 2);
        try
        {
            File.WriteAllBytes(destination, []);
            await MediaTranscode.RunAsync(source, destination, MediaTranscode.Mp3(rate, channels), null, "MP3", ct).ConfigureAwait(false);
        }
        catch (ConversionException)
        {
            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(destination, []);
            await MediaTranscode.RunAsync(source, destination, MediaTranscode.Mp3(), null, "MP3", ct).ConfigureAwait(false);
        }
    }

    /// <summary>FLAC at the source's sample rate and depth (16 or 24 bit), falling back to the encoder default.</summary>
    public static async Task EncodeFlacAsync(string source, string destination, MediaInfo info, CancellationToken ct)
    {
        var rate = (uint)Math.Max(8000, info.AudioSampleRate);
        var channels = (uint)Math.Clamp(info.AudioChannels, 1, 8);
        var bits = (uint)PcmFrames.OutputBitsFor(info.AudioBits);

        try
        {
            File.WriteAllBytes(destination, []);
            await MediaTranscode.RunAsync(source, destination, MediaTranscode.Flac(rate, channels, bits), null, "FLAC", ct).ConfigureAwait(false);
        }
        catch (ConversionException)
        {
            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(destination, []);
            await MediaTranscode.RunAsync(source, destination, MediaTranscode.Flac(0, 0, 0), null, "FLAC", ct).ConfigureAwait(false);
        }
    }
}
