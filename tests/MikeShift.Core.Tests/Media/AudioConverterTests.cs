using MikeShift.Core.Actions;
using MikeShift.Core.Media;
using static MikeShift.Core.Tests.Media.MediaTestSupport;

namespace MikeShift.Core.Tests.Media;

public class AudioConverterTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    private static string Wav(TempDir tmp, string name = "tone.wav", int bits = 16, int channels = 2, double seconds = 1, int rate = 44100) =>
        tmp.WriteFile(name, Fixtures.SineWav(seconds, rate, bits, channels));

    // ------------------------------------------------------------ WAV / AIFF (own writers)

    [Fact]
    public async Task Wav_to_Aiff_writes_a_big_endian_FORM_with_the_same_samples()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp);
        using var guard = OriginalsGuard.Watch(wav);

        var output = await AudioConverter.ToFormatAsync(wav, AudioFormat.Aiff, NoCancel);

        Assert.Equal(tmp.Combine("tone.aiff"), output);
        var bytes = File.ReadAllBytes(output);
        Assert.Equal("FORM", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("AIFF", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Contains("COMM", Chunks(bytes));
        Assert.Contains("SSND", Chunks(bytes));
        var info = Info(output);
        Assert.Equal(44100, info.AudioSampleRate);
        Assert.Equal(2, info.AudioChannels);
        Assert.InRange(info.Duration.TotalSeconds, 0.99, 1.01);
        Assert.True(MediaProbe.CanOpen(output));
    }

    [Theory]
    [InlineData(16, 2)]
    [InlineData(24, 2)]
    [InlineData(16, 1)]
    public async Task Wav_to_Aiff_to_Wav_is_bit_exact(int bits, int channels)
    {
        using var tmp = new TempDir();
        var original = Fixtures.SineWav(0.75, 44100, bits, channels);
        var wav = tmp.WriteFile("src.wav", original);

        var aiff = await AudioConverter.ToFormatAsync(wav, AudioFormat.Aiff, NoCancel);
        // Same stem: the round trip lands beside the AIFF with a fresh name.
        var back = await AudioConverter.ToFormatAsync(aiff, AudioFormat.Wav, NoCancel);

        Assert.Equal(tmp.Combine("src 2.wav"), back);
        Assert.Equal(Fixtures.WavPayload(original), Fixtures.WavPayload(File.ReadAllBytes(back)));
        var info = Info(back);
        Assert.Equal(channels, info.AudioChannels);
        Assert.Equal(44100, info.AudioSampleRate);
        Assert.Equal(bits, info.AudioBits);
    }

    [Fact]
    public async Task Wav_output_depth_is_24_bit_for_deeper_sources()
    {
        using var tmp = new TempDir();
        var aiff32 = tmp.WriteFile("deep.aiff", Fixtures.SineAiff(0.5, 48000, 32, 2));
        var wav = await AudioConverter.ToFormatAsync(aiff32, AudioFormat.Wav, NoCancel);
        Assert.Equal(24, Info(wav).AudioBits);
        Assert.Equal(48000, Info(wav).AudioSampleRate);
    }

    [Fact]
    public async Task Aiff_input_works_for_the_media_foundation_targets()
    {
        using var tmp = new TempDir();
        var aiff = tmp.WriteFile("tone.aiff", Fixtures.SineAiff());
        using var guard = OriginalsGuard.Watch(aiff);

        if (Capabilities.Current.AacEncode)
        {
            var m4a = await AudioConverter.ToFormatAsync(aiff, AudioFormat.M4a, NoCancel);
            Assert.InRange(Info(m4a).Duration.TotalSeconds, 0.9, 1.2);
        }
        if (Capabilities.Current.Mp3Encode)
        {
            var mp3 = await AudioConverter.ToFormatAsync(aiff, AudioFormat.Mp3, NoCancel);
            Assert.InRange(Info(mp3).Duration.TotalSeconds, 0.9, 1.3);
        }

    }

    [Fact]
    public async Task Aiff_staging_file_is_a_wav_and_is_deleted_afterwards()
    {
        using var tmp = new TempDir();
        var aiff = tmp.WriteFile("tone.aiff", Fixtures.SineAiff());
        string? staged = null;

        await MediaCommon.WithTranscodableSource(aiff, Info(aiff), NoCancel, path =>
        {
            staged = path;
            Assert.NotEqual(aiff, path);
            Assert.True(File.Exists(path));
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
            return Task.CompletedTask;
        });

        Assert.NotNull(staged);
        Assert.False(File.Exists(staged));

        // Anything Media Foundation reads itself is passed through untouched.
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav());
        await MediaCommon.WithTranscodableSource(wav, Info(wav), NoCancel, path =>
        {
            Assert.Equal(wav, path);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Conversion_never_overwrites_and_uniquifies()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp);
        var first = await AudioConverter.ToFormatAsync(wav, AudioFormat.Aiff, NoCancel);
        var second = await AudioConverter.ToFormatAsync(wav, AudioFormat.Aiff, NoCancel);
        Assert.Equal(tmp.Combine("tone.aiff"), first);
        Assert.Equal(tmp.Combine("tone 2.aiff"), second);
    }

    [Fact]
    public async Task Cancellation_leaves_no_output_behind()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp, seconds: 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AudioConverter.ToFormatAsync(wav, AudioFormat.Aiff, cts.Token));
        Assert.Equal(["tone.wav"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task A_non_audio_file_is_a_clear_error()
    {
        using var tmp = new TempDir();
        var junk = tmp.WriteFile("noise.mp3", Fixtures.Noise(256));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => AudioConverter.ToFormatAsync(junk, AudioFormat.Wav, NoCancel));
        Assert.Contains("can't open", ex.Message);
    }

    // ------------------------------------------------------------ Media Foundation encoders

    [SkipIfNoCapability(nameof(Capabilities.AacEncode))]
    public async Task Wav_to_M4a_is_AAC_at_44_1_kHz()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp);
        using var guard = OriginalsGuard.Watch(wav);

        var output = await AudioConverter.ToFormatAsync(wav, AudioFormat.M4a, NoCancel);

        Assert.Equal(tmp.Combine("tone.m4a"), output);
        var info = Info(output);
        Assert.True(info.IsAac);
        Assert.Equal(44100, info.AudioSampleRate);
        Assert.Equal(2, info.AudioChannels);
        Assert.InRange(info.Duration.TotalSeconds, 0.9, 1.2);
    }

    [SkipIfNoCapability(nameof(Capabilities.FlacEncode))]
    public async Task Wav_to_Flac_decodes_back_to_the_same_samples()
    {
        using var tmp = new TempDir();
        var original = Fixtures.SineWav(1, 44100, 16, 2);
        var wav = tmp.WriteFile("tone.wav", original);

        var flac = await AudioConverter.ToFormatAsync(wav, AudioFormat.Flac, NoCancel);

        Assert.Equal(tmp.Combine("tone.flac"), flac);
        Assert.Equal("fLaC", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(flac), 0, 4));
        var pcm = DecodePcm(flac, out var rate, out var channels, out var bits);
        Assert.Equal(44100, rate);
        Assert.Equal(2, channels);
        Assert.Equal(16, bits);
        // Lossless: the decoded stream equals the source (allow the decoder's last-block padding).
        var expected = Fixtures.WavPayload(original);
        Assert.True(pcm.Length >= expected.Length);
        Assert.True(expected.AsSpan().SequenceEqual(pcm.AsSpan(0, expected.Length)), "FLAC round trip is not bit-exact");
    }

    [SkipIfNoCapability(nameof(Capabilities.FlacEncode))]
    public async Task Wav24_to_Flac_keeps_24_bits()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp, bits: 24);
        var flac = await AudioConverter.ToFormatAsync(wav, AudioFormat.Flac, NoCancel);
        Assert.Equal(24, Info(flac).AudioBits);
    }

    [SkipIfNoCapability(nameof(Capabilities.Mp3Encode))]
    public async Task Wav_to_Mp3_is_a_playable_mp3()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp);
        using var guard = OriginalsGuard.Watch(wav);

        var mp3 = await AudioConverter.ToFormatAsync(wav, AudioFormat.Mp3, NoCancel);

        Assert.Equal(tmp.Combine("tone.mp3"), mp3);
        var info = Info(mp3);
        Assert.InRange(info.Duration.TotalSeconds, 0.9, 1.3);
        Assert.Equal(2, info.AudioChannels);
        Assert.Equal(44100, info.AudioSampleRate); // the source rate is kept
    }

    [SkipIfNoCapability(nameof(Capabilities.Mp3Encode))]
    public async Task Mp3_decodes_to_wav_and_aiff()
    {
        using var tmp = new TempDir();
        var mp3 = await AudioConverter.ToFormatAsync(Wav(tmp, seconds: 1.5), AudioFormat.Mp3, NoCancel);

        var wav = await AudioConverter.ToFormatAsync(mp3, AudioFormat.Wav, NoCancel);
        var aiff = await AudioConverter.ToFormatAsync(mp3, AudioFormat.Aiff, NoCancel);

        Assert.Equal(16, Info(wav).AudioBits);
        Assert.InRange(Info(wav).Duration.TotalSeconds, 1.4, 1.7);
        Assert.Equal(Info(wav).Duration, Info(aiff).Duration);
    }

    // ------------------------------------------------------------ Compress

    [SkipIfNoCapability(nameof(Capabilities.AacEncode))]
    public async Task Compress_makes_a_smaller_m4a_or_says_it_is_small_enough()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp, seconds: 3);
        using var guard = OriginalsGuard.Watch(wav);

        var output = await AudioConverter.CompressAsync(wav, NoCancel);

        Assert.Equal(tmp.Combine("tone compressed.m4a"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(wav).Length);
        Assert.True(Info(output).IsAac);
    }

    [SkipIfNoCapability(nameof(Capabilities.AacEncode))]
    public async Task Compress_refuses_when_the_result_would_not_be_smaller()
    {
        using var tmp = new TempDir();
        // 0.05 s of audio: the AAC container overhead alone outweighs the PCM.
        var tiny = tmp.WriteFile("blip.wav", Fixtures.SineWav(0.05, 8000, 16, 1));
        try
        {
            var output = await AudioConverter.CompressAsync(tiny, NoCancel);
            Assert.True(new FileInfo(output).Length < new FileInfo(tiny).Length);
        }
        catch (ConversionException ex)
        {
            Assert.Contains("small enough", ex.Message);
            Assert.False(File.Exists(tmp.Combine("blip compressed.m4a")));
        }
    }

    // ------------------------------------------------------------ Trim

    [Fact]
    public async Task Trim_wav_is_a_sample_accurate_lossless_cut()
    {
        using var tmp = new TempDir();
        var original = Fixtures.SineWav(2, 44100, 16, 2);
        var wav = tmp.WriteFile("long.wav", original);
        using var guard = OriginalsGuard.Watch(wav);

        var output = await AudioConverter.TrimAsync(wav, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5)), NoCancel);

        Assert.Equal(tmp.Combine("long trimmed.wav"), output);
        var trimmed = Fixtures.WavPayload(File.ReadAllBytes(output));
        var full = Fixtures.WavPayload(original);
        const int frame = 4;
        Assert.Equal(44100 * frame, trimmed.Length);
        Assert.True(full.AsSpan(22050 * frame, 44100 * frame).SequenceEqual(trimmed), "trimmed samples differ from the source slice");
    }

    [Fact]
    public async Task Trim_aiff_stays_aiff_and_lossless()
    {
        using var tmp = new TempDir();
        var aiff = tmp.WriteFile("long.aif", Fixtures.SineAiff(2));
        var full = DecodePcm(aiff, out _, out _, out _);

        var output = await AudioConverter.TrimAsync(aiff, new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.25)), NoCancel);

        Assert.Equal(tmp.Combine("long trimmed.aif"), output);
        var trimmed = DecodePcm(output, out var rate, out _, out _);
        Assert.Equal(44100, rate);
        Assert.Equal(11025 * 4, trimmed.Length);
        Assert.True(full.AsSpan(44100 * 4, 11025 * 4).SequenceEqual(trimmed));
    }

    [Fact]
    public async Task Trim_end_beyond_the_file_is_clamped_and_bad_ranges_are_refused()
    {
        using var tmp = new TempDir();
        var wav = Wav(tmp, seconds: 1);

        var output = await AudioConverter.TrimAsync(wav, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(60)), NoCancel);
        Assert.Equal(22050 * 4, Fixtures.WavPayload(File.ReadAllBytes(output)).Length);

        await Assert.ThrowsAsync<ConversionException>(() =>
            AudioConverter.TrimAsync(wav, new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6)), NoCancel));
        await Assert.ThrowsAsync<ConversionException>(() =>
            AudioConverter.TrimAsync(wav, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5)), NoCancel));
    }

    [SkipIfNoCapability(nameof(Capabilities.AacEncode), nameof(Capabilities.FlacEncode))]
    public async Task Trim_of_a_compressed_source_becomes_an_AAC_m4a_of_the_right_length()
    {
        using var tmp = new TempDir();
        var flac = await AudioConverter.ToFormatAsync(Wav(tmp, seconds: 2), AudioFormat.Flac, NoCancel);

        var output = await AudioConverter.TrimAsync(flac, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5)), NoCancel);

        Assert.Equal(tmp.Combine("tone trimmed.m4a"), output);
        Assert.InRange(Info(output).Duration.TotalSeconds, 0.85, 1.2);
    }

    private static IEnumerable<string> Chunks(byte[] aiff)
    {
        var pos = 12;
        while (pos + 8 <= aiff.Length)
        {
            yield return System.Text.Encoding.ASCII.GetString(aiff, pos, 4);
            var size = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(pos + 4));
            pos += 8 + size + (size & 1);
        }
    }
}
