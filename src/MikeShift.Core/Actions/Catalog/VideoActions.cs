using MikeShift.Core.Media;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Video category's actions, in wheel order (PLAN §1.2). OWNED BY W2c (Media).
/// MOV output is intentionally not offered (Media Foundation has no QuickTime muxer).
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class VideoActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        // ---- Format page
        Video("MP4", VideoFormat.Mp4, c => c.H264Encode && c.AacEncode, "mp4"),
        Video("M4V", VideoFormat.M4v, c => c.H264Encode && c.AacEncode, "m4v"),
        Video("WMV", VideoFormat.Wmv, null, "wmv"),
        Video("GIF", VideoFormat.Gif, null, "gif"),
        ActionDef.PerFile("M4A", WheelPage.Format,
            (input, _, ct) => VideoConverter.ExtractAudioAsync(input, AudioFormat.M4a, ct),
            requires: c => c.AacEncode),
        ActionDef.PerFile("MP3", WheelPage.Format,
            (input, _, ct) => VideoConverter.ExtractAudioAsync(input, AudioFormat.Mp3, ct),
            requires: c => c.Mp3Encode),

        // ---- Tool page
        ActionDef.PerFile("Compress", WheelPage.Tool,
            (input, _, ct) => VideoConverter.CompressAsync(input, ct),
            glyph: Glyphs.Compress,
            requires: c => c.H264Encode && c.AacEncode),
        ActionDef.PerFile("Trim", WheelPage.Tool,
            async (input, ctx, ct) => await VideoConverter.TrimAsync(input, await ctx.RequireRangeAsync(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            glyph: Glyphs.Trim, interactive: true),
    ];

    private static ActionDef Video(string title, VideoFormat format, Func<Capabilities, bool>? requires, string hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, ctx, ct) => VideoConverter.ToFormatAsync(input, format, ctx.Options, ct),
            hideFor: [hideFor], requires: requires);
}
