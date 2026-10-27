using MikeShift.Core.Feedback;
using MikeShift.Core.Media;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Video category's actions, in wheel order.
/// MOV output is intentionally not offered (Media Foundation has no QuickTime muxer).
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class VideoActions
{
    /// <summary>The list with the two fixed Fit sizes ("Fit 10 MB", "Fit 25 MB"). <see cref="WithFit"/> swaps them for one size from <c>--max-size</c>.</summary>
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
        .. ConversionOptions.FitChoices.Select(Fit),
        ActionDef.PerFile("Trim", WheelPage.Tool,
            async (input, ctx, ct) => await VideoConverter.TrimAsync(input, await ctx.RequireRangeAsync(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            glyph: Glyphs.Trim, interactive: true),
    ];

    /// <summary>
    /// The list with a single Fit tool for <paramref name="megabytes"/> (the command line's <c>--max-size</c>) instead of the two fixed ones;
    /// <see cref="All"/> for null.
    /// </summary>
    public static IReadOnlyList<ActionDef> WithFit(int? megabytes)
    {
        if (megabytes is not { } mb) return All;
        var list = new List<ActionDef>();
        foreach (var action in All)
        {
            if (action.Id != FitId) list.Add(action);
            else if (!list.Any(a => a.Id == FitId)) list.Add(Fit(mb));
        }
        return list;
    }

    /// <summary>"Fit 10 MB": an MP4 of at most that size (all sizes share the stable <see cref="ActionDef.Id"/> "Fit").</summary>
    private static ActionDef Fit(int megabytes) =>
        ActionDef.PerFile(SizeTargetCopy.Title(megabytes), WheelPage.Tool,
            (input, _, ct) => VideoConverter.FitAsync(input, megabytes, ct),
            glyph: Glyphs.Fit, id: FitId,
            requires: c => c.H264Encode && c.AacEncode);

    /// <summary>The stable id of every Fit tool, whatever its size (the right-click ranking uses it).</summary>
    public const string FitId = "Fit";

    private static ActionDef Video(string title, VideoFormat format, Func<Capabilities, bool>? requires, string hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, ctx, ct) => VideoConverter.ToFormatAsync(input, format, ctx.Options, ct),
            hideFor: [hideFor], requires: requires);
}
