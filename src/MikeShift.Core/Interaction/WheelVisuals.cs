namespace MikeShift.Core.Interaction;

/// <summary>An opaque sRGB colour (the pure-logic side of the wheel's colour decisions; the renderer converts to its own colour type).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb White { get; } = new(255, 255, 255);
    public static Rgb Black { get; } = new(0, 0, 0);

    /// <summary>Parses <c>#RRGGBB</c> (test and constant convenience).</summary>
    public static Rgb Hex(string hex)
    {
        var s = hex.TrimStart('#');
        return new Rgb(Convert.ToByte(s[0..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
    }

    /// <summary>This colour drawn over <paramref name="background"/> with opacity <paramref name="alpha"/> (0..1).</summary>
    public Rgb Over(Rgb background, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        byte M(byte fg, byte bg) => (byte)Math.Round(fg * alpha + bg * (1 - alpha));
        return new Rgb(M(R, background.R), M(G, background.G), M(B, background.B));
    }

    /// <summary>Mixes toward <paramref name="target"/> by <paramref name="amount"/> (0..1).</summary>
    public Rgb Mix(Rgb target, double amount) => target.Over(this, amount);
}

/// <summary>WCAG 2.x relative luminance and contrast ratio, and the black-or-white decisions built on them.</summary>
public static class Contrast
{
    /// <summary>The WCAG AA ratio for normal-size text.</summary>
    public const double AaText = 4.5;

    /// <summary>The WCAG AA ratio for large text and UI components.</summary>
    public const double AaLarge = 3.0;

    /// <summary>Relative luminance of an sRGB colour, 0 (black) to 1 (white).</summary>
    public static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Contrast ratio between two colours, 1 to 21 (order does not matter).</summary>
    public static double Ratio(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>The smallest contrast ratio <paramref name="text"/> has against any of <paramref name="backgrounds"/> (the worst point of a gradient or backdrop).</summary>
    public static double WorstRatio(Rgb text, IEnumerable<Rgb> backgrounds)
    {
        var worst = double.MaxValue;
        foreach (var b in backgrounds) worst = Math.Min(worst, Ratio(text, b));
        return worst == double.MaxValue ? 1 : worst;
    }

    /// <summary>
    /// The label colour for a tile filled with <paramref name="fillStops"/> (a flat accent has one stop, a gradient several): white if it
    /// reaches <see cref="AaText"/> against every stop, otherwise black if it does, otherwise whichever of the two does better.
    /// This is why a yellow or light-teal accent gets a black label and the default blue keeps a white one.
    /// </summary>
    public static Rgb TextOnFill(params Rgb[] fillStops) => TextOnFill(Rgb.White, Rgb.Black, fillStops);

    /// <summary><see cref="TextOnFill(Rgb[])"/> with explicit candidates (the theme's own light and dark text colours).</summary>
    public static Rgb TextOnFill(Rgb light, Rgb dark, IReadOnlyList<Rgb> fillStops)
    {
        var onLight = WorstRatio(light, fillStops);
        if (onLight >= AaText) return light;
        var onDark = WorstRatio(dark, fillStops);
        return onDark >= AaText || onDark > onLight ? dark : light;
    }
}

/// <summary>Text sizes of the wheel, in logical px at text scale 1, and the rules for scaling them.</summary>
public static class WheelTypography
{
    /// <summary>The largest <c>UISettings.TextScaleFactor</c> the wheel honours: the disc is a fixed size, so beyond this the labels would not fit.</summary>
    public const double MaxTextScale = 1.4;

    /// <summary>Above this the hub drops its key hint (the callout pill carries it) so the title keeps the room.</summary>
    public const double HubHintMaxScale = 1.2;

    /// <summary>Fluent Caption: the smallest ramp step. Tool captions (SemiBold), the pill text and the hub hint.</summary>
    public const double Caption = 12;

    /// <summary>Fluent Body: format labels (Bold) and the hub title (SemiBold).</summary>
    public const double Body = 14;

    /// <summary>Tile and category glyphs.</summary>
    public const double TileGlyph = 18;

    /// <summary>The glyph in the hub of a single-tile wheel.</summary>
    public const double SingleGlyph = 24;

    /// <summary>The smallest size text is ever shrunk to when it must fit a tile (never below Caption).</summary>
    public const double MinFitSize = Caption;

    /// <summary>The factor to scale text by for the OS text-size setting <paramref name="textScaleFactor"/> (1.0 to 2.25): clamped to 1 .. <see cref="MaxTextScale"/>; NaN and nonsense mean 1.</summary>
    public static double ClampScale(double textScaleFactor)
    {
        if (double.IsNaN(textScaleFactor) || double.IsInfinity(textScaleFactor) || textScaleFactor < 1) return 1;
        return Math.Min(textScaleFactor, MaxTextScale);
    }

    /// <summary>A nominal size multiplied by the text scale.</summary>
    public static double Scaled(double nominal, double textScale) => nominal * ClampScale(textScale);

    /// <summary>
    /// The size to draw text of <paramref name="size"/> px that measured <paramref name="measuredWidth"/> at that size so it fits
    /// <paramref name="maxWidth"/>: the size itself if it fits, else shrunk proportionally but never below <paramref name="minSize"/> (the text is then clipped by the caller).
    /// </summary>
    public static double FitSize(double size, double measuredWidth, double maxWidth, double minSize = MinFitSize)
    {
        if (measuredWidth <= maxWidth || measuredWidth <= 0 || maxWidth <= 0) return size;
        return Math.Max(Math.Min(size, minSize), size * maxWidth / measuredWidth);
    }

    /// <summary>True if the hub has room for its key hint at this text scale.</summary>
    public static bool HubHintFits(double textScale) => ClampScale(textScale) <= HubHintMaxScale;

    /// <summary>Height of a callout pill whose text is <paramref name="fontPx"/> tall (12 px text: 24 px pill).</summary>
    public static double PillHeight(double fontPx) => Math.Ceiling(fontPx + 12);
}

/// <summary>Counts how many wheels have been shown so the key hints can be loud at first and quiet later.</summary>
public static class WheelHints
{
    /// <summary>The first this-many wheels show the key hints prominently.</summary>
    public const int ProminentWheels = 20;

    /// <summary>How the hints look for the wheel about to be shown, given how many wheels have been shown before it.</summary>
    public static HintEmphasis EmphasisFor(int wheelsShownBefore) =>
        wheelsShownBefore < ProminentWheels ? HintEmphasis.Prominent : HintEmphasis.Quiet;

    /// <summary>True while counting still matters (past <see cref="ProminentWheels"/> nothing more is stored).</summary>
    public static bool ShouldCount(int wheelsShownBefore) => wheelsShownBefore < ProminentWheels;

    /// <summary>Reads the stored count; anything that is not a non-negative integer (missing, empty, damaged file) is 0.</summary>
    public static int Parse(string? stored)
    {
        if (stored is null) return 0;
        return int.TryParse(stored.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
               && n >= 0 ? Math.Min(n, ProminentWheels) : 0;
    }

    /// <summary>The text to store for <paramref name="count"/>.</summary>
    public static string Serialize(int count) =>
        Math.Clamp(count, 0, ProminentWheels).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
