using Shiftvert.Core;
using Shiftvert.Core.Media;

namespace Shiftvert.Cli.Tests;

/// <summary>Audio and video end to end through the real <c>shiftvert.exe</c> (child process).</summary>
public class MediaCliTests
{
    private static readonly Lazy<string> s_masterMp4 = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "shiftvert-tests", "cli-media-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(dir, true); } catch (Exception) { } };
        return Fixtures.WriteMp4(Path.Combine(dir, "master.mp4"));
    });

    private static string CopyMp4(TempDir tmp, string name = "clip.mp4")
    {
        var dest = tmp.Combine(name);
        File.Copy(s_masterMp4.Value, dest);
        return dest;
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public void Trim_range_on_an_mp4_keeps_about_one_second()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        using var guard = OriginalsGuard.Watch(mp4);

        var r = CliProcess.Run("--run", "Trim", "--range", "0:00.5-0:01.5", mp4);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("clip trimmed.mp4"), output);
        var info = MediaInfo.TryRead(output);
        Assert.NotNull(info);
        Assert.InRange(info.Duration.TotalSeconds, 0.85, 1.15);
        Assert.True(info.HasVideo);
    }

    [Fact]
    public void Aiff_from_a_wav_prints_the_new_file_and_leaves_the_original_alone()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav());
        using var guard = OriginalsGuard.Watch(wav);

        var r = CliProcess.Run("--run", "AIFF", wav);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("tone.aiff"), output);
        Assert.Equal("FORM", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
    }

    [Fact]
    public void List_for_a_wav_offers_the_other_audio_formats_but_not_wav()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav());

        var r = CliProcess.Run("--list", wav);

        Assert.Equal(0, r.ExitCode);
        var titles = r.Stdout.Trim().Split(", ");
        Assert.Contains("AIFF", titles);
        Assert.Contains("Trim", titles);
        Assert.Contains("Zip", titles);
        Assert.DoesNotContain("WAV", titles);
    }

    [Fact]
    public void Trim_range_on_a_wav_is_a_lossless_cut()
    {
        using var tmp = new TempDir();
        var wav = tmp.WriteFile("tone.wav", Fixtures.SineWav(2));

        var r = CliProcess.Run("--run", "Trim", "--range", "0:00.5-0:01", wav);

        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("tone trimmed.wav"), output);
        Assert.Equal(22050 * 4, Fixtures.WavPayload(File.ReadAllBytes(output)).Length);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public void Gif_from_an_mp4_is_written_beside_it()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);

        var r = CliProcess.Run("--run", "GIF", mp4);

        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("clip.gif"), output);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 6));
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public void Extracting_audio_from_a_silent_video_fails_with_a_clear_message()
    {
        using var tmp = new TempDir();
        var silent = tmp.Combine("silent.mp4");
        Fixtures.WriteMp4(silent, withAudio: false);

        var r = CliProcess.Run("--run", "M4A", silent);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Stdout);
        Assert.Contains("This video has no audio", r.Stderr);
    }
}
