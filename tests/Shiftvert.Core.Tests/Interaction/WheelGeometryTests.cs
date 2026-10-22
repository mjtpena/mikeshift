using Shiftvert.Core.Interaction;

namespace Shiftvert.Core.Tests.Interaction;

public class WheelGeometryTests
{
    private static readonly PointPx Centre = new(1000, 700);

    private static PointPx Offset(double radius, double angle, double dpi = 1.0)
    {
        var p = WheelGeometry.PointAt(radius * dpi, angle);
        return new PointPx(Centre.X + (int)Math.Round(p.X), Centre.Y + (int)Math.Round(p.Y));
    }

    public static IEnumerable<object[]> Counts() => new[] { 1, 2, 5, 8, 12 }.Select(n => new object[] { n });

    // ------------------------------------------------------------------ constants

    [Fact]
    public void ConstantsMatchTheSpec()
    {
        Assert.Equal(380, WheelGeometry.WindowSize);
        Assert.Equal(150, WheelGeometry.DiscRadius);
        Assert.Equal(56, WheelGeometry.RingInner);
        Assert.Equal(139, WheelGeometry.RingOuter);
        Assert.Equal(50, WheelGeometry.HubRadius);
        Assert.Equal(97.5, WheelGeometry.LabelRadius);
        Assert.Equal(9, WheelGeometry.HighlightPush);
        Assert.Equal(18, WheelGeometry.PickPulse);
        Assert.Equal(11, WheelGeometry.TileCornerRadius);
        Assert.Equal(6, WheelGeometry.TileGap);
        Assert.Equal(52, WheelGeometry.HitInnerRadius);
        Assert.Equal(180, WheelGeometry.HitOuterRadius);
        Assert.Equal((WheelGeometry.RingInner + WheelGeometry.RingOuter) / 2, WheelGeometry.LabelRadius);
    }

    [Theory]
    [InlineData(1.0, 380)]
    [InlineData(1.25, 475)]
    [InlineData(1.5, 570)]
    [InlineData(2.0, 760)]
    public void WindowSizeScalesWithDpi(double dpi, int expected) =>
        Assert.Equal(expected, WheelGeometry.WindowSizePx(dpi));

    // ------------------------------------------------------------------ angles

    [Fact]
    public void AnglesRunClockwiseFromTwelveOClock()
    {
        Assert.Equal(0, WheelGeometry.AngleOf(0, -10), 9);                   // up
        Assert.Equal(Math.PI / 2, WheelGeometry.AngleOf(10, 0), 9);          // right
        Assert.Equal(Math.PI, WheelGeometry.AngleOf(0, 10), 9);              // down
        Assert.Equal(3 * Math.PI / 2, WheelGeometry.AngleOf(-10, 0), 9);     // left
        Assert.Equal(7 * Math.PI / 4, WheelGeometry.AngleOf(-10, -10), 9);   // up-left
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void SegmentAnglesTileTheCircleExactly(int n)
    {
        Assert.Equal(0, WheelGeometry.SegmentStartAngle(0, n));
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(WheelGeometry.SegmentEndAngle(i, n), i + 1 < n ? WheelGeometry.SegmentStartAngle(i + 1, n) : 2 * Math.PI, 9);
            Assert.Equal((WheelGeometry.SegmentStartAngle(i, n) + WheelGeometry.SegmentEndAngle(i, n)) / 2, WheelGeometry.SegmentCentreAngle(i, n), 9);
        }
        Assert.Equal(2 * Math.PI, n * WheelGeometry.StepAngle(n), 9);
    }

    [Fact]
    public void PointAtIsTheInverseOfAngleOf()
    {
        for (var a = 0.05; a < 2 * Math.PI; a += 0.37)
        {
            var p = WheelGeometry.PointAt(80, a);
            Assert.Equal(a, WheelGeometry.AngleOf(p.X, p.Y), 9);
        }
    }

    [Fact]
    public void LabelCentresSitOnTheLabelRadiusAndPushMovesThemOutward()
    {
        var p = WheelGeometry.LabelCentre(2, 5);
        Assert.Equal(WheelGeometry.LabelRadius, Math.Sqrt(p.X * p.X + p.Y * p.Y), 9);
        var pushed = WheelGeometry.LabelCentre(2, 5, WheelGeometry.HighlightPush);
        Assert.Equal(WheelGeometry.LabelRadius + 9, Math.Sqrt(pushed.X * pushed.X + pushed.Y * pushed.Y), 9);
        var off = WheelGeometry.PushOffset(2, 5, 9);
        Assert.Equal(pushed.X - p.X, off.X, 9);
        Assert.Equal(pushed.Y - p.Y, off.Y, 9);
    }

    [Fact]
    public void FirstTileOfFourIsTheUpperRightQuadrant()
    {
        var c = WheelGeometry.LabelCentre(0, 4);
        Assert.True(c.X > 0 && c.Y < 0);
    }

    // ------------------------------------------------------------------ hit-testing

    [Theory]
    [MemberData(nameof(Counts))]
    public void CentreDirectionOfEveryTileHitsThatTile(int n)
    {
        for (var i = 0; i < n; i++)
            Assert.Equal(i, WheelGeometry.HitTest(Offset(100, WheelGeometry.SegmentCentreAngle(i, n)), Centre, n, 1.0));
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void JustInsideEachBoundaryBelongsToTheNeighbouringTile(int n)
    {
        const double eps = 0.01;
        for (var i = 0; i < n; i++)
        {
            // at r=150 one pixel is <0.4 degrees, so eps radians (0.57 degrees) is comfortably more than rounding noise
            var start = WheelGeometry.SegmentStartAngle(i, n);
            Assert.Equal(i, WheelGeometry.HitTest(Offset(150, start + eps), Centre, n, 1.0));
            Assert.Equal((i + n - 1) % n, WheelGeometry.HitTest(Offset(150, start - eps), Centre, n, 1.0));
        }
    }

    [Fact]
    public void DirectionsOnTheAxesMapToDeterministicTiles()
    {
        Assert.Equal(0, WheelGeometry.HitTest(new PointPx(1000, 600), Centre, 4, 1.0));  // straight up: angle 0
        Assert.Equal(1, WheelGeometry.HitTest(new PointPx(1100, 700), Centre, 4, 1.0));  // right: exactly pi/2, start of tile 1
        Assert.Equal(2, WheelGeometry.HitTest(new PointPx(1000, 800), Centre, 4, 1.0));  // down
        Assert.Equal(1, WheelGeometry.HitTest(new PointPx(1000, 800), Centre, 2, 1.0));  // two tiles: right half 0, left half 1; straight down is the boundary -> tile 1
        Assert.Equal(0, WheelGeometry.HitTest(new PointPx(1100, 700), Centre, 2, 1.0));
        Assert.Equal(1, WheelGeometry.HitTest(new PointPx(900, 700), Centre, 2, 1.0));
    }

    [Fact]
    public void EveryDirectionOutsideTheHubMapsToATileForEveryCount()
    {
        foreach (var n in new[] { 1, 2, 3, 5, 8, 12, 13 })
            for (var deg = 0; deg < 360; deg++)
                foreach (var r in new[] { 54.0, 56, 97.5, 139, 150, 178 })
                {
                    var hit = WheelGeometry.HitTest(Offset(r, deg * Math.PI / 180), Centre, n, 1.0);
                    Assert.NotNull(hit);
                    Assert.InRange(hit!.Value, 0, n - 1);
                }
    }

    [Fact]
    public void SingleTileCoversEverything()
    {
        for (var deg = 0; deg < 360; deg += 7)
            Assert.Equal(0, WheelGeometry.HitTest(Offset(100, deg * Math.PI / 180), Centre, 1, 1.0));
    }

    [Fact]
    public void GapsBetweenTilesCountAsTheNearestTile()
    {
        // 8 tiles: the boundary between tile 0 and 1 is at 45 degrees; a point slightly either side sits in the visual gap
        Assert.Equal(0, WheelGeometry.HitTest(Offset(100, Math.PI / 4 - 0.005), Centre, 8, 1.0));
        Assert.Equal(1, WheelGeometry.HitTest(Offset(100, Math.PI / 4 + 0.005), Centre, 8, 1.0));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void HubIsADeadZoneAndTheAnnulusIsInclusiveScaledByDpi(double dpi)
    {
        var inner = (int)(52 * dpi);
        var outer = (int)(180 * dpi);
        Assert.Equal(52 * dpi, inner); // the chosen scales give whole-pixel radii
        Assert.Equal(180 * dpi, outer);

        Assert.Null(WheelGeometry.HitTest(Centre, Centre, 6, dpi));
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X + inner - 1, Centre.Y), Centre, 6, dpi));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X + inner, Centre.Y), Centre, 6, dpi));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y - outer), Centre, 6, dpi));
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y - outer - 1), Centre, 6, dpi));
    }

    [Fact]
    public void DpiOneBoundariesAtWholePixels()
    {
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X + 51, Centre.Y), Centre, 5, 1.0));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X + 52, Centre.Y), Centre, 5, 1.0));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X + 180, Centre.Y), Centre, 5, 1.0));
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X + 181, Centre.Y), Centre, 5, 1.0));
    }

    [Fact]
    public void DpiOnePointFiveBoundariesAtWholePixels()
    {
        // 52 * 1.5 = 78, 180 * 1.5 = 270
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X - 77, Centre.Y), Centre, 5, 1.5));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X - 78, Centre.Y), Centre, 5, 1.5));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y + 270), Centre, 5, 1.5));
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y + 271), Centre, 5, 1.5));
    }

    [Fact]
    public void DpiTwoBoundariesAtWholePixels()
    {
        // 52 * 2 = 104, 180 * 2 = 360
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y - 103), Centre, 8, 2.0));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X, Centre.Y - 104), Centre, 8, 2.0));
        Assert.NotNull(WheelGeometry.HitTest(new PointPx(Centre.X + 360, Centre.Y), Centre, 8, 2.0));
        Assert.Null(WheelGeometry.HitTest(new PointPx(Centre.X + 361, Centre.Y), Centre, 8, 2.0));
    }

    [Fact]
    public void ScaledHitTestAgreesWithLogicalHitTestAtTheSameLogicalPoint()
    {
        foreach (var dpi in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
            foreach (var n in new[] { 2, 5, 8, 12 })
                for (var deg = 3.5; deg < 360; deg += 13)
                {
                    var a = deg * Math.PI / 180;
                    var logical = WheelGeometry.HitTest(Offset(110, a), Centre, n, 1.0);
                    var scaled = WheelGeometry.HitTest(Offset(110, a, dpi), Centre, n, dpi);
                    Assert.Equal(logical, scaled);
                }
    }

    [Fact]
    public void OffsetCentreAndNegativeVirtualScreenCoordinatesWork()
    {
        var c = new PointPx(-1500, -300); // a monitor to the left of / above the primary
        Assert.Equal(0, WheelGeometry.HitTest(new PointPx(c.X + 10, c.Y - 100), c, 4, 1.0));
        Assert.Equal(3, WheelGeometry.HitTest(new PointPx(c.X - 100, c.Y - 10), c, 4, 1.0));
    }

    [Fact]
    public void InvalidInputsGiveNoTile()
    {
        Assert.Null(WheelGeometry.HitTest(new PointPx(1000, 600), Centre, 0, 1.0));
        Assert.Null(WheelGeometry.HitTest(new PointPx(1000, 600), Centre, -3, 1.0));
        Assert.Equal(0, WheelGeometry.HitTest(new PointPx(1000, 600), Centre, 4, 0)); // non-positive scale is treated as 1.0
    }

    [Fact]
    public void IndexAtAngleWrapsAndClamps()
    {
        Assert.Equal(0, WheelGeometry.IndexAtAngle(0, 5));
        Assert.Equal(4, WheelGeometry.IndexAtAngle(2 * Math.PI - 1e-12, 5));
        Assert.Equal(0, WheelGeometry.IndexAtAngle(2 * Math.PI, 5));
        Assert.Equal(4, WheelGeometry.IndexAtAngle(-0.01, 5));
        Assert.Equal(1, WheelGeometry.IndexAtAngle(2 * Math.PI + 1.3, 5));
    }

    // ------------------------------------------------------------------ segment outlines

    [Theory]
    [MemberData(nameof(Counts))]
    public void SegmentPointsLieOnTheRingRadii(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var s = WheelGeometry.Segment(i, n);
            Assert.Equal(WheelGeometry.RingOuter, s.OuterRadius);
            Assert.Equal(WheelGeometry.RingInner, s.InnerRadius);
            if (n == 1) continue;
            Assert.Equal(WheelGeometry.RingOuter, Math.Sqrt(s.OuterStart.X * s.OuterStart.X + s.OuterStart.Y * s.OuterStart.Y), 9);
            Assert.Equal(WheelGeometry.RingOuter, Math.Sqrt(s.OuterEnd.X * s.OuterEnd.X + s.OuterEnd.Y * s.OuterEnd.Y), 9);
            Assert.Equal(WheelGeometry.RingInner, Math.Sqrt(s.InnerStart.X * s.InnerStart.X + s.InnerStart.Y * s.InnerStart.Y), 9);
            Assert.Equal(WheelGeometry.RingInner, Math.Sqrt(s.InnerEnd.X * s.InnerEnd.X + s.InnerEnd.Y * s.InnerEnd.Y), 9);
            Assert.True(s.OuterSweep > 0 && s.InnerSweep > 0);
            Assert.True(s.OuterSweep < WheelGeometry.StepAngle(n));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    public void NeighbouringTilesAreSeparatedByExactlyTheGapOnBothArcs(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var a = WheelGeometry.Segment(i, n);
            var b = WheelGeometry.Segment((i + 1) % n, n);
            Assert.Equal(WheelGeometry.TileGap, Distance(a.OuterEnd, b.OuterStart), 9);
            Assert.Equal(WheelGeometry.TileGap, Distance(a.InnerEnd, b.InnerStart), 9);
        }
    }

    [Fact]
    public void InsetShrinksRadiiAndWidensTheGap()
    {
        var plain = WheelGeometry.Segment(1, 8);
        var inset = WheelGeometry.Segment(1, 8, WheelGeometry.TileCornerRadius);
        Assert.Equal(plain.OuterRadius - 11, inset.OuterRadius, 9);
        Assert.Equal(plain.InnerRadius + 11, inset.InnerRadius, 9);
        var next = WheelGeometry.Segment(2, 8, WheelGeometry.TileCornerRadius);
        Assert.Equal(WheelGeometry.TileGap + 22, Distance(inset.OuterEnd, next.OuterStart), 9);
    }

    [Fact]
    public void SingleTileIsAFullRing()
    {
        var s = WheelGeometry.Segment(0, 1);
        Assert.True(s.IsFullRing);
        Assert.Equal(2 * Math.PI, s.OuterSweep);
        Assert.True(s.OuterIsLargeArc);
    }

    [Fact]
    public void LargeArcFlagFollowsTheSweep()
    {
        Assert.True(WheelGeometry.Segment(0, 2).OuterSweep < Math.PI); // the gap eats a little of the half circle
        Assert.False(WheelGeometry.Segment(0, 2).OuterIsLargeArc);
        Assert.False(WheelGeometry.Segment(0, 8).OuterIsLargeArc);
    }

    [Fact]
    public void SegmentRejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.Segment(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.Segment(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.Segment(-1, 5));
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
