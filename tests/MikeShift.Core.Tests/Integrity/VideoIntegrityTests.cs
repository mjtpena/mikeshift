using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Every action the catalog offers for video (MP4, M4V, WMV, GIF, M4A, MP3, Compress, Trim). Judged by: the suite's MP4 box and GIF
/// block parsers, Windows' video-editing stack (a separate Media Foundation client) for duration, size and frame grabs, and a
/// Media Foundation decode of the audio compared with the source audio by cross-correlation.
/// All of it transcodes real video, so these are Slow tests (the whole file takes a minute or two).
/// </summary>
[Trait("Category", "Slow")]
public class VideoIntegrityTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "mikeshift-tests", "video-integrity-" + Environment.ProcessId);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? s_mp4, s_silent, s_wmv;

    private static bool CanMakeVideo => Kit.Caps.H264Encode && Kit.Caps.AacEncode;

    static VideoIntegrityTests() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(Dir, true); } catch (Exception) { /* temp */ } };

    private static async Task<string> MasterAsync(string kind)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Dir);
            switch (kind)
            {
                case "mp4":
                    if (s_mp4 is null) { s_mp4 = Path.Combine(Dir, "clip.mp4"); await VideoCheck.WriteMp4Async(s_mp4); }
                    return s_mp4;
                case "silent":
                    if (s_silent is null) { s_silent = Path.Combine(Dir, "silent.mp4"); await VideoCheck.WriteMp4Async(s_silent, withAudio: false); }
                    return s_silent;
                default:
                    if (s_wmv is null)
                    {
                        var mp4 = await MasterAsyncUnlocked("mp4");
                        s_wmv = Path.Combine(Dir, "clip.wmv");
                        await VideoCheck.WriteWmvAsync(mp4, s_wmv);
                    }
                    return s_wmv;
            }
        }
        finally { Gate.Release(); }
    }

    private static async Task<string> MasterAsyncUnlocked(string kind)
    {
        if (s_mp4 is null) { s_mp4 = Path.Combine(Dir, "clip.mp4"); await VideoCheck.WriteMp4Async(s_mp4); }
        return s_mp4;
    }

    private static async Task<string> CopyAsync(TempDir tmp, string kind)
    {
        var master = await MasterAsync(kind);
        var dest = tmp.Combine(kind == "mp4" ? "clip.mp4" : kind == "silent" ? "silent.mp4" : "clip.wmv");
        File.Copy(master, dest);
        return dest;
    }

    public static TheoryData<string, string> Cases()
    {
        var d = new TheoryData<string, string>();
        if (!CanMakeVideo) return d;
        foreach (var src in new[] { "mp4", "wmv" })
            foreach (var t in new[] { "MP4", "M4V", "WMV", "GIF", "M4A", "MP3" })
            {
                if ((src == "mp4" && t == "MP4") || (src == "wmv" && t == "WMV")) continue; // hidden: already that format
                d.Add(src, t);
            }
        return d;
    }

    // ------------------------------------------------------------------ fixtures

    [SlowFact]
    public async Task The_source_fixtures_are_what_the_tests_assume()
    {
        if (!CanMakeVideo) return;
        foreach (var kind in new[] { "mp4", "wmv" })
        {
            var path = await MasterAsync(kind);
            var facts = await VideoCheck.FactsAsync(path);
            Assert.InRange(facts.Duration.TotalSeconds, 1.9, 2.2);
            Assert.Equal((640, 360), (facts.Width, facts.Height));
            var frames = VideoCheck.Decode(path);
            foreach (var t in new[] { 0.25, 0.75, 1.25, 1.75 }) // the middle of each 0.5 s picture
            {
                var psnr = PixMetrics.Psnr(VideoCheck.ExpectedFrame(t), VideoCheck.At(frames, t));
                Assert.True(psnr >= 22, $"{kind} at {t}s: the fixture frame does not look like the picture that was encoded ({psnr:0.0} dB)");
            }
        }
        var mp4 = AudioCheck.ReadMp4(await MasterAsync("mp4"));
        Assert.Equal("avc1", mp4.Video!.Codec);
        Assert.Equal("mp4a", mp4.Audio!.Codec);
        Kit.Record("video", "clip.mp4 / clip.wmv (2 s, 640x360)", "(source)", "MP4 box parser + sequential Media Foundation frame decode", "avc1 + mp4a, frames match the encoded pictures");
    }

    // ------------------------------------------------------------------ Format page

    [SlowTheory, MemberData(nameof(Cases))]
    public async Task Format_conversion_is_faithful(string source, string title)
    {
        using var tmp = new TempDir();
        var input = await CopyAsync(tmp, source);
        var srcFacts = await VideoCheck.FactsAsync(input);

        var output = Kit.One(await Kit.RunAsync(title, input));

        Assert.Equal(tmp.Path, Path.GetDirectoryName(output));
        string metric;
        switch (title)
        {
            case "MP4":
            case "M4V":
                metric = await AssertMp4(output, input, srcFacts, expectAudio: true);
                break;
            case "WMV":
                Assert.True(VideoCheck.IsAsf(output), "not an ASF container");
                metric = await AssertPictures(output, input, srcFacts) + "; ASF header";
                break;
            case "GIF":
                metric = AssertGif(output, srcFacts);
                break;
            case "M4A":
                metric = await AssertExtractedAudio(output, input, mp3: false);
                break;
            default:
                metric = await AssertExtractedAudio(output, input, mp3: true);
                break;
        }
        Kit.Record("video", Path.GetFileName(input), title,
            title is "GIF" ? "GIF block parser + SkiaSharp frame decode" : title is "M4A" or "MP3" ? "container parser + Media Foundation decode, cross-correlation" : "MP4/ASF parser + sequential Media Foundation frame decode",
            metric);
    }

    /// <summary>Valid MP4 boxes, an avc1 video track and an mp4a audio track, right size and duration, first/middle/last frame like the source.</summary>
    private static async Task<string> AssertMp4(string output, string input, VideoCheck.Facts src, bool expectAudio)
    {
        var info = AudioCheck.ReadMp4(output);
        Assert.Contains(info.Brand, new[] { "mp42", "isom", "iso2", "M4V ", "avc1", "mp41" });
        Assert.NotNull(info.Video);
        Assert.Equal("avc1", info.Video!.Codec);
        Assert.Equal((src.Width, src.Height), (info.Video.Width, info.Video.Height));
        Assert.InRange(info.MovieSeconds, src.Duration.TotalSeconds - 0.15, src.Duration.TotalSeconds + 0.15);
        if (expectAudio)
        {
            Assert.NotNull(info.Audio);
            Assert.Equal("mp4a", info.Audio!.Codec);
        }
        var pictures = await AssertPictures(output, input, src);
        return $"valid boxes, avc1 {info.Video.Width}x{info.Video.Height}" + (expectAudio ? " + mp4a" : "") + $", {info.MovieSeconds:0.000} s vs {src.Duration.TotalSeconds:0.000} s; {pictures}";
    }

    /// <summary>Windows opens it, reports the same size and duration (+-0.15 s) and the first, middle and last frames resemble the source's (>= 25 dB).</summary>
    private static async Task<string> AssertPictures(string output, string input, VideoCheck.Facts src, double offset = 0, double? length = null)
    {
        var facts = await VideoCheck.FactsAsync(output);
        var expectLen = length ?? src.Duration.TotalSeconds;
        Assert.Equal((src.Width, src.Height), (facts.Width, facts.Height));
        Assert.InRange(facts.Duration.TotalSeconds, expectLen - 0.15, expectLen + 0.15);
        var worst = double.PositiveInfinity;
        var outFrames = VideoCheck.Decode(output);
        var srcFrames = VideoCheck.Decode(input);
        Assert.InRange(outFrames[^1].Time.TotalSeconds - outFrames[0].Time.TotalSeconds, expectLen - 0.2, expectLen + 0.1);
        // Early, two in the middle and late in the clip, each well inside one of the four pictures.
        foreach (var fraction in new[] { 0.12, 0.38, 0.62, 0.9 })
        {
            var f = fraction * expectLen;
            var p = PixMetrics.Psnr(VideoCheck.At(srcFrames, f + offset), VideoCheck.At(outFrames, f));
            worst = Math.Min(worst, p);
            Assert.True(p >= 25, $"frame at {f:0.00}s is {p:0.0} dB from the source frame at {f + offset:0.00}s");
        }
        return $"{facts.Duration.TotalSeconds:0.000} s, {outFrames.Count} frames decoded, 4 sampled frames >= {worst:0.0} dB";
    }

    private static string AssertGif(string output, VideoCheck.Facts src)
    {
        Assert.Equal("gif", ImageBytes.Sniff(output));
        var gif = VideoCheck.ReadGif(output);
        Assert.True(Math.Max(gif.Width, gif.Height) <= 480, $"GIF is {gif.Width}x{gif.Height}");
        Assert.Equal(480, Math.Max(gif.Width, gif.Height)); // a 640x360 source is scaled down to 480 on the long side
        Assert.Equal(Math.Round(src.Width / (double)src.Height * gif.Height), gif.Width, 1);
        Assert.True(gif.HasLoopExtension && gif.LoopCount == 0, "GIF does not loop forever");
        // 10 fps for the first 15 s: about 20 frames for 2 s.
        Assert.InRange(gif.Frames.Count, 18, 22);
        Assert.Equal(gif.Frames.Count, gif.DelaysCs.Count);
        var totalSeconds = gif.DelaysCs.Sum() / 100.0;
        Assert.InRange(totalSeconds, src.Duration.TotalSeconds - 0.25, src.Duration.TotalSeconds + 0.25);
        var worst = double.PositiveInfinity;
        foreach (var t in new[] { 0.25, 0.75, 1.25, 1.75 }) // the middle of each 0.5 s picture
        {
            var idx = (int)Math.Min(gif.Frames.Count - 1, t / src.Duration.TotalSeconds * gif.Frames.Count);
            var want = VideoCheck.ExpectedFrame(t, gif.Width, gif.Height);
            var p = PixMetrics.Psnr(want, gif.Frames[idx]);
            worst = Math.Min(worst, p);
            Assert.True(p >= 25, $"GIF frame {idx} is {p:0.0} dB from the source picture at {t:0.00}s");
        }
        return $"{gif.Frames.Count} frames at {gif.Width}x{gif.Height}, {totalSeconds:0.00} s, loops forever, 4 sampled frames >= {worst:0.0} dB";
    }

    private static async Task<string> AssertExtractedAudio(string output, string input, bool mp3)
    {
        var truth = await AudioCheck.DecodeWithMfAsync(input, 44100, 2, 16);
        Assert.True(truth.Seconds > 1.5);
        double seconds;
        string what;
        if (mp3)
        {
            var m = AudioCheck.ReadMp3(output);
            seconds = m.Seconds;
            what = $"{m.Frames} MPEG frames, {m.Bitrate / 1000} kbps";
        }
        else
        {
            var m = AudioCheck.ReadMp4(output);
            Assert.Null(m.Video);
            Assert.Equal("mp4a", m.Audio!.Codec);
            seconds = (double)m.Audio.Duration / m.Audio.Timescale;
            what = "mp4a track only";
        }
        Assert.InRange(seconds, truth.Seconds - 0.1, truth.Seconds + 0.1);
        var decoded = await AudioCheck.DecodeWithMfAsync(output, 44100, 2, 16);
        var (corr, lag) = AudioCheck.Correlate(truth, decoded);
        Assert.True(corr >= 0.9, $"correlation with the video's own audio is {corr:0.000}");
        var tail = AudioCheck.TailCorrelation(truth, decoded, lag);
        Assert.True(tail >= 0.9, $"the end of the audio is missing (tail correlation {tail:0.000})");
        return $"{what}, {seconds:0.000} s vs {truth.Seconds:0.000} s, correlation {corr:0.000}, last 100 ms {tail:0.000}";
    }

    [SlowFact]
    public async Task Extracting_audio_from_a_silent_video_is_refused_cleanly()
    {
        if (!CanMakeVideo) return;
        using var tmp = new TempDir();
        var input = await CopyAsync(tmp, "silent");
        foreach (var title in new[] { "M4A", "MP3" })
        {
            if (!Kit.Offered(title, input)) continue;
            var r = await Kit.TryRunAsync(title, null, input);
            Assert.Empty(r.Outputs);
            Assert.Contains("no audio", r.Failures.Single().Message);
        }
        Assert.Single(Directory.GetFiles(tmp.Path));
        Kit.Record("video", "silent.mp4", "M4A / MP3", "refusal check", "refused: no audio (nothing written)");
    }

    // ------------------------------------------------------------------ Tool page

    [SlowTheory]
    [InlineData("mp4")]
    [InlineData("wmv")]
    public async Task Compress_gives_a_smaller_playable_mp4(string source)
    {
        if (!CanMakeVideo) return;
        using var tmp = new TempDir();
        var input = await CopyAsync(tmp, source);
        var facts = await VideoCheck.FactsAsync(input);

        var result = await Kit.TryRunAsync("Compress", null, input);
        if (result.Failures.Count == 1)
        {
            Assert.Contains("already small", result.Failures[0].Message);
            Assert.Single(Directory.GetFiles(tmp.Path));
            Kit.Record("video", Path.GetFileName(input), "Compress", "refusal check", "refused: already small enough (nothing written)");
            return;
        }
        var output = Kit.One(result.Outputs.Select(o => o.Path).ToList());
        Assert.EndsWith(" compressed.mp4", output);
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length, "not smaller");
        var metric = await AssertMp4(output, input, facts, expectAudio: true);
        Kit.Record("video", Path.GetFileName(input), "Compress", "MP4 box parser + sequential Media Foundation frame decode",
            $"{new FileInfo(input).Length / 1024} KB -> {new FileInfo(output).Length / 1024} KB; {metric}");
    }

    [SlowTheory]
    [InlineData("mp4")]
    [InlineData("wmv")]
    public async Task Trim_keeps_exactly_the_requested_part(string source)
    {
        if (!CanMakeVideo) return;
        using var tmp = new TempDir();
        var input = await CopyAsync(tmp, source);
        var facts = await VideoCheck.FactsAsync(input);

        var output = Kit.One(await Kit.RunAsync("Trim", new FakeInteraction(range: new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5))), input));

        Assert.Equal(source == "wmv" ? ".wmv" : ".mp4", Path.GetExtension(output));
        Assert.Contains(" trimmed", output);
        // Duration 1.0 s +-0.15; picture at output time t is the source picture at t + 0.5 s (and not the one at t).
        var outFacts = await VideoCheck.FactsAsync(output);
        Assert.InRange(outFacts.Duration.TotalSeconds, 1.0 - 0.15, 1.0 + 0.15);
        Assert.Equal((facts.Width, facts.Height), (outFacts.Width, outFacts.Height));
        var srcFrames = VideoCheck.Decode(input);
        var outFrames = VideoCheck.Decode(output);
        // The source is red, green, blue, yellow in 0.5 s steps; the cut starts in green and ends in blue.
        foreach (var t in new[] { 0.2, 0.7 })
        {
            var got = VideoCheck.At(outFrames, t);
            var right = PixMetrics.Psnr(VideoCheck.At(srcFrames, t + 0.5), got);
            var wrong = PixMetrics.Psnr(VideoCheck.At(srcFrames, t), got);
            Assert.True(right >= 25, $"trimmed frame at {t}s is {right:0.0} dB from the source frame at {t + 0.5}s");
            Assert.True(right > wrong + 3, $"the trimmed frame at {t}s looks like the untrimmed one ({wrong:0.0} dB) rather than the right one ({right:0.0} dB)");
        }

        // Audio follows the picture: the cut audio is the chirp from 0.5 s to 1.5 s.
        var truth = await AudioCheck.DecodeWithMfAsync(input, 44100, 2, 16);
        var decoded = await AudioCheck.DecodeWithMfAsync(output, 44100, 2, 16);
        var start = 22050;
        var (right2, _) = AudioCheck.Correlate(truth.Slice(start, 44100), decoded);
        var (wrong2, _) = AudioCheck.Correlate(truth.Slice(0, 44100), decoded);
        Assert.True(right2 >= 0.9, $"audio correlation with 0.5-1.5 s is {right2:0.000}");
        Assert.True(right2 - wrong2 > 0.3, $"trimmed audio also matches the wrong segment ({wrong2:0.000})");

        Kit.Record("video", Path.GetFileName(input), "Trim 0:00.5-0:01.5", "sequential Media Foundation frame decode + audio decode",
            $"{outFacts.Duration.TotalSeconds:0.000} s (asked 1.0), picture and audio come from the requested segment (audio correlation {right2:0.000} vs {wrong2:0.000} for the wrong one)");
    }

    [SlowFact]
    public async Task MP4_to_M4V_is_a_lossless_rewrap()
    {
        if (!CanMakeVideo) return;
        using var tmp = new TempDir();
        var master = await CopyAsync(tmp, "mp4");
        var input = tmp.Combine("clean.mp4");
        await VideoCheck.WriteCleanMp4Async(master, input);
        var output = Kit.One(await Kit.RunAsync("M4V", input));
        var a = AudioCheck.ReadMp4(input);
        var b = AudioCheck.ReadMp4(output);
        // Same codecs, same size, same sample counts, same duration: the encoded pictures and sound were not touched.
        Assert.Equal((a.Video!.Codec, a.Video.Width, a.Video.Height, a.Video.Samples), (b.Video!.Codec, b.Video.Width, b.Video.Height, b.Video.Samples));
        Assert.Equal((a.Audio!.Codec, a.Audio.Samples), (b.Audio!.Codec, b.Audio.Samples));
        Assert.InRange(b.MovieSeconds, a.MovieSeconds - 0.05, a.MovieSeconds + 0.05);
        Assert.InRange(new FileInfo(output).Length, new FileInfo(input).Length * 0.8, new FileInfo(input).Length * 1.2);
        // And the pictures are the same ones.
        var fa = VideoCheck.Decode(input);
        var fb = VideoCheck.Decode(output);
        Assert.Equal(fa.Count, fb.Count);
        for (var i = 0; i < fa.Count; i += 7)
            Assert.True(PixMetrics.Psnr(fa[i].Frame, fb[i].Frame) >= 45, $"frame {i} differs after the rewrap");
        Kit.Record("video", "clean.mp4", "M4V (rewrap)", "MP4 box parser + sequential Media Foundation frame decode", $"same avc1/mp4a tracks and sample counts ({a.Video.Samples} video, {a.Audio.Samples} audio), identical pictures (>= 45 dB), size within 20%");

        // A file whose video samples repeat a time stamp (stills exported by Photos / Movie Maker) cannot be copied by the MP4 sink
        // without losing samples; that must fall back to a full transcode and still give the right pictures.
        var dup = Kit.One(await Kit.RunAsync("M4V", master));
        var src = VideoCheck.Decode(master);
        var got = VideoCheck.Decode(dup);
        foreach (var t in new[] { 0.25, 0.75, 1.25, 1.75 })
            Assert.True(PixMetrics.Psnr(VideoCheck.At(src, t), VideoCheck.At(got, t)) >= 25, $"M4V picture at {t}s is wrong for a source with repeated time stamps");
        Kit.Record("video", "clip.mp4 (repeated video time stamps)", "M4V", "sequential Media Foundation frame decode", "falls back to a full transcode; pictures correct at 4 sampled times");
    }
}
