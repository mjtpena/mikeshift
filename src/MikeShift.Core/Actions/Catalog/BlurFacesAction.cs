using MikeShift.Core.Feedback;
using MikeShift.Core.Images;

namespace MikeShift.Core.Actions.Catalog;

/// <summary>
/// The "Blur faces" tool of the Image category. It runs through <see cref="Each"/> like any per-file tool and adds the number of faces it
/// covered to <see cref="ActionResult.Summary"/>, which the toast and the command line show. Only offered when <see cref="Capabilities.FaceDetect"/>.
/// </summary>
internal static class BlurFacesAction
{
    public const string Title = "Blur faces";

    /// <summary>The tile. <paramref name="find"/> replaces Windows' detector (tests only); null means the real one.</summary>
    public static ActionDef Create(FaceFinder? find = null) => new(Title, WheelPage.Tool, async (paths, ctx, ct) =>
    {
        // Each runs the files one after another, so a plain counter is safe.
        var faces = 0;
        var result = await Each.RunAsync(paths, ctx, async (input, token) =>
        {
            var blurred = find is null
                ? await ImageConverter.BlurFacesAsync(input, ctx.Capabilities, ctx.Options, token).ConfigureAwait(false)
                : await ImageConverter.BlurFacesAsync(input, ctx.Capabilities, ctx.Options, find, token).ConfigureAwait(false);
            faces += blurred.Faces;
            return blurred.Path;
        }, ct).ConfigureAwait(false);
        return faces > 0 ? result with { Summary = FaceBlurCopy.Blurred(faces) } : result;
    })
    {
        Glyph = Glyphs.BlurFaces,
        Requires = c => c.FaceDetect,
    };
}
