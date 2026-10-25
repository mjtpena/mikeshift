using MikeShift.Core.Media;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The Audio category's actions, in wheel order.
/// Titles are unique within the list and double as CLI names.
/// </summary>
public static class AudioActions
{
    public static IReadOnlyList<ActionDef> All { get; } =
    [
        // ---- Format page
        Audio("M4A", AudioFormat.M4a, c => c.AacEncode, "m4a"),
        Audio("WAV", AudioFormat.Wav, null, "wav"),
        Audio("AIFF", AudioFormat.Aiff, null, "aif", "aiff", "aifc"),
        Audio("FLAC", AudioFormat.Flac, c => c.FlacEncode, "flac"),
        Audio("MP3", AudioFormat.Mp3, c => c.Mp3Encode, "mp3"),

        // ---- Tool page
        ActionDef.PerFile("Compress", WheelPage.Tool,
            (input, _, ct) => AudioConverter.CompressAsync(input, ct),
            glyph: Glyphs.Compress,
            requires: c => c.AacEncode),
        ActionDef.PerFile("Trim", WheelPage.Tool,
            async (input, ctx, ct) => await AudioConverter.TrimAsync(input, await ctx.RequireRangeAsync(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            glyph: Glyphs.Trim, interactive: true),
    ];

    private static ActionDef Audio(string title, AudioFormat format, Func<Capabilities, bool>? requires, params string[] hideFor) =>
        ActionDef.PerFile(title, WheelPage.Format,
            (input, _, ct) => AudioConverter.ToFormatAsync(input, format, ct),
            hideFor: hideFor, requires: requires);
}
