using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace MikeShift.Core.Media;

/// <summary>What the converters need to know about a media file (read through a Media Foundation Source Reader; no decoding).</summary>
internal sealed record MediaInfo
{
    public TimeSpan Duration { get; init; }
    public bool HasVideo { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public Guid VideoSubtype { get; init; }
    public bool HasAudio { get; init; }
    public int AudioChannels { get; init; }
    public int AudioSampleRate { get; init; }

    /// <summary>Bits per sample of the native audio stream (16 when the codec does not say).</summary>
    public int AudioBits { get; init; }
    public Guid AudioSubtype { get; init; }

    public bool IsH264 => VideoSubtype == PInvoke.MFVideoFormat_H264;
    public bool IsAac => AudioSubtype == PInvoke.MFAudioFormat_AAC;

    /// <summary>Reads <paramref name="path"/>; null if Media Foundation cannot open it (AIFF is described by <see cref="TryReadAiff"/>).</summary>
    public static MediaInfo? TryRead(string path)
    {
        var aiff = TryReadAiff(path);
        if (aiff is not null) return aiff;

        IMFSourceReader? reader = null;
        try
        {
            reader = MfInterop.TryOpenReader(path);
            if (reader is null) return null;

            var info = new MediaInfo { Duration = MfInterop.GetDuration(reader) ?? TimeSpan.Zero };

            var v = MfInterop.TryGetNativeType(reader, MfInterop.FirstVideoStream);
            if (v is not null)
            {
                try
                {
                    MfInterop.TryGetUInt64(v, PInvoke.MF_MT_FRAME_SIZE, out var size);
                    MfInterop.TryGetUInt64(v, PInvoke.MF_MT_FRAME_RATE, out var rate);
                    MfInterop.TryGetGuid(v, PInvoke.MF_MT_SUBTYPE, out var sub);
                    var num = (uint)(rate >> 32);
                    var den = (uint)rate;
                    info = info with
                    {
                        HasVideo = true,
                        Width = (int)(size >> 32),
                        Height = (int)(uint)size,
                        FrameRate = den == 0 ? 0 : (double)num / den,
                        VideoSubtype = sub,
                    };
                }
                finally { MfInterop.Release(v); }
            }

            var a = MfInterop.TryGetNativeType(reader, MfInterop.FirstAudioStream);
            if (a is not null)
            {
                try
                {
                    MfInterop.TryGetUInt32(a, PInvoke.MF_MT_AUDIO_NUM_CHANNELS, out var ch);
                    MfInterop.TryGetUInt32(a, PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, out var sr);
                    MfInterop.TryGetUInt32(a, PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, out var bits);
                    MfInterop.TryGetGuid(a, PInvoke.MF_MT_SUBTYPE, out var sub);
                    info = info with
                    {
                        HasAudio = true,
                        AudioChannels = (int)ch,
                        AudioSampleRate = (int)sr,
                        AudioBits = bits == 0 ? 16 : (int)bits,
                        AudioSubtype = sub,
                    };
                }
                finally { MfInterop.Release(a); }
            }

            return info;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            MfInterop.Release(reader);
        }
    }

    private static MediaInfo? TryReadAiff(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            var a = AiffFile.TryParse(fs);
            if (a is null) return null;
            return new MediaInfo
            {
                HasAudio = true,
                AudioChannels = a.Channels,
                AudioSampleRate = a.SampleRate,
                AudioBits = a.Bits,
                Duration = TimeSpan.FromSeconds((double)a.Frames / a.SampleRate),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
