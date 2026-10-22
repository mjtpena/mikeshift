using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace Shiftvert.Core.Media;

/// <summary>
/// Decodes the first audio stream of any file Media Foundation can open to integer PCM (16-bit, or 24-bit when the
/// source is deeper than 16 bits), keeping the source sample rate and channel count.
/// </summary>
internal sealed class MfPcmSource : IPcmSource
{
    private IMFSourceReader? _reader;
    private byte[] _current = [];
    private int _pos;
    private bool _eof;

    private MfPcmSource(IMFSourceReader reader, int rate, int channels, int bits)
    {
        _reader = reader;
        SampleRate = rate;
        Channels = channels;
        BitsPerSample = bits;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int BitsPerSample { get; }
    public long TotalFrames => -1;

    /// <summary>
    /// Opens <paramref name="path"/> for PCM decoding. Throws <see cref="ConversionException"/> when the file cannot be opened
    /// or has no audio.
    /// </summary>
    public static MfPcmSource Open(string path, MediaInfo info)
    {
        if (!info.HasAudio) throw new ConversionException("This file has no audio.");

        var reader = MfInterop.TryOpenReader(path)
                     ?? throw new ConversionException("Windows can't open this file.");
        try
        {
            MfInterop.SelectOnly(reader, MfInterop.FirstAudioStream);
            var channels = Math.Max(1, info.AudioChannels);
            var rate = Math.Max(8000, info.AudioSampleRate);
            var bits = PcmFrames.OutputBitsFor(info.AudioBits);

            if (!TrySetPcm(reader, rate, channels, bits))
            {
                bits = 16;
                if (!TrySetPcm(reader, rate, channels, bits))
                    throw new ConversionException("Windows can't decode the audio in this file.");
            }
            return new MfPcmSource(reader, rate, channels, bits);
        }
        catch
        {
            MfInterop.Release(reader);
            throw;
        }
    }

    private static bool TrySetPcm(IMFSourceReader reader, int rate, int channels, int bits)
    {
        IMFMediaType? mt = null;
        try
        {
            mt = MfInterop.CreateMediaType();
            var align = (uint)(channels * bits / 8);
            MfInterop.SetGuid(mt, PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
            MfInterop.SetGuid(mt, PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_PCM);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_AUDIO_NUM_CHANNELS, (uint)channels);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)rate);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, (uint)bits);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_AUDIO_BLOCK_ALIGNMENT, align);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, align * (uint)rate);
            MfInterop.SetUInt32(mt, PInvoke.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
            unsafe { reader.SetCurrentMediaType(MfInterop.FirstAudioStream, null, mt); }
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            MfInterop.Release(mt);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var frame = Channels * (BitsPerSample / 8);
        count -= count % frame;
        var written = 0;
        while (written < count)
        {
            if (_pos >= _current.Length && !Refill()) break;
            var n = Math.Min(count - written, _current.Length - _pos);
            Buffer.BlockCopy(_current, _pos, buffer, offset + written, n);
            _pos += n;
            written += n;
        }
        return written - written % frame;
    }

    private bool Refill()
    {
        while (!_eof && _reader is not null)
        {
            if (!MfInterop.ReadSample(_reader, MfInterop.FirstAudioStream, out var sample, out _))
            {
                MfInterop.Release(sample);
                _eof = true;
                return false;
            }
            if (sample is null) continue;
            try
            {
                _current = MfInterop.CopySampleBytes(sample);
                _pos = 0;
            }
            finally
            {
                MfInterop.Release(sample);
            }
            if (_current.Length > 0) return true;
        }
        return false;
    }

    public void SkipFrames(long frames)
    {
        var frame = Channels * (BitsPerSample / 8);
        var bytes = frames * frame;
        var scratch = new byte[frame * 4096];
        while (bytes > 0)
        {
            var got = Read(scratch, 0, (int)Math.Min(bytes, scratch.Length));
            if (got == 0) break;
            bytes -= got;
        }
    }

    public void Dispose()
    {
        MfInterop.Release(_reader);
        _reader = null;
    }
}
