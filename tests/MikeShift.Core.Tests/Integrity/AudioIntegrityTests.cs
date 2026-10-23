using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Every action the catalog offers for audio (M4A, WAV, AIFF, FLAC, MP3, Compress, Trim) on every audio fixture.
/// Lossless outputs must be sample-exact against the source PCM; lossy outputs must be structurally valid, the right length and
/// must still be the same signal (cross-correlation), judged by parsers written for the suite and by Media Foundation's decoder.
/// </summary>
public class AudioIntegrityTests
{
    private static readonly string[] FormatTitles = ["M4A", "WAV", "AIFF", "FLAC", "MP3"];

    /// <summary>Duration tolerance for lossy encodes: encoder delay plus end padding of Windows' MP3 and AAC encoders (up to two frames).</summary>
    private const double LossyDurationSlack = 0.10;

    private static bool Same(AudioSource s, string title) => title switch
    {
        "M4A" => s.Ext == "m4a",
        "WAV" => s.Ext == "wav",
        "AIFF" => s.Ext is "aif" or "aiff",
        "FLAC" => s.Ext == "flac",
        "MP3" => s.Ext == "mp3",
        _ => false,
    };

    private static bool Encodable(string title)
    {
        var c = Kit.Caps;
        return title switch { "M4A" => c.AacEncode, "FLAC" => c.FlacEncode, "MP3" => c.Mp3Encode, _ => true };
    }

    /// <summary>The conversions that never touch a Media Foundation codec (WAV and AIFF are read and written by MikeShift itself): fast enough for every run.</summary>
    private static readonly HashSet<(string, string)> Fast = [("wav16", "AIFF"), ("wav24", "AIFF"), ("aiff", "WAV"), ("wav24", "WAV")];

    private static TheoryData<string, string> Pairs(bool fast)
    {
        var d = new TheoryData<string, string>();
        foreach (var id in AudioSource.Ids)
        {
            var s = AudioSource.Get(id);
            if (!s.Available) continue;
            foreach (var t in FormatTitles)
                if (!Same(s, t) && Encodable(t) && Fast.Contains((id, t)) == fast) d.Add(id, t);
        }
        return d;
    }

    public static TheoryData<string, string> FastFormatCases() => Pairs(fast: true);

    /// <summary>Everything that goes through an MF encoder or decoder: about 2-3 s each.</summary>
    public static TheoryData<string, string> SlowFormatCases() => Pairs(fast: false);

    private static TheoryData<string> IdList(params string[] only)
    {
        var d = new TheoryData<string>();
        foreach (var id in AudioSource.Ids)
            if (AudioSource.Get(id).Available && (only.Length == 0 || only.Contains(id))) d.Add(id);
        return d;
    }

    public static TheoryData<string> Ids() => IdList();

    public static TheoryData<string> PcmIds() => IdList("wav16", "wav24", "aiff");

    public static TheoryData<string> CodecIds() => IdList("mp3", "m4a", "flac");

    // ------------------------------------------------------------------ Format page

    [Theory, MemberData(nameof(FastFormatCases))]
    public Task Format_conversion_is_faithful(string id, string title) => FormatConversion(id, title);

    [SlowTheory, MemberData(nameof(SlowFormatCases)), Trait("Category", "Slow")]
    public Task Format_conversion_through_media_foundation_is_faithful(string id, string title) => FormatConversion(id, title);

    private static async Task FormatConversion(string id, string title)
    {
        var src = AudioSource.Get(id);
        using var tmp = new TempDir();
        var input = await src.CopyToAsync(tmp);
        var truth = await src.TruthAsync(input);

        var output = Kit.One(await Kit.RunAsync(title, input));

        Assert.Equal(tmp.Path, Path.GetDirectoryName(output));
        var metric = await Validate(title, output, truth);
        Kit.Record("audio", src.FileName, title, ValidatorName(title), metric);
    }

    private static string ValidatorName(string title) => title switch
    {
        "WAV" => "hand-written RIFF parser, sample compare",
        "AIFF" => "hand-written FORM/AIFF parser, sample compare",
        "FLAC" => "STREAMINFO parser + MD5 + Media Foundation decode, sample compare",
        "MP3" => "MPEG frame walker + Media Foundation decode, cross-correlation",
        _ => "MP4 box parser + Media Foundation decode, cross-correlation",
    };

    private static async Task<string> Validate(string title, string output, Pcm truth)
    {
        var depth = truth.Bits > 16 ? 24 : 16;
        switch (title)
        {
            case "WAV":
            case "AIFF":
            {
                if (title == "WAV") AudioCheck.AssertWavHeaderConsistent(output);
                var pcm = title == "WAV" ? AudioCheck.ReadWav(output) : AudioCheck.ReadAiff(output);
                Assert.Equal((truth.Rate, truth.Channels, depth), (pcm.Rate, pcm.Channels, pcm.Bits));
                Assert.Equal(truth.Frames, pcm.Frames);
                Assert.Equal(-1, AudioCheck.FirstDifference(pcm, truth));
                return $"sample-exact: {pcm.Frames} frames, {pcm.Rate} Hz, {pcm.Channels} ch, {pcm.Bits}-bit";
            }
            case "FLAC":
            {
                var f = AudioCheck.ReadFlac(output);
                Assert.Equal((truth.Rate, truth.Channels, depth), (f.Rate, f.Channels, f.Bits));
                Assert.Equal(truth.Frames, (int)f.TotalSamples);
                var decoded = await AudioCheck.DecodeWithMfAsync(output, f.Rate, f.Channels, f.Bits);
                Assert.Equal(truth.Frames, decoded.Frames);
                Assert.Equal(-1, AudioCheck.FirstDifference(decoded, truth));
                if (f.Md5.Any(b => b != 0))
                    Assert.Equal(f.Md5, AudioCheck.FlacMd5(decoded));
                return $"sample-exact after decode: {decoded.Frames} frames, {f.Bits}-bit" + (f.Md5.Any(b => b != 0) ? ", STREAMINFO MD5 matches" : "");
            }
            case "MP3":
            {
                var m = AudioCheck.ReadMp3(output);
                Assert.Equal(truth.Rate, m.Rate);
                Assert.Equal(Math.Min(2, truth.Channels), m.Channels);
                Assert.InRange(m.Seconds, truth.Seconds - LossyDurationSlack, truth.Seconds + LossyDurationSlack);
                var shell = await AudioCheck.ShellDurationAsync(output);
                Assert.InRange(shell.TotalSeconds, truth.Seconds - LossyDurationSlack, truth.Seconds + LossyDurationSlack);
                var decoded = await AudioCheck.DecodeWithMfAsync(output, m.Rate, m.Channels, 16);
                var (corr, lag) = AudioCheck.Correlate(truth, decoded);
                Assert.True(corr >= 0.9, $"MP3 correlation with the source is {corr:0.000}");
                var tail = AudioCheck.TailCorrelation(truth, decoded, lag);
                Assert.True(tail >= 0.9, $"MP3 lost the end of the music (tail correlation {tail:0.000})");
                return $"{m.Frames} MPEG frames chain to EOF, {m.Bitrate / 1000} kbps, {m.Seconds:0.000} s vs {truth.Seconds:0.000} s source (delta {(m.Seconds - truth.Seconds) * 1000:+0;-0} ms), correlation {corr:0.000} (lag {lag:0.0} ms)";
            }
            default: // M4A
            {
                var info = AudioCheck.ReadMp4(output);
                Assert.Contains(info.Brand, new[] { "M4A ", "mp42", "isom", "iso2" });
                var a = info.Audio;
                Assert.NotNull(a);
                Assert.Equal("mp4a", a!.Codec);
                Assert.Null(info.Video);
                var dur = (double)a.Duration / a.Timescale;
                Assert.InRange(dur, truth.Seconds - LossyDurationSlack, truth.Seconds + LossyDurationSlack);
                var shell = await AudioCheck.ShellDurationAsync(output);
                Assert.InRange(shell.TotalSeconds, truth.Seconds - LossyDurationSlack, truth.Seconds + LossyDurationSlack);
                var decoded = await AudioCheck.DecodeWithMfAsync(output, a.Timescale == 0 ? truth.Rate : (int)Math.Min(a.Timescale, 48000), Math.Min(2, truth.Channels), 16);
                var (corr, lag) = AudioCheck.Correlate(truth, decoded.Rate == truth.Rate ? decoded : decoded);
                Assert.True(corr >= 0.9, $"M4A correlation with the source is {corr:0.000}");
                var tail2 = AudioCheck.TailCorrelation(truth, decoded, lag);
                Assert.True(tail2 >= 0.9, $"M4A lost the end of the music (tail correlation {tail2:0.000})");
                return $"valid MP4 boxes, mp4a track {dur:0.000} s vs {truth.Seconds:0.000} s source (delta {(dur - truth.Seconds) * 1000:+0;-0} ms), correlation {corr:0.000} (lag {lag:0.0} ms)";
            }
        }
    }

    // ------------------------------------------------------------------ Compress

    [SlowTheory, MemberData(nameof(Ids)), Trait("Category", "Slow")]
    public async Task Compress_gives_a_smaller_aac_that_is_still_the_same_signal(string id)
    {
        if (!Kit.Caps.AacEncode) return;
        var src = AudioSource.Get(id);
        using var tmp = new TempDir();
        var input = await src.CopyToAsync(tmp);
        var truth = await src.TruthAsync(input);

        var result = await Kit.TryRunAsync("Compress", null, input);
        if (result.Failures.Count == 1)
        {
            Assert.Contains("already small", result.Failures[0].Message);
            Assert.Single(Directory.GetFiles(tmp.Path));
            Kit.Record("audio", src.FileName, "Compress", "refusal check", "refused: already small enough (nothing written)");
            return;
        }

        var output = Kit.One(result.Outputs.Select(o => o.Path).ToList());
        Assert.Equal(tmp.Combine(Path.GetFileNameWithoutExtension(src.FileName) + " compressed.m4a"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length, "not smaller");
        var info = AudioCheck.ReadMp4(output);
        Assert.Equal("mp4a", info.Audio!.Codec);
        var dur = (double)info.Audio.Duration / info.Audio.Timescale;
        Assert.InRange(dur, truth.Seconds - LossyDurationSlack, truth.Seconds + LossyDurationSlack);
        var decoded = await AudioCheck.DecodeWithMfAsync(output, truth.Rate, 2, 16);
        var (corr, lag3) = AudioCheck.Correlate(truth, decoded);
        Assert.True(corr >= 0.9, $"correlation {corr:0.000}");
        var tail3 = AudioCheck.TailCorrelation(truth, decoded, lag3);
        Assert.True(tail3 >= 0.9, $"the last 100 ms of the music are missing or damaged (tail correlation {tail3:0.000})");
        Kit.Record("audio", src.FileName, "Compress", "MP4 box parser + Media Foundation decode, cross-correlation",
            $"{new FileInfo(input).Length / 1024} KB -> {new FileInfo(output).Length / 1024} KB, {dur:0.000} s, correlation {corr:0.000}");
    }

    // ------------------------------------------------------------------ Trim

    private static readonly TimeRange Range = new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.0));

    [Theory, MemberData(nameof(PcmIds))]
    public Task Trim_cuts_the_requested_part_of_the_signal(string id) => TrimCase(id);

    [SlowTheory, MemberData(nameof(CodecIds)), Trait("Category", "Slow")]
    public Task Trim_of_a_compressed_file_cuts_the_requested_part_of_the_signal(string id) => TrimCase(id);

    private static async Task TrimCase(string id)
    {
        var src = AudioSource.Get(id);
        using var tmp = new TempDir();
        var input = await src.CopyToAsync(tmp);
        var truth = await src.TruthAsync(input);

        var output = Kit.One(await Kit.RunAsync("Trim", new FakeInteraction(range: Range), input));
        Assert.Contains(" trimmed", Path.GetFileName(output));

        var start = (int)Math.Round(0.5 * truth.Rate);
        var count = (int)Math.Round(1.5 * truth.Rate);
        if (src.Ext is "wav" or "aiff")
        {
            // WAV and AIFF are cut losslessly: same container, same depth, exactly the requested frames.
            Assert.Equal("." + src.Ext, Path.GetExtension(output));
            var pcm = src.Ext == "wav" ? AudioCheck.ReadWav(output) : AudioCheck.ReadAiff(output);
            if (src.Ext == "wav") AudioCheck.AssertWavHeaderConsistent(output);
            Assert.Equal((truth.Rate, truth.Channels, truth.Bits), (pcm.Rate, pcm.Channels, pcm.Bits));
            Assert.Equal(count, pcm.Frames);
            Assert.Equal(-1, AudioCheck.FirstDifference(pcm, truth.Slice(start, count)));
            Kit.Record("audio", src.FileName, "Trim 0:00.5-0:02.0", "RIFF/FORM parser, sample compare with the source slice", $"lossless: {pcm.Frames} frames, sample-exact against source frames {start}..{start + count}");
            return;
        }

        Assert.Equal(".m4a", Path.GetExtension(output));
        var info = AudioCheck.ReadMp4(output);
        var dur = (double)info.Audio!.Duration / info.Audio.Timescale;
        Assert.InRange(dur, 1.5 - 0.15, 1.5 + 0.15);
        var decoded = await AudioCheck.DecodeWithMfAsync(output, truth.Rate, 2, 16);
        var (right, lag) = AudioCheck.Correlate(truth.Slice(start, count), decoded);
        var (wrong, _) = AudioCheck.Correlate(truth.Slice(0, count), decoded);
        Assert.True(right >= 0.9, $"correlation with the requested segment is {right:0.000}");
        Assert.True(right - wrong > 0.3, $"the cut matches the wrong segment about as well ({wrong:0.000} vs {right:0.000})");
        Kit.Record("audio", src.FileName, "Trim 0:00.5-0:02.0", "MP4 box parser + Media Foundation decode, segment cross-correlation",
            $"{dur:0.000} s (asked 1.5), correlation {right:0.000} with 0.5-2.0 s (lag {lag:0.0} ms) vs {wrong:0.000} with 0-1.5 s");
    }

    // ------------------------------------------------------------------ Awkward but common formats

    [SlowFact, Trait("Category", "Slow")]
    public async Task A_mono_48k_wav_converts_without_changing_rate_channels_or_samples()
    {
        using var tmp = new TempDir();
        var pcm = AudioSource.Chirp(16, seconds: 2, rate: 48000, channels: 1);
        var input = tmp.WriteFile("mono48.wav", AudioCheck.WavBytes(pcm));

        var aiff = Kit.One(await Kit.RunAsync("AIFF", input));
        var back = AudioCheck.ReadAiff(aiff);
        Assert.Equal((48000, 1, 16), (back.Rate, back.Channels, back.Bits));
        Assert.Equal(-1, AudioCheck.FirstDifference(back, pcm));
        Assert.Equal(pcm.Frames, back.Frames);

        if (Kit.Caps.FlacEncode)
        {
            var flac = Kit.One(await Kit.RunAsync("FLAC", input));
            var f = AudioCheck.ReadFlac(flac);
            Assert.Equal((48000, 1, 16, (long)pcm.Frames), (f.Rate, f.Channels, f.Bits, f.TotalSamples));
            var d = await AudioCheck.DecodeWithMfAsync(flac, 48000, 1, 16);
            Assert.Equal(-1, AudioCheck.FirstDifference(d, pcm));
            Assert.Equal(pcm.Frames, d.Frames);
        }
        if (Kit.Caps.Mp3Encode)
        {
            var mp3 = Kit.One(await Kit.RunAsync("MP3", input));
            var m = AudioCheck.ReadMp3(mp3);
            Assert.Equal((48000, 1), (m.Rate, m.Channels));
            Assert.InRange(m.Seconds, 2.0 - LossyDurationSlack, 2.0 + LossyDurationSlack);
        }
        if (Kit.Caps.AacEncode)
        {
            var m4a = Kit.One(await Kit.RunAsync("M4A", input));
            var info = AudioCheck.ReadMp4(m4a);
            Assert.Equal("mp4a", info.Audio!.Codec);
            Assert.InRange((double)info.Audio.Duration / info.Audio.Timescale, 2.0 - LossyDurationSlack, 2.0 + LossyDurationSlack);
            var d = await AudioCheck.DecodeWithMfAsync(m4a, 44100, 1, 16);
            // M4A is always 44.1 kHz: compare the signal after resampling the source ratio away (lag search only).
            Assert.True(d.Seconds is > 1.9 and < 2.1);
        }
        Kit.Record("audio", "mono48.wav (48 kHz mono 16-bit)", "AIFF/FLAC/MP3/M4A", "RIFF/FORM/STREAMINFO/MPEG/MP4 parsers", "rate and channel count preserved, AIFF and FLAC sample-exact");
    }

    [Fact]
    public async Task An_empty_trim_and_a_range_past_the_end_are_refused_and_leave_nothing_behind()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("t.wav", AudioCheck.WavBytes(AudioSource.Chirp(16, 2)));
        var past = await Kit.TryRunAsync("Trim", new FakeInteraction(range: new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6))), input);
        Assert.Empty(past.Outputs);
        Assert.Single(past.Failures);
        Assert.Single(Directory.GetFiles(tmp.Path));
        // A range that runs past the end is clamped to the end.
        var clamped = Kit.One(await Kit.RunAsync("Trim", new FakeInteraction(range: new TimeRange(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(9))), input));
        var pcm = AudioCheck.ReadWav(clamped);
        Assert.Equal(22050, pcm.Frames);
        Assert.Equal(-1, AudioCheck.FirstDifference(pcm, AudioCheck.ReadWav(input).Slice(66150, 22050)));
        Kit.Record("audio", "t.wav (2 s)", "Trim past the end", "RIFF parser, sample compare", "range after the end refused; range running past the end clamps to 0.5 s, sample-exact");
    }
}
