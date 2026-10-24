using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Media;
using static MikeShift.Core.Tests.Media.MediaTestSupport;

namespace MikeShift.Core.Tests.Media;

/// <summary>The "GIF length" setting: a GIF made from a video runs at most 5, 10, 15 or 30 seconds (10 frames a second); 15 is what it always was.</summary>
public class GifLengthSettingTests
{
    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task The_gif_stops_at_the_chosen_length_and_a_longer_setting_lets_a_longer_clip_through()
    {
        using var tmp = new TempDir();
        var clip = Fixtures.WriteMp4(tmp.Combine("long.mp4"), seconds: 20, withAudio: false);

        foreach (var (seconds, frames) in new[] { (5, 50), (10, 100), (15, 150), (30, 200) }) // the 20 s clip ends before 30 s
        {
            var output = await VideoConverter.ToFormatAsync(clip, VideoFormat.Gif, new ConversionOptions { GifSeconds = seconds }, default);
            var gif = ParseGif(File.ReadAllBytes(output));
            if (seconds == 30) Assert.InRange(gif.Delays.Count, 190, 205); // the whole 20 s clip, give or take the last frames
            else Assert.Equal(frames, gif.Delays.Count);
            Assert.Equal(gif.Delays.Count * 10, gif.Delays.Sum());
            Assert.True(gif.HasNetscapeLoop);
        }
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task Without_a_setting_the_gif_is_still_fifteen_seconds_and_the_tile_passes_the_setting_on()
    {
        using var tmp = new TempDir();
        var clip = Fixtures.WriteMp4(tmp.Combine("long.mp4"), seconds: 12, withAudio: false);

        var legacy = await VideoConverter.ToFormatAsync(clip, VideoFormat.Gif, default);
        Assert.InRange(ParseGif(File.ReadAllBytes(legacy)).Delays.Count, 110, 125); // the whole 12 s clip: under the 15 s cap

        var tile = ActionCatalog.Find(VideoActions.All, "GIF")!;
        var result = await tile.Run([clip], new ActionContext(Capabilities.All, options: new ConversionOptions { GifSeconds = 5 }), default);
        Assert.Equal(50, ParseGif(File.ReadAllBytes(Assert.Single(result.Outputs).Path)).Delays.Count);
    }

    [Fact]
    public void The_exporter_refuses_a_length_below_one_second()
    {
        using var tmp = new TempDir();
        Assert.Throws<ArgumentOutOfRangeException>(() => GifExporter.Export(tmp.WriteFile("a.mp4", Fixtures.Noise(100)), tmp.Combine("a.gif"), 0, default));
    }
}
