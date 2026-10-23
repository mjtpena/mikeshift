using System.Buffers.Binary;
using System.Text;
using MikeShift.Core.Media;

namespace MikeShift.Core.Tests.Media;

/// <summary>The built-in WAV/AIFF readers and writers, depth conversion and the 80-bit float: all in memory, no codecs involved.</summary>
public class PcmCoverageTests
{
    // ------------------------------------------------------------------ a deterministic in-memory source

    /// <summary>Little-endian PCM where the n-th sample (counting across channels) has value n (wrapped to the sample width).</summary>
    private sealed class RampSource(int channels, int rate, int bits, long frames) : IPcmSource
    {
        private long _next;
        public int SampleRate => rate;
        public int Channels => channels;
        public int BitsPerSample => bits;
        public long TotalFrames => frames;
        public bool Disposed { get; private set; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var frameBytes = channels * (bits / 8);
            var want = (int)Math.Min(frames - _next, count / frameBytes);
            if (want <= 0) return 0;
            for (var f = 0; f < want; f++)
                for (var c = 0; c < channels; c++)
                {
                    var value = (_next + f) * channels + c;
                    for (var b = 0; b < bits / 8; b++)
                        buffer[offset++] = (byte)((value >> (8 * b)) & 0xFF);
                }
            _next += want;
            return want * frameBytes;
        }

        public void SkipFrames(long n) => _next = Math.Min(frames, _next + n);
        public void Dispose() => Disposed = true;
    }

    /// <summary>The bytes <see cref="RampSource"/> produces for the given frame range.</summary>
    private static byte[] RampBytes(int channels, int bits, long firstFrame, long frames)
    {
        var src = new RampSource(channels, 44100, bits, firstFrame + frames);
        src.SkipFrames(firstFrame);
        var buf = new byte[frames * channels * (bits / 8)];
        var read = 0;
        while (read < buf.Length)
        {
            var n = src.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        Assert.Equal(buf.Length, read);
        return buf;
    }

    private static byte[] ReadAll(IPcmSource s)
    {
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ depth conversion

    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 24)]
    [InlineData(24, 24)]
    [InlineData(32, 24)]
    public void Output_depth_is_16_bit_unless_the_source_is_deeper(int source, int expected) =>
        Assert.Equal(expected, PcmFrames.OutputBitsFor(source));

    [Fact]
    public void Frame_size_is_channels_times_bytes_per_sample()
    {
        Assert.Equal(6, PcmFrames.FrameSize(new RampSource(3, 8000, 16, 0)));
        Assert.Equal(8, PcmFrames.FrameSize(new RampSource(2, 8000, 32, 0)));
    }

    [Fact]
    public void Depth_conversion_pads_with_zeros_when_widening_and_drops_the_low_bytes_when_narrowing()
    {
        byte[] s16 = [0x34, 0x12, 0xCD, 0xAB];             // 0x1234, 0xABCD
        var s24 = new byte[6];
        Assert.Equal(6, PcmFrames.Convert(s16, 0, 4, 16, s24, 24));
        Assert.Equal([0x00, 0x34, 0x12, 0x00, 0xCD, 0xAB], s24);

        var s32 = new byte[8];
        Assert.Equal(8, PcmFrames.Convert(s16, 0, 4, 16, s32, 32));
        Assert.Equal([0x00, 0x00, 0x34, 0x12, 0x00, 0x00, 0xCD, 0xAB], s32);

        byte[] wide24 = [0x01, 0x34, 0x12, 0x02, 0xCD, 0xAB];
        var back16 = new byte[4];
        Assert.Equal(4, PcmFrames.Convert(wide24, 0, 6, 24, back16, 16));
        Assert.Equal(s16, back16);

        byte[] wide32 = [0xAA, 0x01, 0x34, 0x12];
        var to24 = new byte[3];
        Assert.Equal(3, PcmFrames.Convert(wide32, 0, 4, 32, to24, 24));
        Assert.Equal([0x01, 0x34, 0x12], to24);
    }

    [Fact]
    public void Same_depth_conversion_copies_from_the_given_offset()
    {
        byte[] src = [9, 9, 1, 2, 3, 4];
        var dst = new byte[4];
        Assert.Equal(4, PcmFrames.Convert(src, 2, 4, 16, dst, 16));
        Assert.Equal([1, 2, 3, 4], dst);
    }

    // ------------------------------------------------------------------ 80-bit extended floats

    [Theory]
    [InlineData(8000)]
    [InlineData(11025)]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    [InlineData(1)]
    [InlineData(2147483647)]
    public void Sample_rates_survive_the_80_bit_float_round_trip(int rate)
    {
        var b = new byte[10];
        AiffFile.WriteExtended80(b, rate);
        Assert.Equal(rate, AiffFile.ReadExtended80(b));
    }

    [Fact]
    public void Extended_floats_handle_zero_negative_and_non_positive_rates()
    {
        var b = new byte[10];
        b[0] = 0xFF; // dirty buffer: the writer must clear it
        AiffFile.WriteExtended80(b, 0);
        Assert.All(b, x => Assert.Equal(0, x));
        AiffFile.WriteExtended80(b, -5);
        Assert.All(b, x => Assert.Equal(0, x));
        Assert.Equal(0, AiffFile.ReadExtended80(new byte[10]));

        AiffFile.WriteExtended80(b, 44100);
        b[0] |= 0x80; // sign bit
        Assert.Equal(-44100, AiffFile.ReadExtended80(b));
    }

    // ------------------------------------------------------------------ WAV writer + reader

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    [InlineData(2, 24)]
    [InlineData(2, 32)]
    [InlineData(1, 24)]
    public void Wav_written_by_the_writer_reads_back_sample_for_sample(int channels, int bits)
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("out.wav");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteWav(new RampSource(channels, 22050, bits, 1000), fs, bits, -1, default);

        using var wav = WavPcmSource.TryOpen(path)!;
        Assert.Equal((22050, channels, bits, 1000L), (wav.SampleRate, wav.Channels, wav.BitsPerSample, wav.TotalFrames));
        Assert.Equal(RampBytes(channels, bits, 0, 1000), ReadAll(wav));
    }

    [Theory]
    [InlineData(3, 0x07u)]
    [InlineData(4, 0x33u)]
    [InlineData(5, 0x1Fu)]
    [InlineData(6, 0x3Fu)]
    [InlineData(8, 0x63Fu)]
    public void More_than_two_channels_use_the_extensible_header_with_the_right_speaker_mask(int channels, uint mask)
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("multi.wav");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteWav(new RampSource(channels, 48000, 16, 100), fs, 16, -1, default);

        var head = File.ReadAllBytes(path);
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(16)));
        Assert.Equal(0xFFFE, BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(20)));
        Assert.Equal(mask, BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(40)));
        using var wav = WavPcmSource.TryOpen(path)!;
        Assert.Equal((channels, 100L), (wav.Channels, wav.TotalFrames));
        Assert.Equal(RampBytes(channels, 16, 0, 100), ReadAll(wav));
    }

    [Fact]
    public void The_writer_converts_depth_limits_the_length_and_pads_odd_chunks()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("trim.wav");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteWav(new RampSource(1, 8000, 16, 5000), fs, 24, 301, default);   // 16 -> 24 bit, 301 frames only

        using var wav = WavPcmSource.TryOpen(path)!;
        Assert.Equal((24, 301L), (wav.BitsPerSample, wav.TotalFrames));
        var expected16 = RampBytes(1, 16, 0, 301);
        var expected24 = new byte[301 * 3];
        PcmFrames.Convert(expected16, 0, expected16.Length, 16, expected24, 24);
        Assert.Equal(expected24, ReadAll(wav));
        Assert.Equal(0, new FileInfo(path).Length % 2);                                      // RIFF chunks are word aligned
        Assert.Equal((uint)(new FileInfo(path).Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(File.ReadAllBytes(path).AsSpan(4)));
    }

    [Fact]
    public void An_odd_number_of_data_bytes_gets_a_pad_byte_and_the_riff_size_includes_it()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("odd.wav");
        // 3 mono frames of 24-bit audio = 9 data bytes.
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteWav(new RampSource(1, 8000, 24, 3), fs, 24, -1, default);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(44 + 9 + 1, bytes.Length);
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal((uint)(bytes.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
    }

    [Fact]
    public void Writers_stop_promptly_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var wavOut = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => PcmWriter.WriteWav(new RampSource(2, 44100, 16, 1_000_000), wavOut, 16, -1, cts.Token));
        using var aiffOut = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => PcmWriter.WriteAiff(new RampSource(2, 44100, 16, 1_000_000), aiffOut, 16, -1, cts.Token));
    }

    [Fact]
    public void A_source_that_is_already_empty_produces_a_valid_empty_file()
    {
        using var tmp = new TempDir();
        var wavPath = tmp.Combine("empty.wav");
        using (var fs = new FileStream(wavPath, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteWav(new RampSource(2, 8000, 16, 0), fs, 16, -1, default);
        using var wav = WavPcmSource.TryOpen(wavPath)!;
        Assert.Equal(0, wav.TotalFrames);
        Assert.Empty(ReadAll(wav));

        var aiffPath = tmp.Combine("empty.aiff");
        using (var fs = new FileStream(aiffPath, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteAiff(new RampSource(2, 8000, 16, 0), fs, 16, -1, default);
        Assert.True(AiffFile.LooksLikeAiff(aiffPath));
    }

    // ------------------------------------------------------------------ AIFF writer + reader

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    [InlineData(2, 24)]
    [InlineData(2, 32)]
    public void Aiff_written_by_the_writer_reads_back_sample_for_sample(int channels, int bits)
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("out.aiff");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteAiff(new RampSource(channels, 44100, bits, 777), fs, bits, -1, default);

        Assert.True(AiffFile.LooksLikeAiff(path));
        using var aiff = AiffPcmSource.TryOpen(path)!;
        Assert.Equal((44100, channels, bits, 777L), (aiff.SampleRate, aiff.Channels, aiff.BitsPerSample, aiff.TotalFrames));
        Assert.Equal(RampBytes(channels, bits, 0, 777), ReadAll(aiff));
    }

    [Fact]
    public void Aiff_skip_frames_and_partial_reads_stay_frame_aligned()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("skip.aiff");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            PcmWriter.WriteAiff(new RampSource(2, 8000, 16, 100), fs, 16, -1, default);
        using var aiff = AiffPcmSource.TryOpen(path)!;
        aiff.SkipFrames(10);
        var buf = new byte[10]; // room for 2 whole 4-byte frames and a spare 2 bytes
        Assert.Equal(8, aiff.Read(buf, 0, buf.Length));
        Assert.Equal(RampBytes(2, 16, 10, 2), buf[..8]);
        aiff.SkipFrames(1000);                     // past the end: clamps
        Assert.Equal(0, aiff.Read(buf, 0, buf.Length));
        Assert.Equal(0, aiff.Read(new byte[2], 0, 2)); // less than one frame
    }

    private static byte[] Be16(int v) => [(byte)(v >> 8), (byte)v];
    private static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Chunk(string id, byte[] body)
    {
        var list = new List<byte>(Encoding.ASCII.GetBytes(id));
        list.AddRange(Be32((uint)body.Length));
        list.AddRange(body);
        if (body.Length % 2 == 1) list.Add(0);
        return [.. list];
    }

    private static byte[] Comm(int channels, uint frames, int bits, int rate, string? compression)
    {
        var body = new List<byte>();
        body.AddRange(Be16(channels));
        body.AddRange(Be32(frames));
        body.AddRange(Be16(bits));
        var ext = new byte[10];
        AiffFile.WriteExtended80(ext, rate);
        body.AddRange(ext);
        if (compression is not null)
        {
            body.AddRange(Encoding.ASCII.GetBytes(compression));
            body.Add(0); // empty pascal string
            body.Add(0);
        }
        return Chunk("COMM", [.. body]);
    }

    private static byte[] Ssnd(byte[] data, uint offset = 0) => Chunk("SSND", [.. Be32(offset), .. Be32(0), .. data]);

    private static byte[] Form(string type, params byte[][] chunks)
    {
        var body = new List<byte>(Encoding.ASCII.GetBytes(type));
        foreach (var c in chunks) body.AddRange(c);
        return [.. "FORM"u8.ToArray(), .. Be32((uint)body.Count), .. body];
    }

    [Fact]
    public void Eight_bit_aiff_is_widened_to_signed_16_bit_little_endian()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("eight.aiff", Form("AIFF", Comm(1, 3, 8, 8000, null), Ssnd([0x01, 0x7F, 0x80])));
        using var aiff = AiffPcmSource.TryOpen(path)!;
        Assert.Equal(16, aiff.BitsPerSample);
        Assert.Equal([0x00, 0x01, 0x00, 0x7F, 0x00, 0x80], ReadAll(aiff));
    }

    [Fact]
    public void Aifc_sowt_is_little_endian_none_and_twos_are_big_endian_at_every_depth()
    {
        using var tmp = new TempDir();
        byte[] data16 = [0x12, 0x34, 0x56, 0x78];
        var sowt = AiffPcmSource.TryOpen(tmp.WriteFile("a.aifc", Form("AIFC", Comm(1, 2, 16, 8000, "sowt"), Ssnd(data16))))!;
        Assert.Equal(data16, ReadAll(sowt));
        sowt.Dispose();
        var none = AiffPcmSource.TryOpen(tmp.WriteFile("b.aifc", Form("AIFC", Comm(1, 2, 16, 8000, "NONE"), Ssnd(data16))))!;
        Assert.Equal([0x34, 0x12, 0x78, 0x56], ReadAll(none));
        none.Dispose();
        var twos = AiffPcmSource.TryOpen(tmp.WriteFile("c.aifc", Form("AIFC", Comm(1, 2, 16, 8000, "twos"), Ssnd(data16))))!;
        Assert.Equal([0x34, 0x12, 0x78, 0x56], ReadAll(twos));
        twos.Dispose();

        byte[] data24 = [1, 2, 3, 4, 5, 6];
        using var be24 = AiffPcmSource.TryOpen(tmp.WriteFile("d.aiff", Form("AIFF", Comm(1, 2, 24, 8000, null), Ssnd(data24))))!;
        Assert.Equal([3, 2, 1, 6, 5, 4], ReadAll(be24));
        using var le24 = AiffPcmSource.TryOpen(tmp.WriteFile("e.aifc", Form("AIFC", Comm(1, 2, 24, 8000, "sowt"), Ssnd(data24))))!;
        Assert.Equal(data24, ReadAll(le24));

        byte[] data32 = [1, 2, 3, 4, 5, 6, 7, 8];
        using var be32 = AiffPcmSource.TryOpen(tmp.WriteFile("f.aiff", Form("AIFF", Comm(1, 2, 32, 8000, null), Ssnd(data32))))!;
        Assert.Equal([4, 3, 2, 1, 8, 7, 6, 5], ReadAll(be32));
        using var le32 = AiffPcmSource.TryOpen(tmp.WriteFile("g.aifc", Form("AIFC", Comm(1, 2, 32, 8000, "sowt"), Ssnd(data32))))!;
        Assert.Equal(data32, ReadAll(le32));
    }

    [Fact]
    public void Aiff_header_variants_unknown_chunks_the_ssnd_offset_and_oversized_frame_counts()
    {
        using var tmp = new TempDir();
        // Unknown chunk with an odd size (pad byte) before COMM; SSND with a data offset of 2; COMM claims more frames than exist.
        var odd = Chunk("ANNO", [1, 2, 3]);
        var path = tmp.WriteFile("v.aiff", Form("AIFF", odd, Comm(1, 1000, 16, 8000, null), Ssnd([0xAA, 0xBB, 0x11, 0x22, 0x33, 0x44], offset: 2)));
        using var aiff = AiffPcmSource.TryOpen(path)!;
        Assert.Equal(2, aiff.TotalFrames);   // limited to what the file really holds
        Assert.Equal([0x22, 0x11, 0x44, 0x33], ReadAll(aiff));

        // SSND before COMM works too.
        var swapped = tmp.WriteFile("s.aiff", Form("AIFF", Ssnd([0, 1]), Comm(1, 1, 16, 8000, null)));
        Assert.True(AiffFile.LooksLikeAiff(swapped));
    }

    [Theory]
    [InlineData("not a FORM")]
    [InlineData("neither AIFF nor AIFC")]
    [InlineData("COMM too small")]
    [InlineData("AIFC COMM without compression type")]
    [InlineData("AIFC unsupported compression")]
    [InlineData("no SSND")]
    [InlineData("no COMM")]
    [InlineData("12 bits")]
    [InlineData("zero channels")]
    [InlineData("zero rate")]
    [InlineData("truncated header")]
    [InlineData("empty")]
    public void Unsupported_or_damaged_aiff_files_are_rejected(string kind)
    {
        using var tmp = new TempDir();
        var ok = Ssnd([0, 1, 2, 3]);
        byte[] bytes = kind switch
        {
            "not a FORM" => Encoding.ASCII.GetBytes("RIFF....AIFFCOMM"),
            "neither AIFF nor AIFC" => Form("WAVE", Comm(1, 2, 16, 8000, null), ok),
            "COMM too small" => Form("AIFF", Chunk("COMM", new byte[10]), ok),
            "AIFC COMM without compression type" => Form("AIFC", Chunk("COMM", [.. Be16(1), .. Be32(2), .. Be16(16), .. new byte[10]]), ok),
            "AIFC unsupported compression" => Form("AIFC", Comm(1, 2, 16, 8000, "ulaw"), ok),
            "no SSND" => Form("AIFF", Comm(1, 2, 16, 8000, null)),
            "no COMM" => Form("AIFF", ok),
            "12 bits" => Form("AIFF", Comm(1, 2, 12, 8000, null), ok),
            "zero channels" => Form("AIFF", Comm(0, 2, 16, 8000, null), ok),
            "zero rate" => Form("AIFF", Comm(1, 2, 16, 0, null), ok),
            "truncated header" => "FORM\0\0"u8.ToArray(),
            _ => [],
        };
        var path = tmp.WriteFile("bad.aiff", bytes);
        Assert.False(AiffFile.LooksLikeAiff(path), kind);
        Assert.Null(AiffPcmSource.TryOpen(path));
    }

    [Fact]
    public void Aiff_probes_never_throw_for_missing_or_locked_files()
    {
        using var tmp = new TempDir();
        Assert.False(AiffFile.LooksLikeAiff(tmp.Combine("missing.aiff")));
        Assert.Null(AiffPcmSource.TryOpen(tmp.Combine("missing.aiff")));
        var held = tmp.WriteFile("held.aiff", Form("AIFF", Comm(1, 1, 16, 8000, null), Ssnd([0, 1])));
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.False(AiffFile.LooksLikeAiff(held));
        Assert.Null(AiffPcmSource.TryOpen(held));
    }

    // ------------------------------------------------------------------ WAV reader on hand-made files

    private static byte[] Le16(int v) => [(byte)v, (byte)(v >> 8)];
    private static byte[] Le32(uint v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)];

    private static byte[] WavChunk(string id, byte[] body, uint? declaredSize = null)
    {
        var list = new List<byte>(Encoding.ASCII.GetBytes(id));
        list.AddRange(Le32(declaredSize ?? (uint)body.Length));
        list.AddRange(body);
        if (body.Length % 2 == 1 && declaredSize is null) list.Add(0);
        return [.. list];
    }

    private static byte[] Fmt(int tag, int channels, int rate, int bits, int size = 16)
    {
        var body = new List<byte>();
        body.AddRange(Le16(tag));
        body.AddRange(Le16(channels));
        body.AddRange(Le32((uint)rate));
        body.AddRange(Le32((uint)(rate * channels * bits / 8)));
        body.AddRange(Le16(channels * bits / 8));
        body.AddRange(Le16(bits));
        while (body.Count < size) body.Add(0);
        return WavChunk("fmt ", [.. body]);
    }

    private static byte[] Riff(params byte[][] chunks)
    {
        var body = new List<byte>(Encoding.ASCII.GetBytes("WAVE"));
        foreach (var c in chunks) body.AddRange(c);
        return [.. "RIFF"u8.ToArray(), .. Le32((uint)body.Count), .. body];
    }

    [Fact]
    public void Wav_reader_skips_unknown_chunks_including_odd_sized_ones_and_reads_the_data()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.wav", Riff(WavChunk("LIST", [1, 2, 3]), Fmt(1, 1, 8000, 16), WavChunk("fact", [0, 0, 0, 0]), WavChunk("data", [1, 0, 2, 0, 3, 0])));
        using var wav = WavPcmSource.TryOpen(path)!;
        Assert.Equal(3, wav.TotalFrames);
        Assert.Equal([1, 0, 2, 0, 3, 0], ReadAll(wav));
    }

    [Fact]
    public void Wav_data_sizes_of_zero_all_ones_or_beyond_the_file_mean_until_the_end()
    {
        using var tmp = new TempDir();
        byte[] samples = [1, 0, 2, 0, 3, 0, 4, 0];
        foreach (var (name, declared) in new[] { ("zero", 0u), ("ones", 0xFFFFFFFFu), ("big", 1_000_000u) })
        {
            var path = tmp.WriteFile(name + ".wav", Riff(Fmt(1, 1, 8000, 16), WavChunk("data", samples, declared)));
            using var wav = WavPcmSource.TryOpen(path)!;
            Assert.Equal(4, wav.TotalFrames);
            Assert.Equal(samples, ReadAll(wav));
        }
    }

    [Fact]
    public void Wav_partial_trailing_frames_are_ignored_and_reads_smaller_than_a_frame_return_nothing()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("p.wav", Riff(Fmt(1, 2, 8000, 16), WavChunk("data", [1, 0, 2, 0, 3, 0, 4, 0, 5, 0], declaredSize: 10)));
        using var wav = WavPcmSource.TryOpen(path)!;
        Assert.Equal(2, wav.TotalFrames);
        Assert.Equal(0, wav.Read(new byte[8], 0, 3));
        wav.SkipFrames(1);
        Assert.Equal([3, 0, 4, 0], ReadAll(wav));
        wav.SkipFrames(50); // beyond the end
        Assert.Equal(0, wav.Read(new byte[8], 0, 8));
    }

    [Fact]
    public void Wav_extensible_headers_are_accepted_only_for_pcm_subformats()
    {
        using var tmp = new TempDir();
        byte[] ExtFmt(int subFormat)
        {
            var f = new List<byte>();
            f.AddRange(Le16(0xFFFE)); f.AddRange(Le16(2)); f.AddRange(Le32(8000)); f.AddRange(Le32(32000)); f.AddRange(Le16(4)); f.AddRange(Le16(16));
            f.AddRange(Le16(22)); f.AddRange(Le16(16)); f.AddRange(Le32(3)); f.AddRange(Le16(subFormat)); f.AddRange(new byte[14]);
            return WavChunk("fmt ", [.. f]);
        }
        using (var ok = WavPcmSource.TryOpen(tmp.WriteFile("ok.wav", Riff(ExtFmt(1), WavChunk("data", [0, 0, 0, 0])))))
            Assert.NotNull(ok);
        Assert.Null(WavPcmSource.TryOpen(tmp.WriteFile("float.wav", Riff(ExtFmt(3), WavChunk("data", [0, 0, 0, 0])))));
        var shortExt = WavChunk("fmt ", [.. Le16(0xFFFE), .. Le16(2), .. Le32(8000), .. Le32(32000), .. Le16(4), .. Le16(16), .. new byte[2]]);
        Assert.Null(WavPcmSource.TryOpen(tmp.WriteFile("short.wav", Riff(shortExt, WavChunk("data", [0, 0, 0, 0])))));
    }

    [Theory]
    [InlineData("not riff")]
    [InlineData("not wave")]
    [InlineData("too short")]
    [InlineData("float format")]
    [InlineData("fmt too small")]
    [InlineData("fmt too large")]
    [InlineData("8 bit")]
    [InlineData("zero channels")]
    [InlineData("zero rate")]
    [InlineData("data before fmt")]
    [InlineData("no data chunk")]
    [InlineData("only a header")]
    public void Unsupported_wav_files_are_left_to_media_foundation(string kind)
    {
        using var tmp = new TempDir();
        var data = WavChunk("data", [0, 0, 0, 0]);
        byte[] bytes = kind switch
        {
            "not riff" => [.. "RIFX"u8.ToArray(), .. new byte[8]],
            "not wave" => [.. "RIFF"u8.ToArray(), .. Le32(4), .. "AVI "u8.ToArray()],
            "too short" => "RIFF"u8.ToArray(),
            "float format" => Riff(Fmt(3, 1, 8000, 32), data),
            "fmt too small" => Riff(WavChunk("fmt ", new byte[8]), data),
            "fmt too large" => Riff(WavChunk("fmt ", new byte[16], declaredSize: 5000), data),
            "8 bit" => Riff(Fmt(1, 1, 8000, 8), data),
            "zero channels" => Riff(Fmt(1, 0, 8000, 16), data),
            "zero rate" => Riff(Fmt(1, 1, 0, 16), data),
            "data before fmt" => Riff(data, Fmt(1, 1, 8000, 16)),
            "no data chunk" => Riff(Fmt(1, 1, 8000, 16), WavChunk("LIST", [0, 0])),
            _ => Riff(Fmt(1, 1, 8000, 16)),
        };
        Assert.Null(WavPcmSource.TryOpen(tmp.WriteFile("bad.wav", bytes)));
    }

    [Fact]
    public void Wav_probes_never_throw_for_missing_or_locked_files()
    {
        using var tmp = new TempDir();
        Assert.Null(WavPcmSource.TryOpen(tmp.Combine("missing.wav")));
        var held = tmp.WriteFile("held.wav", Riff(Fmt(1, 1, 8000, 16), WavChunk("data", [0, 0])));
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Null(WavPcmSource.TryOpen(held));
    }

    // ------------------------------------------------------------------ MediaCommon plumbing on top of them

    [Fact]
    public void WritePcm_cuts_a_range_and_chooses_wav_or_aiff_and_the_depth()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("in.wav", Fixtures.SineWav(1.0, rate: 8000, bits: 24, channels: 1));
        var info = new MediaInfo { HasAudio = true, AudioChannels = 1, AudioSampleRate = 8000, AudioBits = 24 };

        var wavOut = tmp.Combine("cut.wav");
        MediaCommon.WritePcm(input, info, wavOut, wav: true, startSeconds: 0.25, durationSeconds: 0.5, keepBits: true, default);
        using (var w = WavPcmSource.TryOpen(wavOut)!)
            Assert.Equal((24, 4000L), (w.BitsPerSample, w.TotalFrames));

        var aiffOut = tmp.Combine("cut.aiff");
        MediaCommon.WritePcm(input, info, aiffOut, wav: false, startSeconds: -1, durationSeconds: 0, keepBits: false, default);
        using (var a = AiffPcmSource.TryOpen(aiffOut)!)
            Assert.Equal((24, 8000L), (a.BitsPerSample, a.TotalFrames));       // deeper than 16 bits stays 24
        Assert.True(MediaCommon.IsPcmContainer(wavOut, out var isWav) && isWav);
        Assert.True(MediaCommon.IsPcmContainer(aiffOut, out isWav) && !isWav);
        Assert.False(MediaCommon.IsPcmContainer(tmp.WriteFile("x.txt", "hello"), out isWav));
    }

    [Fact]
    public async Task Aiff_input_is_staged_as_a_temporary_wav_for_media_foundation_and_cleaned_up_afterwards()
    {
        using var tmp = new TempDir();
        var aiff = tmp.WriteFile("in.aiff", Fixtures.SineAiff(0.2, rate: 8000, bits: 16, channels: 1));
        var info = new MediaInfo { HasAudio = true, AudioChannels = 1, AudioSampleRate = 8000, AudioBits = 16 };
        string? seen = null;
        await MediaCommon.WithTranscodableSource(aiff, info, default, staged =>
        {
            seen = staged;
            Assert.EndsWith(".wav", staged);
            Assert.NotEqual(aiff, staged);
            using var wav = WavPcmSource.TryOpen(staged)!;
            Assert.Equal(1600, wav.TotalFrames);
            return Task.CompletedTask;
        });
        Assert.NotNull(seen);
        Assert.False(File.Exists(seen));

        // Non-AIFF input is passed through untouched.
        var wavPath = tmp.WriteFile("in.wav", Fixtures.SineWav(0.1));
        await MediaCommon.WithTranscodableSource(wavPath, info, default, staged =>
        {
            Assert.Equal(wavPath, staged);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_locked_aiff_is_passed_through_unstaged_because_it_cannot_be_recognised()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("in.aiff", Fixtures.SineAiff(0.1, rate: 8000, bits: 16, channels: 1));
        var info = new MediaInfo { HasAudio = true };
        using var hold = new FileStream(good, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // LooksLikeAiff cannot open it either, so the body just receives the original path.
        var called = false;
        await MediaCommon.WithTranscodableSource(good, info, default, p => { called = true; Assert.Equal(good, p); return Task.CompletedTask; });
        Assert.True(called);
    }
}
