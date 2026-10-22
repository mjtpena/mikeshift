namespace Shiftvert.Core.Interaction;

/// <summary>A point in (fractional) logical or physical pixels, used for geometry relative to the wheel centre or the window.</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>
/// An annular sector for the renderer: outer arc from <see cref="OuterStart"/> clockwise to <see cref="OuterEnd"/>, a line to
/// <see cref="InnerEnd"/>, the inner arc counter-clockwise back to <see cref="InnerStart"/>, close. All coordinates are logical px
/// relative to the wheel centre with y growing down. For <see cref="IsFullRing"/> (a single tile) draw two full circles instead.
/// </summary>
public readonly record struct SegmentPath(
    PointD OuterStart,
    PointD OuterEnd,
    PointD InnerEnd,
    PointD InnerStart,
    double OuterRadius,
    double InnerRadius,
    double OuterSweep,
    double InnerSweep,
    bool IsFullRing)
{
    /// <summary>True if the outer arc spans more than 180 degrees (the SVG-style large-arc flag).</summary>
    public bool OuterIsLargeArc => OuterSweep > Math.PI;

    /// <summary>True if the inner arc spans more than 180 degrees.</summary>
    public bool InnerIsLargeArc => InnerSweep > Math.PI;
}

/// <summary>
/// The wheel's geometry (PLAN §3.2) and hit-testing (PLAN §3.1 item 6). Constants are logical px (DIPs); hit-testing takes
/// physical pixels and a DPI scale (1.0 = 96 dpi). Angles are radians measured <b>clockwise from 12 o'clock</b>; tile
/// <c>i</c> of <c>n</c> spans <c>[i·2π/n, (i+1)·2π/n)</c>, so tile 0 starts at 12 o'clock and its centre direction is half a step later.
/// </summary>
public static class WheelGeometry
{
    /// <summary>Wheel window edge (square), logical px.</summary>
    public const double WindowSize = 380;
    /// <summary>Glass disc radius.</summary>
    public const double DiscRadius = 150;
    /// <summary>Inner radius of the tile ring.</summary>
    public const double RingInner = 56;
    /// <summary>Outer radius of the tile ring.</summary>
    public const double RingOuter = 139;
    /// <summary>Centre hub circle radius.</summary>
    public const double HubRadius = 50;
    /// <summary>Radius at which tile labels are centred ((56 + 139) / 2).</summary>
    public const double LabelRadius = 97.5;
    /// <summary>Label box (width, height).</summary>
    public const double LabelWidth = 76, LabelHeight = 40;
    /// <summary>Tile corner radius.</summary>
    public const double TileCornerRadius = 11;
    /// <summary>Gap between neighbouring tiles.</summary>
    public const double TileGap = 6;
    /// <summary>How far the highlighted tile is pushed outward.</summary>
    public const double HighlightPush = 9;
    /// <summary>How far the picked tile pulses outward.</summary>
    public const double PickPulse = 18;

    /// <summary>Hit-test inner radius: a little beyond the hub, inside = no tile.</summary>
    public const double HitInnerRadius = 52;
    /// <summary>Hit-test outer radius: beyond the disc and ring, up to (almost) the window corner midpoints. Outside = no tile.</summary>
    public const double HitOuterRadius = 180;

    /// <summary>
    /// Radius around the point where the drag entered the wheel inside which nothing highlights (logical px).
    /// 12 px ≈ 3× the default 4 px system drag threshold: it absorbs hand tremor and the 1–2 px settle after the nudge/OLE
    /// re-targeting, yet is small enough (hub radius 50) that a deliberate move toward a tile leaves it within ~1 frame. Scaled by DPI.
    /// </summary>
    public const double NeutralRadius = 12;

    /// <summary>Wheel window edge in physical px for <paramref name="dpiScale"/>.</summary>
    public static int WindowSizePx(double dpiScale) => (int)Math.Ceiling(WindowSize * dpiScale);

    /// <summary>The angular size of one tile, <c>2π/n</c>.</summary>
    public static double StepAngle(int n) => 2 * Math.PI / n;

    /// <summary>Start angle (clockwise from 12 o'clock) of tile <paramref name="index"/> of <paramref name="n"/>.</summary>
    public static double SegmentStartAngle(int index, int n) => index * StepAngle(n);

    /// <summary>End angle of tile <paramref name="index"/>.</summary>
    public static double SegmentEndAngle(int index, int n) => (index + 1) * StepAngle(n);

    /// <summary>Centre angle of tile <paramref name="index"/> (where its label sits).</summary>
    public static double SegmentCentreAngle(int index, int n) => (index + 0.5) * StepAngle(n);

    /// <summary>The point at <paramref name="radius"/> and <paramref name="angle"/> (clockwise from 12 o'clock) relative to the centre; y grows down.</summary>
    public static PointD PointAt(double radius, double angle) => new(radius * Math.Sin(angle), -radius * Math.Cos(angle));

    /// <summary>Label centre for tile <paramref name="index"/>, offset outward by <paramref name="push"/> (pass <see cref="HighlightPush"/>/<see cref="PickPulse"/>).</summary>
    public static PointD LabelCentre(int index, int n, double push = 0) =>
        PointAt(LabelRadius + push, SegmentCentreAngle(index, n));

    /// <summary>Translation to apply to a tile that is pushed outward by <paramref name="push"/>.</summary>
    public static PointD PushOffset(int index, int n, double push) => PointAt(push, SegmentCentreAngle(index, n));

    /// <summary>
    /// The outline of tile <paramref name="index"/> of <paramref name="n"/> with a constant-width <see cref="TileGap"/> between
    /// neighbours. <paramref name="inset"/> shrinks the shape on every side (radii ±inset, gap + 2·inset): draw the inset shape
    /// with <see cref="TileCornerRadius"/> as inset and fill+stroke it with a round join of width <c>2·inset</c> to get rounded corners
    /// without writing fillet maths. A single tile is a full ring.
    /// </summary>
    public static SegmentPath Segment(int index, int n, double inset = 0)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
        if (index < 0 || index >= n) throw new ArgumentOutOfRangeException(nameof(index));

        var outer = RingOuter - inset;
        var inner = RingInner + inset;
        if (n == 1)
        {
            var top = PointAt(outer, 0);
            var itop = PointAt(inner, 0);
            return new SegmentPath(top, top, itop, itop, outer, inner, 2 * Math.PI, 2 * Math.PI, true);
        }

        var halfGap = TileGap / 2 + inset;
        var start = SegmentStartAngle(index, n);
        var end = SegmentEndAngle(index, n);
        var outerInset = Math.Asin(Math.Min(1, halfGap / outer));
        var innerInset = Math.Asin(Math.Min(1, halfGap / inner));
        var os = start + outerInset;
        var oe = end - outerInset;
        var iss = start + innerInset;
        var ie = end - innerInset;
        return new SegmentPath(
            PointAt(outer, os),
            PointAt(outer, oe),
            PointAt(inner, ie),
            PointAt(inner, iss),
            outer,
            inner,
            Math.Max(0, oe - os),
            Math.Max(0, ie - iss),
            false);
    }

    /// <summary>
    /// Which tile the pointer is over, or null. <paramref name="point"/> and <paramref name="centre"/> are physical pixels; the
    /// hit annulus is <see cref="HitInnerRadius"/>..<see cref="HitOuterRadius"/> logical px × <paramref name="dpiScale"/> (both inclusive).
    /// Inside it every direction maps to a tile (the gaps and the area beyond the ring belong to the nearest tile); inside the hub
    /// or beyond the outer radius there is no tile.
    /// </summary>
    public static int? HitTest(PointPx point, PointPx centre, int n, double dpiScale) =>
        HitTest(point.X, point.Y, centre.X, centre.Y, n, dpiScale);

    /// <inheritdoc cref="HitTest(PointPx, PointPx, int, double)"/>
    public static int? HitTest(double x, double y, double centreX, double centreY, int n, double dpiScale)
    {
        if (n < 1) return null;
        if (dpiScale <= 0) dpiScale = 1;
        double dx = x - centreX, dy = y - centreY;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < HitInnerRadius * dpiScale || r > HitOuterRadius * dpiScale) return null;
        return IndexAtAngle(AngleOf(dx, dy), n);
    }

    /// <summary>Clockwise-from-12-o'clock angle in [0, 2π) of the vector (<paramref name="dx"/>, <paramref name="dy"/>) (y down).</summary>
    public static double AngleOf(double dx, double dy)
    {
        var a = Math.Atan2(dx, -dy);
        if (a < 0) a += 2 * Math.PI;
        return a >= 2 * Math.PI ? 0 : a;
    }

    /// <summary>The tile whose range contains <paramref name="angle"/> (any real; wrapped into [0, 2π)).</summary>
    public static int IndexAtAngle(double angle, int n)
    {
        var a = angle % (2 * Math.PI);
        if (a < 0) a += 2 * Math.PI;
        var i = (int)Math.Floor(a / StepAngle(n));
        return Math.Clamp(i, 0, n - 1);
    }
}
