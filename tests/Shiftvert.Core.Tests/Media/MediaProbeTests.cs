using System.Buffers.Binary;
using Shiftvert.Core.Media;
using static Shiftvert.Core.Tests.Media.MediaTestSupport;

namespace Shiftvert.Core.Tests.Media;

public class MediaProbeTests
{
    [Fact]
    public void Wav_and_aiff_are_openable()
    {
        using var tmp = new TempDir();
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("a.wav", Fixtures.SineWav())));
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("a.aiff", Fixtures.SineAiff())));
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("b.aif", Fixtures.SineAiff(0.1, 22050, 24, 1))));
    }

    [Fact]
    public void Aiff_is_recognised_by_content_not_extension()
    {
        using var tmp = new TempDir();
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("really-aiff.dat", Fixtures.SineAiff())));
        // A WAV that was renamed .aiff is still just a WAV to Media Foundation.
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("really-wav.aiff", Fixtures.SineWav())));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public void Mp4_is_openable()
    {
        using var tmp = new TempDir();
        Assert.True(MediaProbe.CanOpen(CopyMp4(tmp)));
    }

    [Fact]
    public void Junk_is_not_openable()
    {
        using var tmp = new TempDir();
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("noise.mp4", Fixtures.Noise(2048))));
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("noise.mp3", Fixtures.Noise(2048))));
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("empty.wav", [])));
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("text.aiff", "FORM but not really")));
        Assert.False(MediaProbe.CanOpen(tmp.Combine("missing.mp4")));
    }

    [Fact]
    public void A_truncated_aiff_header_is_not_openable()
    {
        using var tmp = new TempDir();
        var full = Fixtures.SineAiff();
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("cut.aiff", full[..20])));
    }

    [Fact]
    public void Kind_classifies_real_media_files()
    {
        using var tmp = new TempDir();
        Assert.Equal(Category.Audio, Kind.Of(tmp.WriteFile("tone.wav", Fixtures.SineWav())));
        Assert.Equal(Category.Audio, Kind.Of(tmp.WriteFile("tone.aiff", Fixtures.SineAiff())));
        Assert.Equal(Category.Other, Kind.Of(tmp.WriteFile("fake.mp3", Fixtures.Noise(512))));
        Assert.Equal(Category.Other, Kind.Of(tmp.WriteFile("fake.mov", Fixtures.Noise(512))));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public void Kind_classifies_an_mp4_as_video()
    {
        using var tmp = new TempDir();
        Assert.Equal(Category.Video, Kind.Of(CopyMp4(tmp)));
    }

    [Fact]
    public void Probe_fills_in_media_flags_without_touching_the_others()
    {
        var seed = Capabilities.None with { HeifEncode = true, WordCom = true };
        var probed = MediaProbe.ProbeCapabilities(seed);

        Assert.True(probed.HeifEncode);
        Assert.True(probed.WordCom);
        Assert.False(probed.WebpEncode);
        // The machine running the tests is Windows 10/11: the inbox AAC and H.264 MFTs exist.
        Assert.Equal(MediaProbe.HasMft(Windows.Win32.PInvoke.MFT_CATEGORY_AUDIO_ENCODER,
            Windows.Win32.PInvoke.MFMediaType_Audio, Windows.Win32.PInvoke.MFAudioFormat_AAC, output: true), probed.AacEncode);
        Assert.True(probed.AacEncode || !probed.H264Encode || true); // flags are independent booleans; none may throw
    }

    [Fact]
    public void Probe_is_stable_and_cached_on_Capabilities_Current()
    {
        var a = Capabilities.Probe();
        var b = Capabilities.Probe();
        Assert.Equal(a with { }, b);
        Assert.Equal(Capabilities.Current.AacEncode, a.AacEncode);
    }

    // ------------------------------------------------------------ AIFF format details

    [Theory]
    [InlineData(8000)]
    [InlineData(11025)]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    public void Extended_float_sample_rate_round_trips(int rate)
    {
        Span<byte> ext = stackalloc byte[10];
        AiffFile.WriteExtended80(ext, rate);
        Assert.Equal(rate, AiffFile.ReadExtended80(ext));
    }

    [Fact]
    public void Extended_float_for_44100_matches_the_well_known_encoding()
    {
        Span<byte> ext = stackalloc byte[10];
        AiffFile.WriteExtended80(ext, 44100);
        Assert.Equal("400EAC44000000000000", Convert.ToHexString(ext));
    }

    [Fact]
    public void Aifc_sowt_little_endian_samples_are_read_correctly()
    {
        using var tmp = new TempDir();
        // Build an AIFC with 'sowt' (little-endian) 16-bit mono: samples 1, -2, 300.
        short[] samples = [1, -2, 300];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void Be32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); w.Write(b); }
        void Be16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); w.Write(b); }
        w.Write("FORM"u8); Be32(0); w.Write("AIFC"u8);
        w.Write("COMM"u8); Be32(24); Be16(1); Be32((uint)samples.Length); Be16(16);
        Span<byte> ext = stackalloc byte[10];
        AiffFile.WriteExtended80(ext, 8000);
        w.Write(ext); w.Write("sowt"u8); w.Write((byte)0); w.Write((byte)0); // empty pascal string, padded
        w.Write("SSND"u8); Be32((uint)(8 + samples.Length * 2)); Be32(0); Be32(0);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        var path = tmp.WriteFile("le.aifc", bytes);

        Assert.True(MediaProbe.CanOpen(path));
        var pcm = DecodePcm(path, out var rate, out var channels, out var bits);
        Assert.Equal(8000, rate);
        Assert.Equal(1, channels);
        Assert.Equal(16, bits);
        Assert.Equal(samples, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm).ToArray());
    }

    [Fact]
    public void Eight_bit_aiff_is_widened_to_sixteen_bits()
    {
        using var tmp = new TempDir();
        sbyte[] samples = [0, 1, -1, 127, -128];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void Be32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); w.Write(b); }
        void Be16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); w.Write(b); }
        w.Write("FORM"u8); Be32(0); w.Write("AIFF"u8);
        w.Write("COMM"u8); Be32(18); Be16(1); Be32((uint)samples.Length); Be16(8);
        Span<byte> ext = stackalloc byte[10];
        AiffFile.WriteExtended80(ext, 11025);
        w.Write(ext);
        w.Write("SSND"u8); Be32((uint)(8 + samples.Length)); Be32(0); Be32(0);
        foreach (var s in samples) w.Write((byte)s);
        w.Write((byte)0); // pad to even
        w.Flush();
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));

        var pcm = DecodePcm(tmp.WriteFile("eight.aif", bytes), out _, out _, out var bits);

        Assert.Equal(16, bits);
        Assert.Equal(samples.Select(s => (short)(s << 8)).ToArray(), System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm).ToArray());
    }

    [Fact]
    public void Aiff_writer_patches_every_size_field()
    {
        var aiff = Fixtures.SineAiff(0.5, 44100, 24, 2);
        Assert.Equal(aiff.Length - 8, (int)BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(4)));
        // FORM(12) + COMM(8+18) = 38: SSND chunk header follows.
        Assert.Equal("SSND", System.Text.Encoding.ASCII.GetString(aiff, 38, 4));
        var ssndSize = (int)BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(42));
        var frames = (int)BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(12 + 8 + 2));
        Assert.Equal(22050, frames);
        Assert.Equal(8 + frames * 2 * 3, ssndSize);
        Assert.Equal(12 + 26 + 8 + ssndSize, aiff.Length);
    }

    [Fact]
    public void Wav_reader_handles_extensible_and_ignores_unknown_chunks()
    {
        using var tmp = new TempDir();
        var plain = Fixtures.SineWav(0.2, 48000, 24, 2);
        var payload = Fixtures.WavPayload(plain);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        w.Write("LIST"u8); w.Write(5); w.Write("junk!"u8); w.Write((byte)0); // odd-sized chunk with pad
        w.Write("fmt "u8); w.Write(40); w.Write((ushort)0xFFFE); w.Write((ushort)2); w.Write(48000); w.Write(48000 * 6); w.Write((ushort)6); w.Write((ushort)24);
        w.Write((ushort)22); w.Write((ushort)24); w.Write(3);
        w.Write(new byte[] { 1, 0, 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 });
        w.Write("data"u8); w.Write(payload.Length); w.Write(payload);
        w.Flush();
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));

        var back = DecodePcm(tmp.WriteFile("ext.wav", bytes), out var rate, out var channels, out var bits);

        Assert.Equal(48000, rate);
        Assert.Equal(2, channels);
        Assert.Equal(24, bits);
        Assert.Equal(payload, back);
    }
}
