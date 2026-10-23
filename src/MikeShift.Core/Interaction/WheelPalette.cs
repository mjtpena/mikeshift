namespace MikeShift.Core.Interaction;

/// <summary>
/// The colours of the wheel's solid parts (callout pill, hub, tile labels) for one theme, as pure data so a test can check that every
/// text/background pair reaches WCAG AA (4.5:1) even over the worst backdrop. The translucent glass parts (disc, tiles) are
/// recipes (<see cref="SurfaceTint"/>, <see cref="SurfaceTintAlpha"/>...) because their final colour depends on the wallpaper.
/// Values follow the Windows 11 Fluent tokens (<c>SolidBackgroundFillColor</c>, <c>TextFillColorPrimary/Secondary</c>, <c>SystemFillColorCaution</c>).
/// </summary>
public sealed record WheelPalette
{
    // ---- the disc (acrylic-like: blurred backdrop, luminosity, tint, 2 % noise, 1 px stroke)
    /// <summary>The acrylic tint colour (the flyout surface colour of the theme).</summary>
    public required Rgb SurfaceTint { get; init; }
    /// <summary>How strongly the tint covers the blurred backdrop (0..1).</summary>
    public required double SurfaceTintAlpha { get; init; }
    /// <summary>How strongly the backdrop's own luminosity is replaced by the tint's (0..1): keeps a bright wallpaper from washing the glass out.</summary>
    public required double LuminosityAlpha { get; init; }
    /// <summary>Noise overlay strength (0.02 = 2 %).</summary>
    public double NoiseAlpha { get; init; } = 0.02;
    /// <summary>The 1 px outline colour of the disc and its alpha.</summary>
    public required Rgb DiscStroke { get; init; }
    public required double DiscStrokeAlpha { get; init; }

    // ---- tiles (ControlFillColorDefault: a white wash on the glass)
    public required double TileFillTopAlpha { get; init; }
    public required double TileFillBottomAlpha { get; init; }
    public required Rgb TileFill { get; init; }
    public required Rgb TileStroke { get; init; }
    public required double TileStrokeAlpha { get; init; }
    /// <summary>The label colour of an idle tile (over the glass + wash).</summary>
    public required Rgb TileText { get; init; }

    // ---- hub (an almost opaque well so its text never depends on the wallpaper)
    public required Rgb HubFill { get; init; }
    public required double HubFillAlpha { get; init; }
    public required Rgb HubStroke { get; init; }
    public required double HubStrokeAlpha { get; init; }
    public required Rgb HubText { get; init; }
    public required Rgb HubSecondary { get; init; }

    // ---- callout pill (opaque)
    public required Rgb PillFill { get; init; }
    public required Rgb PillStroke { get; init; }
    public required double PillStrokeAlpha { get; init; }
    public required Rgb PillText { get; init; }
    public required Rgb PillSecondary { get; init; }
    public required Rgb CautionFill { get; init; }
    public required Rgb CautionText { get; init; }
    public required Rgb CautionGlyph { get; init; }

    /// <summary>Colour of the label on a highlighted tile is chosen per accent by <see cref="Contrast.TextOnFill(Rgb[])"/>; these are the candidates.</summary>
    public required Rgb OnAccentLight { get; init; }
    public required Rgb OnAccentDark { get; init; }

    /// <summary>The dark theme.</summary>
    public static WheelPalette Dark { get; } = new()
    {
        SurfaceTint = Rgb.Hex("#2C2C2C"),
        SurfaceTintAlpha = 0.78,
        LuminosityAlpha = 0.55,
        DiscStroke = Rgb.White,
        DiscStrokeAlpha = 0.10,
        TileFill = Rgb.White,
        TileFillTopAlpha = 0.06,
        TileFillBottomAlpha = 0.03,
        TileStroke = Rgb.White,
        TileStrokeAlpha = 0.08,
        TileText = Rgb.White,
        HubFill = Rgb.Hex("#202020"),
        HubFillAlpha = 0.92,
        HubStroke = Rgb.White,
        HubStrokeAlpha = 0.10,
        HubText = Rgb.White,
        HubSecondary = Rgb.Hex("#C5C5C5"),
        PillFill = Rgb.Hex("#2C2C2C"),
        PillStroke = Rgb.White,
        PillStrokeAlpha = 0.14,
        PillText = Rgb.White,
        PillSecondary = Rgb.Hex("#C5C5C5"),
        CautionFill = Rgb.Hex("#433519"),
        CautionText = Rgb.White,
        CautionGlyph = Rgb.Hex("#FCE100"),
        OnAccentLight = Rgb.White,
        OnAccentDark = Rgb.Black,
    };

    /// <summary>The light theme.</summary>
    public static WheelPalette Light { get; } = new()
    {
        SurfaceTint = Rgb.Hex("#F9F9F9"),
        SurfaceTintAlpha = 0.80,
        LuminosityAlpha = 0.55,
        DiscStroke = Rgb.Black,
        DiscStrokeAlpha = 0.10,
        TileFill = Rgb.White,
        TileFillTopAlpha = 1.0,
        TileFillBottomAlpha = 0.90,
        TileStroke = Rgb.Black,
        TileStrokeAlpha = 0.07,
        TileText = Rgb.Hex("#1A1A1A"),
        HubFill = Rgb.Hex("#F3F3F3"),
        HubFillAlpha = 0.92,
        HubStroke = Rgb.Black,
        HubStrokeAlpha = 0.08,
        HubText = Rgb.Hex("#1A1A1A"),
        HubSecondary = Rgb.Hex("#5C5C5C"),
        PillFill = Rgb.Hex("#F9F9F9"),
        PillStroke = Rgb.Black,
        PillStrokeAlpha = 0.12,
        PillText = Rgb.Hex("#1A1A1A"),
        PillSecondary = Rgb.Hex("#5C5C5C"),
        CautionFill = Rgb.Hex("#FFF4CE"),
        CautionText = Rgb.Hex("#1A1A1A"),
        CautionGlyph = Rgb.Hex("#9D5D00"),
        OnAccentLight = Rgb.White,
        OnAccentDark = Rgb.Black,
    };

    /// <summary>The palette for a theme.</summary>
    public static WheelPalette For(bool dark) => dark ? Dark : Light;

    /// <summary>
    /// The colour of the glass under the hub and tiles where the wallpaper (as the blurred backdrop) has colour <paramref name="backdrop"/>:
    /// the backdrop pulled toward the tint by the luminosity layer and then covered by the tint. Used to check the worst case (a white and a black wallpaper).
    /// </summary>
    public Rgb GlassOver(Rgb backdrop)
    {
        var luminosity = SurfaceTint.Over(backdrop, LuminosityAlpha * 0.5);
        return SurfaceTint.Over(luminosity, SurfaceTintAlpha);
    }

    /// <summary>The hub's actual background over <paramref name="backdrop"/>.</summary>
    public Rgb HubOver(Rgb backdrop) => HubFill.Over(GlassOver(backdrop), HubFillAlpha);

    /// <summary>An idle tile's actual background (top of the wash) over <paramref name="backdrop"/>.</summary>
    public Rgb TileOver(Rgb backdrop) => TileFill.Over(GlassOver(backdrop), Math.Min(TileFillTopAlpha, 1));

    /// <summary>Backdrops the contrast checks use: the extremes plus mid-greys.</summary>
    public static IReadOnlyList<Rgb> WorstBackdrops { get; } =
        [Rgb.White, Rgb.Black, new Rgb(128, 128, 128), new Rgb(255, 214, 10), new Rgb(0, 120, 212)];
}
