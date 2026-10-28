using MikeShift.Core.Images;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The "Upscale 2x" tool of the Image category: <c>logo.png</c> becomes <c>logo 2x.png</c> at twice the width and height. One tile, one factor;
/// today the Windows AI image scaler of Copilot+ PCs is the only engine, so the tile is shown only when <see cref="Capabilities.AiUpscale"/>.
/// A second engine for other PCs would change that gate, not the title or the naming.
/// </summary>
internal static class UpscaleAction
{
    public const string Title = "Upscale 2x";

    /// <summary>The tile. <paramref name="engine"/> replaces the Windows scaler (tests only); null means the real one.</summary>
    public static ActionDef Create(IUpscaleEngine? engine = null) => ActionDef.PerFile(Title, WheelPage.Tool,
        (input, ctx, ct) => engine is null
            ? ImageConverter.UpscaleAsync(input, ctx.Capabilities, ctx.Options, ct)
            : ImageConverter.UpscaleAsync(input, ctx.Capabilities, ctx.Options, engine, ct),
        glyph: Glyphs.Upscale,
        requires: c => c.AiUpscale);
}
