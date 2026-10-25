namespace MikeShift.Core.Interaction;

/// <summary>A CSS-style cubic-bezier timing function: maps linear time 0..1 to eased progress (may overshoot 1 if y control points say so).</summary>
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

    /// <summary>Fluent "decelerate" (fast out, slow in): cubic-bezier(0, 0, 0, 1). Entrances, slides and the pick pulse. Never overshoots.</summary>
    public static CubicBezier Decelerate { get; } = new(0, 0, 0, 1);

    /// <summary>Fluent "accelerate": cubic-bezier(1, 0, 1, 1). Exits (fade out, collapse). Never overshoots.</summary>
    public static CubicBezier Accelerate { get; } = new(1, 0, 1, 1);

    /// <summary>The wheel's open curve: the same as <see cref="Decelerate"/>. Windows 11 entrances decelerate and do not overshoot.</summary>
    public static CubicBezier Open => Decelerate;

    /// <summary>CSS <c>ease-in</c>.</summary>
    public static CubicBezier EaseIn { get; } = new(0.42, 0, 1, 1);

    /// <summary>CSS <c>ease-out</c>.</summary>
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

/// <summary>
/// The wheel's motion, following the Windows 11 / Fluent motion guidance: entrances take 167-250 ms and decelerate, exits take 83-167 ms and
/// accelerate, nothing overshoots, and nothing grows past its size (no zoom-out collapse). Durations are in seconds; they become 0 when the OS
/// has animations turned off. The pick pulse is kept as the confirmation that a tile was chosen.
/// </summary>
public static class WheelTimings
{
    /// <summary>Open: disc scales <see cref="OpenScaleFrom"/>→1 and fades in over this, decelerating.</summary>
    public const double OpenSeconds = 0.20;
    public const double OpenScaleFrom = 0.94;

    /// <summary>Each tile appears this much after the previous one (tile <c>i</c> starts at <c>i × StaggerSeconds</c>).</summary>
    public const double StaggerSeconds = 0.008;

    /// <summary>Page switch: old tiles fade out (accelerate) over this…</summary>
    public const double PageFadeSeconds = 0.083;
    /// <summary>…new tiles slide in (decelerate) over this…</summary>
    public const double PageSlideSeconds = 0.167;
    /// <summary>…starting this many logical px closer to the centre.</summary>
    public const double PageSlideDistance = 12;

    /// <summary>After a pick the disc fades out and settles to <see cref="CollapseScaleTo"/> over this (accelerate).</summary>
    public const double CollapseSeconds = 0.14;
    public const double CollapseScaleTo = 0.96;

    /// <summary>The picked tile pulses outward by <see cref="WheelGeometry.PickPulse"/> over this.</summary>
    public const double PickPulseSeconds = 0.12;

    /// <summary>Dismiss (released on nothing, Esc, focus loss): fade out (accelerate) and settle to <see cref="DismissScaleTo"/> over this.</summary>
    public const double DismissSeconds = 0.10;
    public const double DismissScaleTo = 0.96;

    /// <summary>Total time for the open animation of <paramref name="tileCount"/> tiles (the last tile's appearance finishes last).</summary>
    public static double OpenTotalSeconds(int tileCount) => OpenSeconds + Math.Max(0, tileCount - 1) * StaggerSeconds;

    /// <summary>Scales a duration by whether animations are enabled (<c>UISettings.AnimationsEnabled</c>).</summary>
    public static double Scaled(double seconds, bool animationsEnabled) => animationsEnabled ? seconds : 0;

    /// <summary>Disc scale during the open animation at <paramref name="elapsedSeconds"/> (0.94→1, never above 1).</summary>
    public static double OpenScale(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(OpenSeconds, animationsEnabled), OpenScaleFrom, 1);

    /// <summary>Disc opacity during the open animation (0→1, never above 1).</summary>
    public static double OpenOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(OpenSeconds, animationsEnabled), 0, 1);

    /// <summary>Tile appearance progress (0..1, never above 1) for tile <paramref name="index"/> during the open animation.</summary>
    public static double TilePop(int index, double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(OpenSeconds, animationsEnabled), 0, 1, animationsEnabled ? index * StaggerSeconds : 0);

    /// <summary>Disc scale during the collapse after a pick: 1→0.96 with accelerate.</summary>
    public static double CollapseScale(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Accelerate, elapsedSeconds, Scaled(CollapseSeconds, animationsEnabled), 1, CollapseScaleTo);

    /// <summary>Opacity during the collapse: 1→0 with accelerate (the picked tile stays visible while it pulses).</summary>
    public static double CollapseOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Accelerate, elapsedSeconds, Scaled(CollapseSeconds, animationsEnabled), 1, 0);

    /// <summary>Extra outward push of the picked tile (0→18 px).</summary>
    public static double PickPulseOffset(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(PickPulseSeconds, animationsEnabled), 0, WheelGeometry.PickPulse);

    /// <summary>Opacity during a dismiss: 1→0 with accelerate.</summary>
    public static double DismissOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Accelerate, elapsedSeconds, Scaled(DismissSeconds, animationsEnabled), 1, 0);

    /// <summary>Disc scale during a dismiss: 1→0.96.</summary>
    public static double DismissScale(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Accelerate, elapsedSeconds, Scaled(DismissSeconds, animationsEnabled), 1, DismissScaleTo);

    /// <summary>Opacity of the outgoing page's tiles: 1→0 over <see cref="PageFadeSeconds"/>.</summary>
    public static double PageOutOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Accelerate, elapsedSeconds, Scaled(PageFadeSeconds, animationsEnabled), 1, 0);

    /// <summary>Radial offset (logical px, toward the centre = negative) of the incoming page's tiles: -12→0.</summary>
    public static double PageInOffset(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(PageSlideSeconds, animationsEnabled), -PageSlideDistance, 0);

    /// <summary>Opacity of the incoming page's tiles: 0→1 over the slide.</summary>
    public static double PageInOpacity(double elapsedSeconds, bool animationsEnabled = true) =>
        Easing.Animate(Easing.Decelerate, elapsedSeconds, Scaled(PageSlideSeconds, animationsEnabled), 0, 1);
}
