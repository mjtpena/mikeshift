using MikeShift.Core.Actions;
using MikeShift.Core.Media;
using Windows.Win32;

namespace MikeShift.Core.Tests.Media;

/// <summary>Sizing and bitrate maths, trim-range validation, input checks, the Media Foundation helper layer and the failure paths of the encoders.</summary>
public class MediaHelpersCoverageTests
{
    // ------------------------------------------------------------------ sizes and bitrates

    [Theory]
    [InlineData(0.0, 2u)]
    [InlineData(1.0, 2u)]
    [InlineData(3.0, 2u)]
    [InlineData(719.9, 718u)]
    [InlineData(720.0, 720u)]
    [InlineData(1921.0, 1920u)]
    public void Even_rounds_down_to_an_even_size_of_at_least_two(double value, uint expected) =>
        Assert.Equal(expected, MediaTranscode.Even(value));

    [Theory]
    [InlineData(1920, 1080, 720, 1280u, 720u)]    // downscaled, aspect kept
    [InlineData(1280, 720, 720, 1280u, 720u)]     // already fits
    [InlineData(640, 480, 720, 640u, 480u)]       // never upscaled
    [InlineData(641, 481, 720, 640u, 480u)]       // odd sizes made even
    [InlineData(0, 0, 720, 1280u, 720u)]          // unknown size: a safe default
    [InlineData(-5, 100, 720, 1280u, 720u)]
    [InlineData(3840, 2160, 1080, 1920u, 1080u)]
    [InlineData(100, 5000, 720, 14u, 720u)]       // extreme portrait
    public void Video_is_fitted_to_the_row_budget_with_even_dimensions(int w, int h, int maxHeight, uint ew, uint eh) =>
        Assert.Equal((ew, eh), MediaTranscode.FitHeight(w, h, maxHeight));

    [Theory]
    [InlineData(1280u, 720u, 30.0, 0.15, 4_147_200u)]
    [InlineData(16u, 16u, 1.0, 0.1, 400_000u)]                    // clamped up
    [InlineData(7680u, 4320u, 60.0, 0.4, 25_000_000u)]            // clamped down
    public void Video_bitrate_is_pixels_times_rate_times_density_within_sane_limits(uint w, uint h, double fps, double bpp, uint expected) =>
        Assert.Equal(expected, MediaTranscode.VideoBitrate(w, h, fps, bpp));

    // ------------------------------------------------------------------ encoding profiles

    [Fact]
    public void Mp4_profile_takes_the_source_size_rate_and_drops_audio_when_there_is_none()
    {
        var info = new MediaInfo { HasVideo = true, Width = 1920, Height = 1080, FrameRate = 29.97, HasAudio = false };
        var p = MediaTranscode.Mp4(info, 720, 0.1);
        Assert.Equal((1280u, 720u), (p.Video.Width, p.Video.Height));
        Assert.Equal((30000u, 1001u), (p.Video.FrameRate.Numerator, p.Video.FrameRate.Denominator));
        Assert.Null(p.Audio);
        Assert.Equal(MediaTranscode.VideoBitrate(1280, 720, 29.97, 0.1), p.Video.Bitrate);

        var withAudio = MediaTranscode.Mp4(info with { HasAudio = true }, 720, 0.1);
        Assert.NotNull(withAudio.Audio);
    }

    [Theory]
    [InlineData(23.976, 24000u, 1001u)]
    [InlineData(29.97, 30000u, 1001u)]
    [InlineData(59.94, 60000u, 1001u)]
    [InlineData(25.0, 25000u, 1000u)]
    [InlineData(60.0, 60000u, 1000u)]
    [InlineData(144.0, 30000u, 1000u)]    // implausible rates fall back to 30
    [InlineData(0.0, 30000u, 1000u)]
    public void Frame_rates_keep_the_common_ntsc_ratios_exact(double fps, uint num, uint den)
    {
        var p = MediaTranscode.Mp4(new MediaInfo { HasVideo = true, Width = 640, Height = 360, FrameRate = fps }, 720, 0.1);
        Assert.Equal((num, den), (p.Video.FrameRate.Numerator, p.Video.FrameRate.Denominator));
    }

    [Fact]
    public void A_video_bitrate_cap_lowers_the_target_but_never_below_300_kbps()
    {
        var info = new MediaInfo { HasVideo = true, Width = 1280, Height = 720, FrameRate = 30 };
        Assert.Equal(1_000_000u, MediaTranscode.Mp4(info, 720, 0.15, maxVideoBitrate: 1_000_000).Video.Bitrate);
        Assert.Equal(300_000u, MediaTranscode.Mp4(info, 720, 0.15, maxVideoBitrate: 10).Video.Bitrate);
        Assert.Equal(MediaTranscode.VideoBitrate(1280, 720, 30, 0.15), MediaTranscode.Mp4(info, 720, 0.15, maxVideoBitrate: 100_000_000).Video.Bitrate);
    }

    [Fact]
    public void Wmv_profile_follows_the_same_sizing_rules()
    {
        var info = new MediaInfo { HasVideo = true, Width = 1920, Height = 1080, FrameRate = 24, HasAudio = false };
        var p = MediaTranscode.Wmv(info, 1080, 0.12);
        Assert.Equal((1920u, 1080u), (p.Video.Width, p.Video.Height));
        Assert.Null(p.Audio);
        Assert.NotNull(MediaTranscode.Wmv(info with { HasAudio = true }, 1080, 0.12).Audio);
    }

    [Fact]
    public void Audio_profiles_apply_only_the_settings_that_were_asked_for()
    {
        var m4a = MediaTranscode.M4a(128_000, 44100, 2);
        Assert.Equal((128_000u, 44100u, 2u, "AAC"), (m4a.Audio.Bitrate, m4a.Audio.SampleRate, m4a.Audio.ChannelCount, m4a.Audio.Subtype));
        var m4aDefault = MediaTranscode.M4a(0, 0, 0);
        Assert.Equal("AAC", m4aDefault.Audio.Subtype);
        Assert.NotEqual(0u, m4aDefault.Audio.SampleRate);

        var mp3 = MediaTranscode.Mp3(48000, 1);
        Assert.Equal((48000u, 1u), (mp3.Audio.SampleRate, mp3.Audio.ChannelCount));
        Assert.NotEqual(0u, MediaTranscode.Mp3().Audio.SampleRate);

        var flac = MediaTranscode.Flac(96000, 2, 24);
        Assert.Equal((96000u, 2u, 24u), (flac.Audio.SampleRate, flac.Audio.ChannelCount, flac.Audio.BitsPerSample));
        Assert.NotEqual(0u, MediaTranscode.Flac(0, 0, 0).Audio.SampleRate);
    }

    // ------------------------------------------------------------------ trim ranges

    private static TimeRange R(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public void Empty_or_negative_ranges_are_refused(double start, double end)
    {
        var ex = Assert.Throws<ConversionException>(() => MediaCommon.ValidateRange(R(start, end), TimeSpan.FromSeconds(60)));
        Assert.Equal("The trim range is empty.", ex.Message);
    }

    [Fact]
    public void A_range_starting_after_the_end_is_refused_and_one_ending_after_it_is_clamped()
    {
        var late = Assert.Throws<ConversionException>(() => MediaCommon.ValidateRange(R(60, 70), TimeSpan.FromSeconds(60)));
        Assert.Equal("The trim range starts after the end of the file.", late.Message);
        Assert.Equal(R(10, 60), MediaCommon.ValidateRange(R(10, 90), TimeSpan.FromSeconds(60)));
        Assert.Equal(R(10, 20), MediaCommon.ValidateRange(R(10, 20), TimeSpan.FromSeconds(60)));
        // An unknown duration cannot be checked against: the range is trusted.
        Assert.Equal(R(100, 200), MediaCommon.ValidateRange(R(100, 200), TimeSpan.Zero));
    }

    // ------------------------------------------------------------------ input checks

    [Fact]
    public async Task Missing_unreadable_and_audio_only_inputs_are_reported_in_plain_language()
    {
        using var tmp = new TempDir();
        var missing = await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.ReadInfoAsync(tmp.Combine("gone.mp4"), default));
        Assert.Equal("The file could not be found.", missing.Message);

        var garbage = await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.ReadInfoAsync(tmp.WriteFile("junk.mp4", Fixtures.Noise(2000)), default));
        Assert.Equal("Windows can't open this file.", garbage.Message);

        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav(0.1));
        var noVideo = await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.RequireVideoAsync(wav, default));
        Assert.Equal("This file has no video.", noVideo.Message);
        var info = await MediaCommon.ReadInfoAsync(wav, default);
        Assert.True(info.HasAudio);
        Assert.False(info.HasVideo);
        Assert.Equal(2, info.AudioChannels);
        Assert.InRange(info.Duration.TotalSeconds, 0.09, 0.11);
    }

    [Fact]
    public async Task Extracting_audio_only_offers_m4a_and_mp3_and_needs_an_audio_stream()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav(0.1));
        var wrong = await Assert.ThrowsAsync<ConversionException>(() => VideoConverter.ExtractAudioAsync(wav, (AudioFormat)99, default));
        Assert.Equal("Only M4A and MP3 can be extracted from a video.", wrong.Message);
    }

    [Fact]
    public void Media_info_of_junk_and_missing_files_is_null_and_of_a_wav_describes_the_audio()
    {
        using var tmp = new TempDir();
        Assert.Null(MediaInfo.TryRead(tmp.WriteFile("junk.mp3", Fixtures.Noise(4000))));
        Assert.Null(MediaInfo.TryRead(tmp.Combine("missing.mp4")));
        var info = MediaInfo.TryRead(tmp.WriteFile("tone.wav", Fixtures.SineWav(0.5, rate: 22050, bits: 16, channels: 1)))!;
        Assert.Equal((true, 1, 22050, 16), (info.HasAudio, info.AudioChannels, info.AudioSampleRate, info.AudioBits));
        Assert.False(info.IsH264);
        Assert.False(info.IsAac);
        var aiff = MediaInfo.TryRead(tmp.WriteFile("tone.aiff", Fixtures.SineAiff(0.5, rate: 8000, bits: 24, channels: 2)))!;
        Assert.Equal((true, 2, 8000, 24), (aiff.HasAudio, aiff.AudioChannels, aiff.AudioSampleRate, aiff.AudioBits));
        Assert.InRange(aiff.Duration.TotalSeconds, 0.49, 0.51);
    }

    // ------------------------------------------------------------------ Media Foundation helpers

    [Fact]
    public void Attribute_helpers_round_trip_and_report_missing_keys_without_throwing()
    {
        var attrs = MfInterop.CreateAttributes();
        try
        {
            var key = Guid.NewGuid();
            Assert.False(MfInterop.TryGetUInt32(attrs, key, out var missing32));
            Assert.Equal(0u, missing32);
            Assert.False(MfInterop.TryGetUInt64(attrs, key, out var missing64));
            Assert.Equal(0ul, missing64);
            Assert.False(MfInterop.TryGetGuid(attrs, key, out var missingGuid));
            Assert.Equal(Guid.Empty, missingGuid);

            MfInterop.SetUInt32(attrs, key, 42);
            Assert.True(MfInterop.TryGetUInt32(attrs, key, out var v32));
            Assert.Equal(42u, v32);
            var key64 = Guid.NewGuid();
            MfInterop.SetUInt64(attrs, key64, MfInterop.Pack(640, 480));
            Assert.True(MfInterop.TryGetUInt64(attrs, key64, out var v64));
            Assert.Equal((640u, 480u), ((uint)(v64 >> 32), (uint)v64));
            var keyGuid = Guid.NewGuid();
            MfInterop.SetGuid(attrs, keyGuid, PInvoke.MFMediaType_Audio);
            Assert.True(MfInterop.TryGetGuid(attrs, keyGuid, out var g));
            Assert.Equal(PInvoke.MFMediaType_Audio, g);
            // The wrong type for an existing key is a "not found" as well.
            Assert.False(MfInterop.TryGetUInt64(attrs, key, out _));
        }
        finally
        {
            MfInterop.Release(attrs);
        }
        MfInterop.Release(null);
        MfInterop.Release("not a COM object");
        Assert.NotNull(MfInterop.CreateMediaType());
    }

    [Fact]
    public void Opening_a_reader_on_junk_or_a_missing_file_gives_null_and_on_a_wav_gives_a_duration_and_no_video_type()
    {
        using var tmp = new TempDir();
        Assert.Null(MfInterop.TryOpenReader(tmp.WriteFile("junk.mp4", Fixtures.Noise(3000))));
        Assert.Null(MfInterop.TryOpenReader(tmp.Combine("missing.mp4")));

        var reader = MfInterop.TryOpenReader(tmp.WriteFile("tone.wav", Fixtures.SineWav(0.25)))!;
        try
        {
            Assert.InRange(MfInterop.GetDuration(reader)!.Value.TotalSeconds, 0.24, 0.26);
            Assert.Null(MfInterop.TryGetNativeType(reader, MfInterop.FirstVideoStream));
            var audio = MfInterop.TryGetNativeType(reader, MfInterop.FirstAudioStream);
            Assert.NotNull(audio);
            MfInterop.Release(audio);
        }
        finally
        {
            MfInterop.Release(reader);
        }
    }

    [Fact]
    public void Media_foundation_decodes_a_wav_to_pcm_skipping_and_reading_frame_accurately()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("tone.wav", Fixtures.SineWav(0.5, rate: 16000, bits: 16, channels: 2));
        var info = MediaInfo.TryRead(path)!;
        using var src = MfPcmSource.Open(path, info);
        Assert.Equal((16000, 2, 16, -1L), (src.SampleRate, src.Channels, src.BitsPerSample, src.TotalFrames));

        src.SkipFrames(4000);
        var buf = new byte[4096 * 4];
        var total = 0;
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0) { Assert.Equal(0, n % 4); total += n; }
        Assert.Equal((8000 - 4000) * 4, total);
        src.SkipFrames(10);     // past the end is harmless
        Assert.Equal(0, src.Read(buf, 0, buf.Length));
    }

    [Fact]
    public void Opening_a_pcm_source_needs_audio_and_a_file_media_foundation_can_read()
    {
        using var tmp = new TempDir();
        var noAudio = Assert.Throws<ConversionException>(() => MfPcmSource.Open(tmp.WriteFile("a.wav", Fixtures.SineWav(0.1)), new MediaInfo { HasAudio = false }));
        Assert.Equal("This file has no audio.", noAudio.Message);
        var junk = Assert.Throws<ConversionException>(() => MfPcmSource.Open(tmp.WriteFile("junk.mp3", Fixtures.Noise(100)), new MediaInfo { HasAudio = true, AudioChannels = 2, AudioSampleRate = 44100 }));
        Assert.Equal("Windows can't open this file.", junk.Message);
    }

    [Fact]
    public void Open_pcm_prefers_the_built_in_readers_and_falls_back_to_media_foundation()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("a.wav", Fixtures.SineWav(0.1, rate: 8000, bits: 16, channels: 1));
        var aiff = tmp.WriteFile("a.aiff", Fixtures.SineAiff(0.1, rate: 8000, bits: 16, channels: 1));
        using (var w = MediaCommon.OpenPcm(wav, MediaInfo.TryRead(wav)!)) Assert.Equal(800, w.TotalFrames);
        using (var a = MediaCommon.OpenPcm(aiff, MediaInfo.TryRead(aiff)!)) Assert.Equal(800, a.TotalFrames);
        // An 8-bit WAV is not handled by the built-in reader: Media Foundation decodes it (total length unknown up front).
        var eightBit = tmp.WriteFile("b.wav", EightBitWav());
        using var mf = MediaCommon.OpenPcm(eightBit, MediaInfo.TryRead(eightBit)!);
        Assert.Equal(-1, mf.TotalFrames);
    }

    private static byte[] EightBitWav()
    {
        var data = new byte[800];
        Array.Fill(data, (byte)128);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36u + (uint)data.Length); w.Write("WAVEfmt "u8.ToArray());
        w.Write(16u); w.Write((ushort)1); w.Write((ushort)1); w.Write(8000u); w.Write(8000u); w.Write((ushort)1); w.Write((ushort)8);
        w.Write("data"u8.ToArray()); w.Write((uint)data.Length); w.Write(data);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ probing

    [Fact]
    public void Capability_probing_reports_what_media_foundation_registers()
    {
        Assert.Throws<ArgumentNullException>(() => MediaProbe.ProbeCapabilities(null!));
        var caps = MediaProbe.ProbeCapabilities(Capabilities.None);
        Assert.Equal(MediaProbe.HasMft(PInvoke.MFT_CATEGORY_AUDIO_ENCODER, PInvoke.MFMediaType_Audio, PInvoke.MFAudioFormat_AAC, output: true), caps.AacEncode);
        // A subtype nobody registered is simply absent, for both directions.
        Assert.False(MediaProbe.HasMft(PInvoke.MFT_CATEGORY_AUDIO_ENCODER, PInvoke.MFMediaType_Audio, Guid.NewGuid(), output: true));
        Assert.False(MediaProbe.HasMft(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, Guid.NewGuid(), output: false));
        // Probing keeps whatever the caller already knew.
        Assert.True(MediaProbe.ProbeCapabilities(Capabilities.None with { WordCom = true }).WordCom);
    }

    [Fact]
    public void CanOpen_accepts_pcm_and_aiff_and_rejects_junk_and_missing_files()
    {
        using var tmp = new TempDir();
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("a.wav", Fixtures.SineWav(0.1))));
        Assert.True(MediaProbe.CanOpen(tmp.WriteFile("a.aiff", Fixtures.SineAiff(0.1))));
        Assert.False(MediaProbe.CanOpen(tmp.WriteFile("junk.mp3", Fixtures.Noise(500))));
        Assert.False(MediaProbe.CanOpen(tmp.Combine("missing.mp3")));
    }

    // ------------------------------------------------------------------ stream copy and GIF export

    [Fact]
    public void Stream_copy_is_only_possible_for_h264_with_aac_or_no_audio()
    {
        var h264 = PInvoke.MFVideoFormat_H264;
        var aac = PInvoke.MFAudioFormat_AAC;
        Assert.True(StreamCopy.CanCopy(new MediaInfo { HasVideo = true, VideoSubtype = h264 }));
        Assert.True(StreamCopy.CanCopy(new MediaInfo { HasVideo = true, VideoSubtype = h264, HasAudio = true, AudioSubtype = aac }));
        Assert.False(StreamCopy.CanCopy(new MediaInfo { HasVideo = true, VideoSubtype = h264, HasAudio = true, AudioSubtype = PInvoke.MFAudioFormat_MP3 }));
        Assert.False(StreamCopy.CanCopy(new MediaInfo { HasVideo = true, VideoSubtype = PInvoke.MFVideoFormat_HEVC }));
        Assert.False(StreamCopy.CanCopy(new MediaInfo { HasVideo = false, VideoSubtype = h264 }));
    }

    [Fact]
    public void A_stream_copy_that_cannot_start_returns_false_and_leaves_no_file_behind()
    {
        using var tmp = new TempDir();
        var junk = tmp.WriteFile("junk.mp4", Fixtures.Noise(5000));
        var dest = tmp.WriteFile("out.mp4", "partial");
        var info = new MediaInfo { HasVideo = true, VideoSubtype = PInvoke.MFVideoFormat_H264 };
        Assert.False(StreamCopy.TryRewrapToMp4(junk, info, dest, default));
        Assert.False(File.Exists(dest));
        // Not copyable at all: refuses before touching anything.
        var untouched = tmp.WriteFile("keep.mp4", "keep");
        Assert.False(StreamCopy.TryRewrapToMp4(junk, new MediaInfo { HasVideo = true, VideoSubtype = PInvoke.MFVideoFormat_HEVC }, untouched, default));
        Assert.True(File.Exists(untouched));
    }

    [Fact]
    public void Gif_export_reports_an_audio_only_file_and_unreadable_files()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav(0.1));
        var noVideo = Assert.Throws<ConversionException>(() => GifExporter.Export(wav, tmp.Combine("a.gif"), default));
        Assert.Equal("Windows can't decode the video in this file.", noVideo.Message); // selecting the missing video stream fails inside Media Foundation
        var junk = Assert.Throws<ConversionException>(() => GifExporter.Export(tmp.WriteFile("junk.mp4", Fixtures.Noise(4000)), tmp.Combine("b.gif"), default));
        Assert.Equal("Windows can't open this video.", junk.Message);
    }

    [Fact]
    public void Gif_resampling_averages_blocks_and_honours_bottom_up_frames()
    {
        // A 4x2 BGRX frame: left half black, right half white (top row), the bottom row inverted.
        var src = new byte[4 * 2 * 4];
        void Px(int x, int y, byte v) { var o = (y * 4 + x) * 4; src[o] = src[o + 1] = src[o + 2] = v; src[o + 3] = 255; }
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 4; x++)
                Px(x, y, (byte)(((x >= 2) ^ (y == 1)) ? 255 : 0));

        var topDown = new byte[2 * 1 * 3];
        GifExporter.Resample(src, 4, 2, stride: 16, topDown, 2, 1);
        Assert.Equal(new byte[] { 127, 127, 127, 127, 127, 127 }, topDown);       // each output pixel averages one black and one white column over both rows

        var identity = new byte[4 * 2 * 3];
        GifExporter.Resample(src, 4, 2, stride: 16, identity, 4, 2);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255 }, identity[..12]);
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0 }, identity[12..]);

        var bottomUp = new byte[4 * 2 * 3];
        GifExporter.Resample(src, 4, 2, stride: -16, bottomUp, 4, 2);
        Assert.Equal(identity[12..], bottomUp[..12]);   // rows swapped
        Assert.Equal(identity[..12], bottomUp[12..]);

        // A buffer too short for the requested frame is tolerated (the missing rows come out black).
        var truncated = new byte[4 * 2 * 3];
        GifExporter.Resample(src[..16], 4, 2, stride: 16, truncated, 4, 2);
        Assert.Equal(new byte[12], truncated[12..]);
    }

    // ------------------------------------------------------------------ encoder fallbacks on an unreadable source

    [Fact]
    public async Task Every_audio_encoder_gives_up_with_a_conversion_error_after_trying_its_fallbacks()
    {
        using var tmp = new TempDir();
        var junk = tmp.WriteFile("junk.wav", Fixtures.Noise(4000));
        var info = new MediaInfo { HasAudio = true, AudioChannels = 2, AudioSampleRate = 44100, AudioBits = 16 };

        await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.EncodeAacAsync(junk, tmp.Combine("o.m4a"), info, 256_000, default));
        await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.EncodeAacAsync(junk, tmp.Combine("o2.m4a"), info with { AudioSampleRate = 48000 }, 96_000, default, keepSampleRate: true));
        await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.EncodeMp3Async(junk, tmp.Combine("o.mp3"), info, default));
        await Assert.ThrowsAsync<ConversionException>(() => MediaCommon.EncodeFlacAsync(junk, tmp.Combine("o.flac"), info with { AudioBits = 24 }, default));
    }

    [Fact]
    public async Task Cancelled_encodes_stop_before_starting_the_fallback_ladder()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav(0.1));
        var info = new MediaInfo { HasAudio = true, AudioChannels = 2, AudioSampleRate = 44100, AudioBits = 16 };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaCommon.EncodeAacAsync(wav, tmp.Combine("o.m4a"), info, 128_000, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaTranscode.RunAsync(wav, tmp.Combine("o2.m4a"), MediaTranscode.M4a(0, 0, 0), null, "M4A", cts.Token));
    }

    [Fact]
    public async Task A_transcode_that_media_foundation_rejects_says_which_target_failed_and_leaves_no_output_content()
    {
        using var tmp = new TempDir();
        var junk = tmp.WriteFile("junk.wav", Fixtures.Noise(4000));
        var dest = tmp.Combine("o.mp3");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => MediaTranscode.RunAsync(junk, dest, MediaTranscode.Mp3(), null, "MP3", default));
        Assert.Contains("MP3", ex.Message);
        Assert.True(!File.Exists(dest) || new FileInfo(dest).Length == 0);
    }
}
