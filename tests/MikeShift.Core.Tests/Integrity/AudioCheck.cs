using System.Buffers.Binary;
using System.Security.Cryptography;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>Interleaved little-endian signed PCM. 8-bit is not used by the suite.</summary>
internal sealed record Pcm(int Rate, int Channels, int Bits, byte[] Data)
{
    public int BytesPerFrame => Channels * Bits / 8;
    public int Frames => Data.Length / BytesPerFrame;
    public double Seconds => (double)Frames / Rate;

    /// <summary>Sample <paramref name="ch"/> of frame <paramref name="frame"/> as a signed integer at the file's own depth.</summary>
    public int Sample(int frame, int ch)
    {
        var o = frame * BytesPerFrame + ch * (Bits / 8);
        return Bits switch
        {
            16 => BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(o)),
            24 => (Data[o] | (Data[o + 1] << 8) | ((sbyte)Data[o + 2] << 16)),
            32 => BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(o)),
            _ => throw new NotSupportedException("bits " + Bits),
        };
    }

    /// <summary>The same samples scaled to 24-bit integers (16-bit shifted up) so different depths can be compared.</summary>
    public int Sample24(int frame, int ch) => Bits switch { 16 => Sample(frame, ch) << 8, 24 => Sample(frame, ch), _ => Sample(frame, ch) >> 8 };

    /// <summary>Frames [start, start+count) as a new Pcm.</summary>
    public Pcm Slice(int start, int count) => this with { Data = Data.AsSpan(start * BytesPerFrame, count * BytesPerFrame).ToArray() };

    /// <summary>Mono mix in [-1, 1].</summary>
    public double[] Mono()
    {
        var scale = 1.0 / (1L << (Bits - 1));
        var o = new double[Frames];
        for (var i = 0; i < o.Length; i++)
        {
            double s = 0;
            for (var c = 0; c < Channels; c++) s += Sample(i, c);
            o[i] = s * scale / Channels;
        }
        return o;
    }
}

/// <summary>
/// Independent audio readers: hand-written WAV / AIFF / FLAC / MP3 / MP4 parsers (nothing shared with the converters), Windows'
/// shell property system for durations, and a Media Foundation decode (via <c>MediaTranscoder</c> to WAV, a different path
/// from the Source Reader the converters use) for the compressed formats.
/// </summary>
internal static class AudioCheck
{
    /// <summary>Reads a whole file, tolerating the short moment after a WinRT transcode in which Windows still holds a handle.</summary>
    public static byte[] ReadAll(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var b = new byte[fs.Length];
                fs.ReadExactly(b);
                return b;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    // ------------------------------------------------------------------ WAV

    public static Pcm ReadWav(string path)
    {
        var b = ReadAll(path);
        Assert.True(b.Length > 44 && b.AsSpan(0, 4).SequenceEqual("RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WAVE"u8), "not a RIFF/WAVE file: " + path);
        int pos = 12, rate = 0, ch = 0, bits = 0;
        var tag = 0;
        byte[]? data = null;
        while (pos + 8 <= b.Length)
        {
            var id = b.AsSpan(pos, 4);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
            var body = pos + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                tag = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body));
                ch = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 14));
                if (tag == 0xFFFE) tag = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 24)); // sub-format GUID starts with the real tag
            }
            else if (id.SequenceEqual("data"u8))
            {
                var len = (int)Math.Min(size, b.Length - body);
                data = b.AsSpan(body, len).ToArray();
                break;
            }
            pos = (int)Math.Min(b.Length, body + size + (size & 1));
        }
        Assert.Equal(1, tag);
        Assert.NotNull(data);
        var frame = ch * bits / 8;
        Assert.True(data!.Length % frame == 0, "data chunk is not a whole number of frames");
        return new Pcm(rate, ch, bits, data);
    }

    /// <summary>Header consistency: RIFF size and data size agree with the file length (a "corrupt WAV" is usually this).</summary>
    public static void AssertWavHeaderConsistent(string path)
    {
        var b = ReadAll(path);
        var riff = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4));
        Assert.Equal((uint)(b.Length - 8), riff);
        var pos = 12;
        while (pos + 8 <= b.Length)
        {
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
            if (b.AsSpan(pos, 4).SequenceEqual("data"u8))
            {
                Assert.Equal(b.Length, pos + 8 + size + (size & 1));
                return;
            }
            pos += (int)(8 + size + (size & 1));
        }
        Assert.Fail("no data chunk");
    }

    public static byte[] WavBytes(Pcm p)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + p.Data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)p.Channels); w.Write(p.Rate);
        w.Write(p.Rate * p.BytesPerFrame); w.Write((short)p.BytesPerFrame); w.Write((short)p.Bits);
        w.Write("data"u8); w.Write(p.Data.Length); w.Write(p.Data);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ AIFF

    public static Pcm ReadAiff(string path)
    {
        var b = ReadAll(path);
        Assert.True(b.Length > 54 && b.AsSpan(0, 4).SequenceEqual("FORM"u8), "not a FORM file");
        var kind = b.AsSpan(8, 4);
        Assert.True(kind.SequenceEqual("AIFF"u8) || kind.SequenceEqual("AIFC"u8), "not AIFF");
        var formSize = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
        Assert.Equal((uint)(b.Length - 8), formSize);
        int pos = 12, ch = 0, bits = 0, rate = 0;
        long frames = 0;
        byte[]? ssnd = null;
        var little = false;
        while (pos + 8 <= b.Length)
        {
            var id = b.AsSpan(pos, 4);
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(pos + 4));
            var body = pos + 8;
            if (id.SequenceEqual("COMM"u8))
            {
                ch = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(body));
                frames = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(body + 2));
                bits = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(body + 6));
                rate = (int)Math.Round(ReadExtended(b.AsSpan(body + 8, 10)));
                if (kind.SequenceEqual("AIFC"u8))
                {
                    var comp = System.Text.Encoding.ASCII.GetString(b, body + 18, 4);
                    Assert.True(comp is "NONE" or "sowt" or "twos", "compressed AIFC: " + comp);
                    little = comp == "sowt";
                }
            }
            else if (id.SequenceEqual("SSND"u8))
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(body));
                var len = (int)(size - 8 - offset);
                ssnd = b.AsSpan(body + 8 + offset, len).ToArray();
            }
            pos = (int)Math.Min(b.Length, body + size + (size & 1));
        }
        Assert.NotNull(ssnd);
        var width = bits / 8;
        Assert.Equal(frames * ch * width, ssnd!.Length);
        if (!little) // big-endian -> little-endian
            for (var i = 0; i + width <= ssnd.Length; i += width) Array.Reverse(ssnd, i, width);
        return new Pcm(rate, ch, bits, ssnd);
    }

    public static byte[] AiffBytes(Pcm p)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var width = p.Bits / 8;
        var data = (byte[])p.Data.Clone();
        for (var i = 0; i + width <= data.Length; i += width) Array.Reverse(data, i, width);
        void BE32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(s, v); w.Write(s); }
        void BE16(ushort v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(s, v); w.Write(s); }
        w.Write("FORM"u8); BE32((uint)(4 + 8 + 18 + 8 + 8 + data.Length)); w.Write("AIFF"u8);
        w.Write("COMM"u8); BE32(18); BE16((ushort)p.Channels); BE32((uint)p.Frames); BE16((ushort)p.Bits); w.Write(Extended(p.Rate));
        w.Write("SSND"u8); BE32((uint)(8 + data.Length)); BE32(0); BE32(0); w.Write(data);
        return ms.ToArray();
    }

    private static double ReadExtended(ReadOnlySpan<byte> e)
    {
        var exp = ((e[0] & 0x7F) << 8) | e[1];
        var mant = BinaryPrimitives.ReadUInt64BigEndian(e[2..]);
        if (exp == 0 && mant == 0) return 0;
        return Math.Pow(2, exp - 16383 - 63) * mant;
    }

    private static byte[] Extended(int rate)
    {
        var e = new byte[10];
        var exp = 0;
        var m = (ulong)rate;
        while ((m & 0x8000000000000000UL) == 0) { m <<= 1; exp++; }
        var biased = 16383 + 63 - exp;
        e[0] = (byte)(biased >> 8); e[1] = (byte)biased;
        BinaryPrimitives.WriteUInt64BigEndian(e.AsSpan(2), m);
        return e;
    }

    // ------------------------------------------------------------------ FLAC

    public sealed record FlacInfo(int Rate, int Channels, int Bits, long TotalSamples, byte[] Md5);

    public static FlacInfo ReadFlac(string path)
    {
        var b = ReadAll(path);
        Assert.True(b.Length > 42 && b.AsSpan(0, 4).SequenceEqual("fLaC"u8), "not a FLAC file");
        Assert.Equal(0, b[4] & 0x7F); // first metadata block is STREAMINFO
        Assert.Equal(34, (b[5] << 16) | (b[6] << 8) | b[7]);
        var s = b.AsSpan(8, 34);
        var rate = (s[10] << 12) | (s[11] << 4) | (s[12] >> 4);
        var ch = ((s[12] >> 1) & 7) + 1;
        var bits = (((s[12] & 1) << 4) | (s[13] >> 4)) + 1;
        var total = ((long)(s[13] & 0xF) << 32) | ((long)s[14] << 24) | ((long)s[15] << 16) | ((long)s[16] << 8) | s[17];
        return new FlacInfo(rate, ch, bits, total, s.Slice(18, 16).ToArray());
    }

    /// <summary>MD5 of the interleaved little-endian PCM, as the FLAC spec defines the STREAMINFO signature.</summary>
    public static byte[] FlacMd5(Pcm p) => MD5.HashData(p.Data);

    // ------------------------------------------------------------------ MP4 / M4A / M4V

    public sealed record Mp4Track(string Handler, string Codec, long Duration, uint Timescale, int Width, int Height, int Samples = 0);

    public sealed record Mp4Info(string Brand, double MovieSeconds, IReadOnlyList<Mp4Track> Tracks, bool MoovBeforeMdat)
    {
        public Mp4Track? Audio => Tracks.FirstOrDefault(t => t.Handler == "soun");
        public Mp4Track? Video => Tracks.FirstOrDefault(t => t.Handler == "vide");
    }

    /// <summary>Walks the ISO-BMFF box tree (ftyp, moov, mvhd, trak, tkhd, mdia, hdlr, minf/stbl/stsd). Throws if the structure is broken.</summary>
    public static Mp4Info ReadMp4(string path)
    {
        var b = ReadAll(path);
        string brand = "";
        double movie = 0;
        var tracks = new List<Mp4Track>();
        int moovAt = -1, mdatAt = -1;

        void Walk(int start, int end, string parent, Action<string, int, int>? visit)
        {
            var pos = start;
            while (pos + 8 <= end)
            {
                long size = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(pos));
                var type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
                var hdr = 8;
                if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(pos + 8)); hdr = 16; }
                else if (size == 0) size = end - pos;
                Assert.True(size >= hdr && pos + size <= end, $"broken box '{type}' at {pos} (size {size}) inside '{parent}'");
                visit?.Invoke(type, pos + hdr, (int)(pos + size));
                pos = (int)(pos + size);
            }
            Assert.Equal(end, pos);
        }

        Walk(0, b.Length, "file", (type, body, end) =>
        {
            if (type == "ftyp") brand = System.Text.Encoding.ASCII.GetString(b, body, 4);
            if (type == "mdat" && mdatAt < 0) mdatAt = body;
            if (type != "moov") return;
            moovAt = body;
            Walk(body, end, "moov", (t, bb, ee) =>
            {
                if (t == "mvhd")
                {
                    var ver = b[bb];
                    var ts = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(bb + (ver == 1 ? 20 : 12)));
                    var dur = ver == 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(bb + 24)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(bb + 16));
                    movie = (double)dur / ts;
                }
                if (t != "trak") return;
                string handler = "", codec = "";
                long mdur = 0;
                uint mts = 1;
                int w = 0, h = 0, samples = 0;
                Walk(bb, ee, "trak", (t2, b2, e2) =>
                {
                    if (t2 != "mdia") return;
                    Walk(b2, e2, "mdia", (t3, b3, e3) =>
                    {
                        if (t3 == "mdhd")
                        {
                            var ver = b[b3];
                            mts = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(b3 + (ver == 1 ? 20 : 12)));
                            mdur = ver == 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(b3 + 24)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(b3 + 16));
                        }
                        if (t3 == "hdlr") handler = System.Text.Encoding.ASCII.GetString(b, b3 + 8, 4);
                        if (t3 != "minf") return;
                        Walk(b3, e3, "minf", (t4, b4, e4) =>
                        {
                            if (t4 != "stbl") return;
                            Walk(b4, e4, "stbl", (t5, b5, e5) =>
                            {
                                if (t5 == "stsz") samples = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(b5 + 8));
                                if (t5 != "stsd") return;
                                codec = System.Text.Encoding.ASCII.GetString(b, b5 + 12, 4);
                                if (handler == "vide")
                                {
                                    w = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(b5 + 8 + 8 + 24));
                                    h = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(b5 + 8 + 8 + 26));
                                }
                            });
                        });
                    });
                });
                tracks.Add(new Mp4Track(handler, codec, mdur, mts, w, h, samples));
            });
        });

        Assert.True(moovAt >= 0, "no moov box");
        return new Mp4Info(brand, movie, tracks, moovAt < mdatAt || mdatAt < 0);
    }

    // ------------------------------------------------------------------ MP3

    public sealed record Mp3Info(int Frames, int Rate, int Channels, int Bitrate, double Seconds, bool HasId3v2);

    private static readonly int[] Mpeg1L3Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static readonly int[] Mpeg2L3Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
    private static readonly int[] Mpeg1Rates = [44100, 48000, 32000, 0];

    /// <summary>Walks MPEG audio frames (Layer III) end to end; every frame header must be valid and chain to the next.</summary>
    public static Mp3Info ReadMp3(string path)
    {
        var b = ReadAll(path);
        var pos = 0;
        var id3 = false;
        if (b.Length > 10 && b.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            id3 = true;
            pos = 10 + ((b[6] << 21) | (b[7] << 14) | (b[8] << 7) | b[9]);
        }
        // find the first sync
        while (pos + 4 < b.Length && !(b[pos] == 0xFF && (b[pos + 1] & 0xE0) == 0xE0)) pos++;
        int frames = 0, rate = 0, ch = 0, br = 0;
        double seconds = 0;
        while (pos + 4 <= b.Length)
        {
            if (b.Length - pos >= 128 && b.AsSpan(pos, 3).SequenceEqual("TAG"u8) && b.Length - pos == 128) break; // ID3v1 trailer
            if (!(b[pos] == 0xFF && (b[pos + 1] & 0xE0) == 0xE0)) break;
            var version = (b[pos + 1] >> 3) & 3; // 3 = MPEG1, 2 = MPEG2, 0 = 2.5
            var layer = (b[pos + 1] >> 1) & 3;    // 1 = Layer III
            Assert.Equal(1, layer);
            var brIdx = b[pos + 2] >> 4;
            var srIdx = (b[pos + 2] >> 2) & 3;
            var padding = (b[pos + 2] >> 1) & 1;
            Assert.NotEqual(15, brIdx);
            Assert.NotEqual(3, srIdx);
            var mpeg1 = version == 3;
            var bitrate = (mpeg1 ? Mpeg1L3Bitrates : Mpeg2L3Bitrates)[brIdx] * 1000;
            var sr = Mpeg1Rates[srIdx] >> (mpeg1 ? 0 : version == 2 ? 1 : 2);
            var samples = mpeg1 ? 1152 : 576;
            var len = (mpeg1 ? 144 : 72) * bitrate / sr + padding;
            if (bitrate == 0) break; // free format: not produced by Windows
            rate = sr; br = bitrate; ch = ((b[pos + 3] >> 6) & 3) == 3 ? 1 : 2;
            frames++;
            seconds += (double)samples / sr;
            pos += len;
        }
        Assert.True(frames > 0, "no MPEG audio frames");
        return new Mp3Info(frames, rate, ch, br, seconds, id3);
    }

    // ------------------------------------------------------------------ Windows-side decode and properties

    /// <summary>Duration as the Windows shell property system reports it (System.Media.Duration).</summary>
    public static async Task<TimeSpan> ShellDurationAsync(string path)
    {
        var f = await StorageFile.GetFileFromPathAsync(path);
        var props = await f.Properties.RetrievePropertiesAsync(["System.Media.Duration"]);
        return props.TryGetValue("System.Media.Duration", out var v) && v is ulong ticks ? TimeSpan.FromTicks((long)ticks) : TimeSpan.Zero;
    }

    /// <summary>
    /// Decodes any file Media Foundation can read to PCM by transcoding it to WAV at its own rate and channel count.
    /// <paramref name="bits"/> is the depth asked of the WAV encoder (16 or 24).
    /// </summary>
    public static async Task<Pcm> DecodeWithMfAsync(string path, int rate, int channels, int bits = 16)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "mikeshift-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var dst = Path.Combine(tmpDir, "decoded.wav");
            await File.WriteAllBytesAsync(dst, []);
            var src = await StorageFile.GetFileFromPathAsync(path);
            var dest = await StorageFile.GetFileFromPathAsync(dst);
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            profile.Audio = AudioEncodingProperties.CreatePcm((uint)rate, (uint)channels, (uint)bits);
            profile.Video = null;
            var prep = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dest, profile);
            Assert.True(prep.CanTranscode, "Media Foundation cannot decode " + Path.GetFileName(path) + ": " + prep.FailureReason);
            await prep.TranscodeAsync();
            return ReadWav(dst);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch (IOException) { /* temp */ }
        }
    }

    /// <summary>Encodes a WAV with Media Foundation to MP3 / M4A / FLAC (used only to make source fixtures).</summary>
    public static async Task EncodeWithMfAsync(string wav, string dst, string kind)
    {
        await File.WriteAllBytesAsync(dst, []);
        var src = await StorageFile.GetFileFromPathAsync(wav);
        var dest = await StorageFile.GetFileFromPathAsync(dst);
        var pcm = ReadWav(wav);
        MediaEncodingProfile profile = kind switch
        {
            "mp3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
            "m4a" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
            "flac" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
            _ => throw new ArgumentException(kind),
        };
        if (kind == "flac") profile.Audio = AudioEncodingProperties.CreateFlac((uint)pcm.Rate, (uint)pcm.Channels, (uint)pcm.Bits);
        else
        {
            profile.Audio.SampleRate = (uint)pcm.Rate;
            profile.Audio.ChannelCount = (uint)pcm.Channels;
        }
        var prep = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dest, profile);
        Assert.True(prep.CanTranscode, "cannot make the " + kind + " fixture: " + prep.FailureReason);
        await prep.TranscodeAsync();
    }

    // ------------------------------------------------------------------ signal comparison

    /// <summary>
    /// The peak normalised cross-correlation between the mono mixes of <paramref name="a"/> and <paramref name="b"/> when
    /// <paramref name="b"/> is shifted by up to <paramref name="maxLagMs"/> either way. Both are low-passed and decimated first, so
    /// codec delay and noise do not matter but a wrong segment or a damaged signal does. Returns (correlation, lag in ms).
    /// </summary>
    public static (double Corr, double LagMs) Correlate(Pcm a, Pcm b, int maxLagMs = 100)
    {
        const int decimate = 4;
        var x = Decimate(a.Mono(), decimate);
        var y = Decimate(b.Mono(), decimate);
        var rate = a.Rate / (double)decimate;
        var maxLag = (int)(maxLagMs / 1000.0 * rate);
        var best = double.NegativeInfinity;
        var bestLag = 0;
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            double sxy = 0, sxx = 0, syy = 0;
            for (var i = 0; i < x.Length; i++)
            {
                var j = i + lag;
                if (j < 0 || j >= y.Length) continue;
                sxy += x[i] * y[j];
                sxx += x[i] * x[i];
                syy += y[j] * y[j];
            }
            if (sxx <= 0 || syy <= 0) continue;
            var c = sxy / Math.Sqrt(sxx * syy);
            if (c > best) { best = c; bestLag = lag; }
        }
        return (best, bestLag / rate * 1000.0);
    }

    /// <summary>
    /// Correlation of the LAST <paramref name="ms"/> milliseconds (100 by default) of <paramref name="source"/> with the same stretch of
    /// <paramref name="decoded"/> (shifted by <paramref name="lagMs"/>, as found by <see cref="Correlate"/>). Falls to ~0 when the
    /// encoder lost the end of the music, which whole-file correlation can hide.
    /// </summary>
    public static double TailCorrelation(Pcm source, Pcm decoded, double lagMs, int ms = 100)
    {
        var x = source.Mono();
        var y = decoded.Mono();
        var lag0 = (int)Math.Round(lagMs / 1000.0 * source.Rate);
        var count = source.Rate * ms / 1000;
        var best = 0.0;
        // The whole-file lag is found on decimated audio; refine it to single samples here (a 4 kHz tone is sensitive to that).
        for (var lag = lag0 - 48; lag <= lag0 + 48; lag++)
        {
            double sxy = 0, sxx = 0, syy = 0;
            for (var i = x.Length - count; i < x.Length; i++)
            {
                var j = i + lag;
                var v = j >= 0 && j < y.Length ? y[j] : 0;
                sxy += x[i] * v; sxx += x[i] * x[i]; syy += v * v;
            }
            if (sxx > 0 && syy > 0) best = Math.Max(best, sxy / Math.Sqrt(sxx * syy));
        }
        return best;
    }

    private static double[] Decimate(double[] s, int factor)
    {
        var o = new double[s.Length / factor];
        for (var i = 0; i < o.Length; i++)
        {
            double sum = 0;
            for (var k = 0; k < factor; k++) sum += s[i * factor + k];
            o[i] = sum / factor;
        }
        return o;
    }

    /// <summary>First frame index where two PCMs differ (compared at 24-bit scale), or -1 if the common length is identical.</summary>
    public static int FirstDifference(Pcm a, Pcm b, int tolerance = 0)
    {
        Assert.Equal(a.Channels, b.Channels);
        var n = Math.Min(a.Frames, b.Frames);
        for (var f = 0; f < n; f++)
            for (var c = 0; c < a.Channels; c++)
                if (Math.Abs(a.Sample24(f, c) - b.Sample24(f, c)) > tolerance) return f;
        return -1;
    }
}
