using System.Buffers.Binary;

namespace MikeShift.Core.Media;

/// <summary>
/// A stream of uncompressed integer PCM: little-endian, interleaved, signed (16/24/32-bit). The common currency between
/// every audio reader (WAV, AIFF, Media Foundation decode) and every PCM writer (WAV, AIFF).
/// </summary>
internal interface IPcmSource : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }

    /// <summary>Bits per sample as delivered by <see cref="Read"/>: 16, 24 or 32.</summary>
    int BitsPerSample { get; }

    /// <summary>Total sample frames if known, otherwise -1.</summary>
    long TotalFrames { get; }

    /// <summary>Reads whole frames into <paramref name="buffer"/>; returns the byte count, 0 at the end.</summary>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Discards <paramref name="frames"/> frames (sample-accurate).</summary>
    void SkipFrames(long frames);
}

internal static class PcmFrames
{
    public static int FrameSize(IPcmSource s) => s.Channels * (s.BitsPerSample / 8);

    /// <summary>Output depth rule (PLAN §1.2): 16-bit, or 24-bit when the source is deeper than 16 bits.</summary>
    public static int OutputBitsFor(int sourceBits) => sourceBits > 16 ? 24 : 16;

    /// <summary>
    /// Converts little-endian packed samples between 16, 24 and 32 bits (only 32 -> 24 and equal depths are lossy-free
    /// by truncating the low byte; 16 -> 24/32 pads with zeros). Returns the number of bytes written to <paramref name="dst"/>.
    /// </summary>
    public static int Convert(byte[] src, int srcOffset, int srcBytes, int srcBits, byte[] dst, int dstBits)
    {
        if (srcBits == dstBits)
        {
            Buffer.BlockCopy(src, srcOffset, dst, 0, srcBytes);
            return srcBytes;
        }

        var srcW = srcBits / 8;
        var dstW = dstBits / 8;
        var n = srcBytes / srcW;
        for (var i = 0; i < n; i++)
        {
            var s = srcOffset + i * srcW;
            var d = i * dstW;
            if (dstW < srcW)
            {
                // Drop least-significant bytes.
                for (var b = 0; b < dstW; b++) dst[d + b] = src[s + (srcW - dstW) + b];
            }
            else
            {
                var pad = dstW - srcW;
                for (var b = 0; b < pad; b++) dst[d + b] = 0;
                for (var b = 0; b < srcW; b++) dst[d + pad + b] = src[s + b];
            }
        }
        return n * dstW;
    }
}

/// <summary>Reads PCM WAV files (format tag 1 or WAVE_FORMAT_EXTENSIBLE/PCM; 16, 24 or 32 bits).</summary>
internal sealed class WavPcmSource : IPcmSource
{
    private readonly FileStream _fs;
    private long _remaining;

    private WavPcmSource(FileStream fs, int rate, int channels, int bits, long dataStart, long dataBytes)
    {
        _fs = fs;
        SampleRate = rate;
        Channels = channels;
        BitsPerSample = bits;
        _remaining = dataBytes - dataBytes % (channels * (bits / 8));
        TotalFrames = _remaining / (channels * (bits / 8));
        fs.Position = dataStart;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int BitsPerSample { get; }
    public long TotalFrames { get; }

    /// <summary>Opens <paramref name="path"/> if it is a plain PCM WAV; null for anything else (Media Foundation handles those).</summary>
    public static WavPcmSource? TryOpen(string path)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            Span<byte> head = stackalloc byte[12];
            if (fs.Read(head) != 12 || !head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WAVE"u8))
                return Fail(fs);

            int rate = 0, channels = 0, bits = 0;
            var fmtSeen = false;
            Span<byte> ch = stackalloc byte[8];
            while (fs.Read(ch) == 8)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(ch[4..]);
                if (ch[..4].SequenceEqual("fmt "u8))
                {
                    if (size < 16 || size > 1024) return Fail(fs);
                    var fmt = new byte[size];
                    fs.ReadExactly(fmt);
                    var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                    rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));
                    if (tag == 0xFFFE)
                    {
                        if (size < 26 || BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24)) != 1) return Fail(fs);
                    }
                    else if (tag != 1)
                    {
                        return Fail(fs);
                    }
                    if (size % 2 == 1) fs.Seek(1, SeekOrigin.Current);
                    fmtSeen = true;
                }
                else if (ch[..4].SequenceEqual("data"u8))
                {
                    if (!fmtSeen || channels < 1 || rate < 1 || bits is not (16 or 24 or 32)) return Fail(fs);
                    var start = fs.Position;
                    var available = fs.Length - start;
                    var bytes = size == 0 || size == 0xFFFFFFFF || size > available ? available : size;
                    return new WavPcmSource(fs, rate, channels, bits, start, bytes);
                }
                else
                {
                    fs.Seek(size + (size & 1), SeekOrigin.Current);
                }
                if (fs.Position >= fs.Length) break;
            }
            return Fail(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            fs?.Dispose();
            return null;
        }
    }

    private static WavPcmSource? Fail(FileStream fs)
    {
        fs.Dispose();
        return null;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var frame = Channels * (BitsPerSample / 8);
        var want = (int)Math.Min(_remaining, count - count % frame);
        if (want <= 0) return 0;
        var got = _fs.Read(buffer, offset, want);
        got -= got % frame;
        _remaining -= got;
        if (got == 0) _remaining = 0;
        return got;
    }

    public void SkipFrames(long frames)
    {
        var bytes = Math.Min(frames * Channels * (BitsPerSample / 8), _remaining);
        _fs.Seek(bytes, SeekOrigin.Current);
        _remaining -= bytes;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>AIFF / AIFF-C ('NONE' and 'sowt') parsing and reading. Media Foundation has no AIFF source, so this is the reader (PLAN §1.2).</summary>
internal sealed class AiffFile
{
    public int Channels { get; init; }
    public long Frames { get; init; }
    public int Bits { get; init; }
    public int SampleRate { get; init; }
    public long DataOffset { get; init; }
    public bool LittleEndian { get; init; }

    /// <summary>True if <paramref name="path"/> is an AIFF/AIFF-C file we can read (full header parse, no decoding).</summary>
    public static bool LooksLikeAiff(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            return TryParse(fs) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Parses the header and chunk list of <paramref name="fs"/> (position is left unspecified). Null if not a supported AIFF.</summary>
    public static AiffFile? TryParse(FileStream fs)
    {
        try
        {
            Span<byte> head = stackalloc byte[12];
            fs.Position = 0;
            if (fs.Read(head) != 12 || !head[..4].SequenceEqual("FORM"u8)) return null;
            var aifc = head[8..12].SequenceEqual("AIFC"u8);
            if (!aifc && !head[8..12].SequenceEqual("AIFF"u8)) return null;

            int channels = 0, bits = 0, rate = 0;
            long frames = 0, dataOffset = -1;
            var little = false;
            var comm = false;
            Span<byte> ch = stackalloc byte[8];
            while (fs.Read(ch) == 8)
            {
                var size = BinaryPrimitives.ReadUInt32BigEndian(ch[4..]);
                var bodyStart = fs.Position;
                if (ch[..4].SequenceEqual("COMM"u8))
                {
                    if (size < 18) return null;
                    var body = new byte[Math.Min(size, 64u)];
                    fs.ReadExactly(body);
                    channels = BinaryPrimitives.ReadUInt16BigEndian(body);
                    frames = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(2));
                    bits = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(6));
                    rate = (int)Math.Round(ReadExtended80(body.AsSpan(8, 10)));
                    if (aifc)
                    {
                        if (size < 22) return null;
                        var type = body.AsSpan(18, 4);
                        if (type.SequenceEqual("sowt"u8)) little = true;
                        else if (!type.SequenceEqual("NONE"u8) && !type.SequenceEqual("twos"u8)) return null;
                    }
                    comm = true;
                }
                else if (ch[..4].SequenceEqual("SSND"u8))
                {
                    var sh = new byte[8];
                    fs.ReadExactly(sh);
                    dataOffset = bodyStart + 8 + BinaryPrimitives.ReadUInt32BigEndian(sh);
                }
                if (comm && dataOffset >= 0) break;
                var next = bodyStart + size + (size & 1);
                if (next > fs.Length) break;
                fs.Position = next;
            }

            if (!comm || dataOffset < 0 || channels < 1 || rate < 1 || bits is not (8 or 16 or 24 or 32)) return null;
            var frameBytes = (long)channels * (bits / 8);
            var available = Math.Max(0, (fs.Length - dataOffset) / frameBytes);
            return new AiffFile
            {
                Channels = channels,
                Bits = bits,
                SampleRate = rate,
                DataOffset = dataOffset,
                Frames = Math.Min(frames, available),
                LittleEndian = little,
            };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Decodes an IEEE 754 80-bit extended float (used for the AIFF sample rate).</summary>
    internal static double ReadExtended80(ReadOnlySpan<byte> b)
    {
        var se = BinaryPrimitives.ReadUInt16BigEndian(b);
        var mant = BinaryPrimitives.ReadUInt64BigEndian(b[2..]);
        var exp = se & 0x7FFF;
        if (exp == 0 && mant == 0) return 0;
        var value = mant * Math.Pow(2, exp - 16383 - 63);
        return (se & 0x8000) != 0 ? -value : value;
    }

    /// <summary>Encodes a positive integer rate as an IEEE 754 80-bit extended float.</summary>
    internal static void WriteExtended80(Span<byte> dest, int rate)
    {
        dest.Clear();
        if (rate <= 0) return;
        var exp = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)rate); // floor(log2(rate))
        var mant = (ulong)rate << (63 - exp);
        BinaryPrimitives.WriteUInt16BigEndian(dest, (ushort)(16383 + exp));
        BinaryPrimitives.WriteUInt64BigEndian(dest[2..], mant);
    }
}

/// <summary>Streams the sample data of an <see cref="AiffFile"/> as little-endian PCM (8-bit AIFF is widened to 16-bit).</summary>
internal sealed class AiffPcmSource : IPcmSource
{
    private readonly FileStream _fs;
    private readonly AiffFile _info;
    private readonly int _srcWidth;
    private long _remainingFrames;
    private byte[] _scratch = [];

    private AiffPcmSource(FileStream fs, AiffFile info)
    {
        _fs = fs;
        _info = info;
        _srcWidth = info.Bits / 8;
        _remainingFrames = info.Frames;
        TotalFrames = info.Frames;
        BitsPerSample = info.Bits == 8 ? 16 : info.Bits;
        fs.Position = info.DataOffset;
    }

    public int SampleRate => _info.SampleRate;
    public int Channels => _info.Channels;
    public int BitsPerSample { get; }
    public long TotalFrames { get; }

    public static AiffPcmSource? TryOpen(string path)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var info = AiffFile.TryParse(fs);
            if (info is null) { fs.Dispose(); return null; }
            return new AiffPcmSource(fs, info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fs?.Dispose();
            return null;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var outFrame = Channels * (BitsPerSample / 8);
        var srcFrame = Channels * _srcWidth;
        var frames = (int)Math.Min(_remainingFrames, count / outFrame);
        if (frames <= 0) return 0;
        if (_scratch.Length < frames * srcFrame) _scratch = new byte[frames * srcFrame];

        var got = _fs.Read(_scratch, 0, frames * srcFrame);
        frames = got / srcFrame;
        if (frames == 0) { _remainingFrames = 0; return 0; }
        _remainingFrames -= frames;

        var samples = frames * Channels;
        var o = offset;
        for (var i = 0; i < samples; i++)
        {
            var s = i * _srcWidth;
            switch (_info.Bits)
            {
                case 8:
                    buffer[o++] = 0;
                    buffer[o++] = _scratch[s]; // signed 8-bit becomes the high byte of a 16-bit sample
                    break;
                case 16:
                    if (_info.LittleEndian) { buffer[o++] = _scratch[s]; buffer[o++] = _scratch[s + 1]; }
                    else { buffer[o++] = _scratch[s + 1]; buffer[o++] = _scratch[s]; }
                    break;
                case 24:
                    if (_info.LittleEndian) { buffer[o++] = _scratch[s]; buffer[o++] = _scratch[s + 1]; buffer[o++] = _scratch[s + 2]; }
                    else { buffer[o++] = _scratch[s + 2]; buffer[o++] = _scratch[s + 1]; buffer[o++] = _scratch[s]; }
                    break;
                default:
                    if (_info.LittleEndian) { for (var b = 0; b < 4; b++) buffer[o++] = _scratch[s + b]; }
                    else { for (var b = 3; b >= 0; b--) buffer[o++] = _scratch[s + b]; }
                    break;
            }
        }
        return frames * outFrame;
    }

    public void SkipFrames(long frames)
    {
        frames = Math.Min(frames, _remainingFrames);
        _fs.Seek(frames * Channels * _srcWidth, SeekOrigin.Current);
        _remainingFrames -= frames;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>PCM container writers. Both write to a seekable stream and patch the sizes at the end.</summary>
internal static class PcmWriter
{
    private const long MaxRiffBytes = 0xFFFF_FFFFL - 1024;

    /// <summary>Writes <paramref name="source"/> as a PCM WAV. <paramref name="maxFrames"/> limits the length (-1 = all).</summary>
    public static void WriteWav(IPcmSource source, Stream output, int outBits, long maxFrames, CancellationToken ct)
    {
        var channels = source.Channels;
        var frameBytes = channels * (outBits / 8);
        var extensible = channels > 2;
        using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        w.Write("RIFF"u8);
        w.Write(0u); // patched
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(extensible ? 40u : 16u);
        w.Write((ushort)(extensible ? 0xFFFE : 1));
        w.Write((ushort)channels);
        w.Write((uint)source.SampleRate);
        w.Write((uint)(source.SampleRate * frameBytes));
        w.Write((ushort)frameBytes);
        w.Write((ushort)outBits);
        if (extensible)
        {
            w.Write((ushort)22);
            w.Write((ushort)outBits);
            w.Write(ChannelMask(channels));
            // KSDATAFORMAT_SUBTYPE_PCM {00000001-0000-0010-8000-00aa00389b71}
            w.Write(new byte[] { 1, 0, 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 });
        }
        w.Write("data"u8);
        w.Write(0u); // patched
        var dataStart = output.Position;

        var written = Pump(source, outBits, maxFrames, ct, (buf, n) =>
        {
            output.Write(buf, 0, n);
            if (output.Position - dataStart > MaxRiffBytes)
                throw new ConversionException("This audio is too long for a WAV file (over 4 GB).");
        });

        if (written % 2 == 1) output.WriteByte(0); // chunk padding
        var end = output.Position;
        output.Position = 4;
        w.Write((uint)(end - 8));
        output.Position = dataStart - 4;
        w.Write((uint)written);
        output.Position = end;
        w.Flush();
    }

    /// <summary>Writes <paramref name="source"/> as an AIFF (big-endian PCM, FORM/COMM/SSND).</summary>
    public static void WriteAiff(IPcmSource source, Stream output, int outBits, long maxFrames, CancellationToken ct)
    {
        var channels = source.Channels;
        var width = outBits / 8;
        var frameBytes = channels * width;
        using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        w.Write("FORM"u8);
        w.Write(0u); // patched
        w.Write("AIFF"u8);

        w.Write("COMM"u8);
        WriteBe32(w, 18);
        WriteBe16(w, (ushort)channels);
        var framesPos = output.Position;
        WriteBe32(w, 0); // number of sample frames, patched
        WriteBe16(w, (ushort)outBits);
        Span<byte> ext = stackalloc byte[10];
        AiffFile.WriteExtended80(ext, source.SampleRate);
        w.Write(ext);

        w.Write("SSND"u8);
        WriteBe32(w, 0); // patched
        WriteBe32(w, 0); // offset
        WriteBe32(w, 0); // block size
        var dataStart = output.Position;

        var swapped = new byte[1 << 16];
        var written = Pump(source, outBits, maxFrames, ct, (buf, n) =>
        {
            if (swapped.Length < n) swapped = new byte[n];
            for (var i = 0; i < n; i += width)
                for (var b = 0; b < width; b++)
                    swapped[i + b] = buf[i + width - 1 - b];
            output.Write(swapped, 0, n);
            if (output.Position - dataStart > MaxRiffBytes)
                throw new ConversionException("This audio is too long for an AIFF file (over 4 GB).");
        });

        if (written % 2 == 1) output.WriteByte(0);
        var end = output.Position;
        output.Position = 4;
        WriteBe32(w, (uint)(end - 8));
        output.Position = framesPos;
        WriteBe32(w, (uint)(written / frameBytes));
        output.Position = dataStart - 12;
        WriteBe32(w, (uint)(written + 8));
        output.Position = end;
        w.Flush();
    }

    /// <summary>Reads <paramref name="source"/> to the end (or <paramref name="maxFrames"/>), converts the depth and hands each block to <paramref name="sink"/>. Returns the bytes written.</summary>
    private static long Pump(IPcmSource source, int outBits, long maxFrames, CancellationToken ct, Action<byte[], int> sink)
    {
        var inFrame = source.Channels * (source.BitsPerSample / 8);
        var outFrame = source.Channels * (outBits / 8);
        var read = new byte[inFrame * 8192];
        var conv = source.BitsPerSample == outBits ? read : new byte[outFrame * 8192];
        long framesLeft = maxFrames < 0 ? long.MaxValue : maxFrames;
        long total = 0;

        while (framesLeft > 0)
        {
            ct.ThrowIfCancellationRequested();
            var want = (int)Math.Min(framesLeft, 8192) * inFrame;
            var got = source.Read(read, 0, want);
            if (got <= 0) break;
            var frames = got / inFrame;
            framesLeft -= frames;
            var n = source.BitsPerSample == outBits
                ? got
                : PcmFrames.Convert(read, 0, got, source.BitsPerSample, conv, outBits);
            sink(conv, n);
            total += n;
        }
        return total;
    }

    private static uint ChannelMask(int channels) => channels switch
    {
        4 => 0x33,
        6 => 0x3F,
        8 => 0x63F,
        _ => (uint)((1L << channels) - 1),
    };

    private static void WriteBe16(BinaryWriter w, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        w.Write(b);
    }

    private static void WriteBe32(BinaryWriter w, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        w.Write(b);
    }
}
