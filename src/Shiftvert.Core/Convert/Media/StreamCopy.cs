using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Shiftvert.Core.Media;

/// <summary>
/// Lossless re-wrap of H.264 (+ AAC) into an MP4 container: <c>IMFSourceReader</c> (compressed samples, no decoding) ->
/// <c>IMFSinkWriter</c> with matching input and output types, so nothing is re-encoded. Used for MOV/MKV/M4V/MP4 ->
/// MP4/M4V when the source is already H.264/AAC. It is strictly an optimisation: any failure (unusual stream, sink
/// refusing the type, bad timestamps) returns false and the caller falls back to the full transcode.
/// </summary>
internal static class StreamCopy
{
    /// <summary>True if <paramref name="info"/> describes a source this path can copy.</summary>
    public static bool CanCopy(MediaInfo info) => info.HasVideo && info.IsH264 && (!info.HasAudio || info.IsAac);

    /// <summary>
    /// Copies the streams of <paramref name="input"/> into a new MP4 at <paramref name="destination"/>.
    /// Returns false (and removes any partial file) when the copy is not possible or produced an implausible file.
    /// </summary>
    public static bool TryRewrapToMp4(string input, MediaInfo info, string destination, CancellationToken ct)
    {
        if (!CanCopy(info)) return false;

        IMFSourceReader? reader = null;
        IMFMediaType? video = null, audio = null;
        IMFAttributes? attrs = null;
        IMFSinkWriter? writer = null;
        var ok = false;
        try
        {
            reader = MfInterop.TryOpenReader(input);
            if (reader is null) return false;

            video = MfInterop.TryGetNativeType(reader, MfInterop.FirstVideoStream);
            if (video is null) return false;
            if (info.HasAudio) audio = MfInterop.TryGetNativeType(reader, MfInterop.FirstAudioStream);

            reader.SetStreamSelection(MfInterop.AllStreams, false);
            reader.SetStreamSelection(MfInterop.FirstVideoStream, true);
            // Setting the output type to the native one asks the reader for the compressed samples as they are stored.
            unsafe { reader.SetCurrentMediaType(MfInterop.FirstVideoStream, null, video); }
            if (audio is not null)
            {
                reader.SetStreamSelection(MfInterop.FirstAudioStream, true);
                unsafe { reader.SetCurrentMediaType(MfInterop.FirstAudioStream, null, audio); }
            }

            attrs = MfInterop.CreateAttributes();
            MfInterop.SetGuid(attrs, PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);
            // Fail instead of silently inserting an encoder/decoder: this path must be a pure copy.
            MfInterop.SetUInt32(attrs, PInvoke.MF_READWRITE_DISABLE_CONVERTERS, 1);
            PInvoke.MFCreateSinkWriterFromURL(destination, null, attrs, out writer).ThrowOnFailure();

            writer.AddStream(video, out var videoOut);
            writer.SetInputMediaType(videoOut, video, null);
            var audioOut = 0u;
            if (audio is not null)
            {
                writer.AddStream(audio, out audioOut);
                writer.SetInputMediaType(audioOut, audio, null);
            }
            writer.BeginWriting();

            Pump(reader, writer, videoOut, audio is not null ? audioOut : null, ct);
            writer.Finalize();
            MfInterop.Release(writer);
            writer = null;

            ok = LooksRight(destination, info);
            return ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ConversionException or IOException or ArgumentException)
        {
            return false;
        }
        finally
        {
            MfInterop.Release(writer);
            MfInterop.Release(attrs);
            MfInterop.Release(audio);
            MfInterop.Release(video);
            MfInterop.Release(reader);
            if (!ok)
            {
                try { File.Delete(destination); } catch (IOException) { /* best effort */ }
            }
        }
    }

    /// <summary>Merges the video and audio sample streams by timestamp into the writer.</summary>
    private static void Pump(IMFSourceReader reader, IMFSinkWriter writer, uint videoOut, uint? audioOut, CancellationToken ct)
    {
        IMFSample? v = Next(reader, MfInterop.FirstVideoStream, out var vTs);
        IMFSample? a = audioOut is null ? null : Next(reader, MfInterop.FirstAudioStream, out _);
        long aTs = 0;
        if (a is not null) a.GetSampleTime(out aTs);

        try
        {
            while (v is not null || a is not null)
            {
                ct.ThrowIfCancellationRequested();
                var takeVideo = v is not null && (a is null || vTs <= aTs);
                if (takeVideo)
                {
                    writer.WriteSample(videoOut, v!);
                    MfInterop.Release(v);
                    v = Next(reader, MfInterop.FirstVideoStream, out vTs);
                }
                else
                {
                    writer.WriteSample(audioOut!.Value, a!);
                    MfInterop.Release(a);
                    a = Next(reader, MfInterop.FirstAudioStream, out aTs);
                }
            }
        }
        finally
        {
            MfInterop.Release(v);
            MfInterop.Release(a);
        }
    }

    /// <summary>The next real sample of <paramref name="stream"/> (skipping gaps/ticks), or null at the end of the stream.</summary>
    private static IMFSample? Next(IMFSourceReader reader, uint stream, out long timestamp)
    {
        while (true)
        {
            if (!MfInterop.ReadSample(reader, stream, out var sample, out timestamp))
            {
                MfInterop.Release(sample);
                return null;
            }
            if (sample is not null) return sample;
        }
    }

    /// <summary>The copy must open, still have video (and audio if the source had it) and be about as long as the source.</summary>
    private static bool LooksRight(string destination, MediaInfo source)
    {
        var copy = MediaInfo.TryRead(destination);
        if (copy is null || !copy.HasVideo || !copy.IsH264) return false;
        if (source.HasAudio && !copy.HasAudio) return false;
        if (source.Duration > TimeSpan.Zero && Math.Abs((copy.Duration - source.Duration).TotalSeconds) > 0.75) return false;
        return new FileInfo(destination).Length > 0;
    }
}
