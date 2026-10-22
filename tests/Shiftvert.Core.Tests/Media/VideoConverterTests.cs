using Shiftvert.Core.Actions;
using Shiftvert.Core.Media;
using static Shiftvert.Core.Tests.Media.MediaTestSupport;

namespace Shiftvert.Core.Tests.Media;

public class VideoConverterTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    // ---- every test below needs the generated H.264/AAC MP4, hence the capability gate

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Mp4_to_M4a_extracts_the_audio_track()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var output = await VideoConverter.ExtractAudioAsync(mp4, AudioFormat.M4a, NoCancel);

        Assert.Equal(tmp.Combine("clip.m4a"), output);
        var info = Info(output);
        Assert.True(info.HasAudio);
        Assert.False(info.HasVideo);
        Assert.True(info.IsAac);
        Assert.InRange(info.Duration.TotalSeconds, 1.8, 2.3);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode), nameof(Capabilities.Mp3Encode))]
    public async Task Mp4_to_Mp3_extracts_the_audio_track()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var output = await VideoConverter.ExtractAudioAsync(mp4, AudioFormat.Mp3, NoCancel);

        Assert.Equal(tmp.Combine("clip.mp3"), output);
        var info = Info(output);
        Assert.False(info.HasVideo);
        Assert.InRange(info.Duration.TotalSeconds, 1.8, 2.4);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Extracting_audio_from_a_silent_video_says_so()
    {
        using var tmp = new TempDir();
        var silent = CopySilentMp4(tmp);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => VideoConverter.ExtractAudioAsync(silent, AudioFormat.M4a, NoCancel));

        Assert.Equal("This video has no audio.", ex.Message);
        Assert.Equal(["silent.mp4"], FileNames(tmp.Path));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Mp4_to_Gif_is_a_looping_ten_fps_animation_within_480_px()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var output = await VideoConverter.ToFormatAsync(mp4, VideoFormat.Gif, NoCancel);

        Assert.Equal(tmp.Combine("clip.gif"), output);
        var gif = ParseGif(File.ReadAllBytes(output));
        Assert.Equal(480, Math.Max(gif.Width, gif.Height)); // 640x360 -> 480x270
        Assert.Equal(270, Math.Min(gif.Width, gif.Height));
        Assert.True(gif.HasNetscapeLoop, "missing NETSCAPE2.0 loop extension");
        Assert.Equal(0, gif.LoopCount); // 0 = loop forever
        Assert.InRange(gif.Delays.Count, 18, 22); // 2 s at 10 fps
        Assert.All(gif.Delays, d => Assert.Equal(10, d)); // 10 cs = 10 fps

        // Windows' own GIF decoder agrees (this validates the palette and the LZW stream, not just the structure).
        var decoded = await DecodeGifWithWindows(output, x: 10, y: gif.Height - 10);
        Assert.Equal(gif.Delays.Count, decoded.Frames);
        Assert.Equal(gif.Width, decoded.Width);
        Assert.Equal(gif.Height, decoded.Height);
        // Bottom-left of the first frame: red ~ 0, green ~ 250, blue = 60 (the generated gradient).
        Assert.InRange(decoded.Pixel.R, 0, 32);
        Assert.InRange(decoded.Pixel.G, 215, 255);
        Assert.InRange(decoded.Pixel.B, 40, 90);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Gif_stops_after_fifteen_seconds()
    {
        using var tmp = new TempDir();
        var long20 = Fixtures.WriteMp4(tmp.Combine("long.mp4"), seconds: 20, withAudio: false);

        var output = await VideoConverter.ToFormatAsync(long20, VideoFormat.Gif, NoCancel);

        var gif = ParseGif(File.ReadAllBytes(output));
        Assert.Equal(150, gif.Delays.Count);
        Assert.Equal(1500, gif.Delays.Sum()); // 15.00 s
        Assert.True(gif.HasNetscapeLoop);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Mp4_to_Wmv_and_M4v_and_Mp4_reencode()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var wmv = await VideoConverter.ToFormatAsync(mp4, VideoFormat.Wmv, NoCancel);
        var m4v = await VideoConverter.ToFormatAsync(mp4, VideoFormat.M4v, NoCancel);
        var again = await VideoConverter.ToFormatAsync(mp4, VideoFormat.Mp4, NoCancel);

        Assert.Equal(tmp.Combine("clip.wmv"), wmv);
        Assert.Equal(tmp.Combine("clip.m4v"), m4v);
        Assert.Equal(tmp.Combine("clip 2.mp4"), again); // never overwrites the original

        foreach (var path in new[] { wmv, m4v, again })
        {
            var info = Info(path);
            Assert.True(info.HasVideo, path);
            Assert.True(info.HasAudio, path);
            Assert.Equal(640, info.Width);
            Assert.Equal(360, info.Height);
            Assert.InRange(info.Duration.TotalSeconds, 1.8, 2.4);
        }
        Assert.True(Info(m4v).IsH264);
        Assert.True(Info(m4v).IsAac);
        Assert.True(Info(again).IsH264);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Compress_720p_is_smaller_than_the_original_or_refused()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        try
        {
            var output = await VideoConverter.CompressAsync(mp4, NoCancel);
            Assert.Equal(tmp.Combine("clip compressed.mp4"), output);
            Assert.True(new FileInfo(output).Length < new FileInfo(mp4).Length);
            var info = Info(output);
            Assert.True(info.IsH264);
            Assert.True(info.Height <= 720);
        }
        catch (ConversionException ex)
        {
            Assert.Contains("small enough", ex.Message);
            Assert.Equal(["clip.mp4"], FileNames(tmp.Path));
        }
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Compress_scales_a_larger_video_down_to_720p()
    {
        using var tmp = new TempDir();
        var info0 = Info(CopyMp4(tmp));
        // Re-render the source at 1080p so there is something to shrink.
        var big = tmp.Combine("big.mp4");
        var profile = MediaTranscode.Mp4(info0 with { Width = 1920, Height = 1080 }, 1080, 0.3);
        await MediaTranscode.RunAsync(tmp.Combine("clip.mp4"), big, profile, null, "MP4", NoCancel);
        Assert.Equal(1080u, (uint)Info(big).Height);
        using var guard = OriginalsGuard.Watch(big);

        var output = await VideoConverter.CompressAsync(big, NoCancel);

        var info = Info(output);
        Assert.Equal(720, info.Height);
        Assert.Equal(1280, info.Width);
        Assert.True(new FileInfo(output).Length < new FileInfo(big).Length);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Trim_keeps_about_one_second_between_0_5_and_1_5()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var output = await VideoConverter.TrimAsync(mp4, new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5)), NoCancel);

        Assert.Equal(tmp.Combine("clip trimmed.mp4"), output);
        var info = Info(output);
        Assert.InRange(info.Duration.TotalSeconds, 0.85, 1.15);
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Equal(640, info.Width);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Trim_rejects_empty_and_out_of_range_selections()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);

        await Assert.ThrowsAsync<ConversionException>(() =>
            VideoConverter.TrimAsync(mp4, new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), NoCancel));
        await Assert.ThrowsAsync<ConversionException>(() =>
            VideoConverter.TrimAsync(mp4, new TimeRange(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31)), NoCancel));
        Assert.Equal(["clip.mp4"], FileNames(tmp.Path));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Mkv_to_Mp4_when_Windows_can_decode_the_mkv()
    {
        using var tmp = new TempDir();
        // MP4 bytes under an .mkv name: Media Foundation sniffs content, so this is "decodable" only on machines whose
        // MKV handler accepts it. Either way the converter must not crash or leave a partial file behind.
        var mkv = tmp.Combine("movie.mkv");
        File.Copy(CopyMp4(tmp, "seed.mp4"), mkv);
        if (!MediaProbe.CanOpen(mkv)) return; // MKV not decodable here: nothing to assert

        var output = await VideoConverter.ToFormatAsync(mkv, VideoFormat.Mp4, NoCancel);

        Assert.Equal(tmp.Combine("movie.mp4"), output);
        Assert.True(Info(output).IsH264);
    }

    [Fact]
    public async Task A_file_with_no_video_is_refused_clearly()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => VideoConverter.ToFormatAsync(wav, VideoFormat.Mp4, NoCancel));
        Assert.Equal("This file has no video.", ex.Message);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Cancelling_leaves_no_output_and_no_temp_files()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoConverter.ToFormatAsync(mp4, VideoFormat.Wmv, cts.Token));

        Assert.Equal(["clip.mp4"], FileNames(tmp.Path));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Cancelling_mid_conversion_removes_the_temp_file()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        try
        {
            await VideoConverter.ToFormatAsync(mp4, VideoFormat.Gif, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected unless the machine finished the GIF in under 40 ms
        }

        Assert.DoesNotContain(Directory.GetFiles(tmp.Path, "*", SearchOption.TopDirectoryOnly), f => f.EndsWith(".shiftvert-tmp", StringComparison.Ordinal));
    }
}
