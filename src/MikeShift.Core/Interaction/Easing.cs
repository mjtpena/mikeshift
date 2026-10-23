namespace MikeShift.Core.Interaction;

/// <summary>A CSS-style cubic-bezier timing function: maps linear time 0..1 to eased progress (may overshoot 1).</summary>
public sealed class CubicBezier
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        if (x1 is < 0 or > 1 || x2 is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(x1), "x control points must be within [0,1].");
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
    }

    /// <summary>Eased progress for linear time <paramref name="t"/> (clamped to 0..1). Exactly 0 at 0 and 1 at 1.</summary>
    public double Evaluate(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        if (_x1 == _y1 && _x2 == _y2) return t; // linear

        // Solve x(s) = t for the curve parameter s: Newton with a bisection fallback.
        var s = t;
        for (var i = 0; i < 8; i++)
        {
            var x = Sample(s, _x1, _x2) - t;
            if (Math.Abs(x) < 1e-7) return Sample(s, _y1, _y2);
            var d = Derivative(s, _x1, _x2);
            if (Math.Abs(d) < 1e-6) break;
            s -= x / d;
        }

        double lo = 0, hi = 1;
        s = t;
        for (var i = 0; i < 40; i++)
        {
            var x = Sample(s, _x1, _x2);
            if (Math.Abs(x - t) < 1e-7) break;
            if (x < t) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Sample(s, _y1, _y2);
    }

    private static double Sample(double s, double p1, double p2)
    {
        var u = 1 - s;
        return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
    }

    private static double Derivative(double s, double p1, double p2)
    {
        var u = 1 - s;
        return 3 * u * u * p1 + 6 * u * s * (p2 - p1) + 3 * s * s * (1 - p2);
    }
}

/// <summary>Ready-made timing functions used by the wheel.</summary>
public static class Easing
{
    public static CubicBezier Linear { get; } = new(0, 0, 1, 1);

    /// <summary>The wheel open curve: cubic-bezier(0.3, 1.25, 0.4, 1), overshoots ~5 % before settling.</summary>
    public static CubicBezier Open { get; } = new(0.3, 1.25, 0.4, 1);

    /// <summary>CSS <c>ease-in</c>, used for the collapse.</summary>
    public static CubicBezier EaseIn { get; } = new(0.42, 0, 1, 1);

    /// <summary>CSS <c>ease-out</c>, used for fades, slides and the pick pulse.</summary>
    public static CubicBezier EaseOut { get; } = new(0, 0, 0.58, 1);

    /// <summary>CSS <c>ease-in-out</c>.</summary>
    public static CubicBezier EaseInOut { get; } = new(0.42, 0, 0.58, 1);

    /// <summary>Linear time 0..1 for an animation that started <paramref name="delaySeconds"/> after t=0 and lasts <paramref name="durationSeconds"/>. Zero duration = instantly 1 (animations disabled).</summary>
    public static double Progress(double elapsedSeconds, double durationSeconds, double delaySeconds = 0)
    {
        if (durationSeconds <= 0) return elapsedSeconds >= delaySeconds ? 1 : 0;
        return Math.Clamp((elapsedSeconds - delaySeconds) / durationSeconds, 0, 1);
    }

    /// <summary>Eased value of an animation between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public static double Animate(CubicBezier curve, double elapsedSeconds, double durationSeconds, double from, double to, double delaySeconds = 0) =>
        from + (to - from) * curve.Evaluate(Progress(elapsedSeconds, durationSeconds, delaySeconds));
}

/// <summary>The wheel animation numbers of PLAN §3.2. Durations are in seconds; multiply by 0 when the OS has animations turned off.</summary>
public static class WheelTimings
{
    /// <summary>Open: disc scales <see cref="OpenScaleFrom"/>→1 and fades in over this, with <see cref="Easing.Open"/>.</summary>
    public const double OpenSeconds = 0.28;
    public const double OpenScaleFrom = 0.60;

    /// <summary>Each tile pops out this much after the previous one (tile <c>i</c> starts at <c>i × StaggerSeconds</c>).</summary>
    public const double StaggerSeconds = 0.010;

    /// <summary>Page switch: old tiles fade out over this…</summary>
    public const double PageFadeSeconds = 0.12;
    /// <summary>…new tiles slide in over this…</summary>
    public const double PageSlideSeconds = 0.30;
    /// <summary>…starting this many logical px closer to the centre.</summary>
    public const double PageSlideDistance = 16;

    /// <summary>Collapse after a pick: disc grows to <see cref="CollapseScaleTo"/> and fades, ease-in.</summary>
    public const double CollapseSeconds = 0.20;
    public const double CollapseScaleTo = 1.70;

    /// <summary>The picked tile pulses outward by <see cref="WheelGeometry.PickPulse"/> over this.</summary>
    public const double PickPulseSeconds = 0.16;

    /// <summary>Dismiss (released on nothing, Esc, focus loss): fade out over this.</summary>
    public const double DismissSeconds = 0.20;

    /// <summary>Total time for the open animation of <paramref name="tileCount"/> tiles (the last tile's pop finishes last).</summary>
    public static double OpenTotalSeconds(int tileCount) => OpenSeconds + Math.Max(0, tileCount - 1) * StaggerSeconds;

    /// <summary>Scales a duration by whether animations are enabled (<c>UISettings.AnimationsEnabled</c>).</summary>
    public static double Scaled(double seconds, bool animationsEnabled) => animationsEnabled ? seconds : 0;

    /// <summary>Disc scale during the open animation at <paramref name="elapsedSeconds"/> (may overshoot 1 slightly).</summary>
    public static double OpenScale(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Open, elapsedSeconds, Scaled(OpenSeconds, animationsEnabled), OpenScaleFrom, 1);

    /// <summary>Tile pop progress (0..1, may overshoot) for tile <paramref name="index"/> during the open animation.</summary>
    public static double TilePop(int index, double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Open, elapsedSeconds, Scaled(OpenSeconds, animationsEnabled), 0, 1, animationsEnabled ? index * StaggerSeconds : 0);

    /// <summary>Disc scale during the collapse: 1→1.7 with ease-in.</summary>
    public static double CollapseScale(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseIn, elapsedSeconds, Scaled(CollapseSeconds, animationsEnabled), 1, CollapseScaleTo);

    /// <summary>Opacity during the collapse: 1→0 with ease-in.</summary>
    public static double CollapseOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseIn, elapsedSeconds, Scaled(CollapseSeconds, animationsEnabled), 1, 0);

    /// <summary>Extra outward push of the picked tile (0→18 px).</summary>
    public static double PickPulseOffset(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseOut, elapsedSeconds, Scaled(PickPulseSeconds, animationsEnabled), 0, WheelGeometry.PickPulse);

    /// <summary>Opacity during a dismiss: 1→0.</summary>
    public static double DismissOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseOut, elapsedSeconds, Scaled(DismissSeconds, animationsEnabled), 1, 0);

    /// <summary>Opacity of the outgoing page's tiles: 1→0 over 0.12 s.</summary>
    public static double PageOutOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseOut, elapsedSeconds, Scaled(PageFadeSeconds, animationsEnabled), 1, 0);

    /// <summary>Radial offset (logical px, toward the centre = negative) of the incoming page's tiles: -16→0 over 0.3 s.</summary>
    public static double PageInOffset(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseOut, elapsedSeconds, Scaled(PageSlideSeconds, animationsEnabled), -PageSlideDistance, 0);

    /// <summary>Opacity of the incoming page's tiles: 0→1 over the slide.</summary>
    public static double PageInOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.EaseOut, elapsedSeconds, Scaled(PageSlideSeconds, animationsEnabled), 0, 1);
}
