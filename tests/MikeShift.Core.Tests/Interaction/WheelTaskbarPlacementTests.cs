using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>With the pointer over a taskbar the wheel is anchored beside it so the opaque disc never sits under the shell's topmost taskbar.</summary>
public class WheelTaskbarPlacementTests
{
    private const int Size = 380;                       // logical 380 at 100 %
    private const int Margin = (int)WheelGeometry.WindowMargin;   // 40: the transparent margin around the disc
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);
    private static readonly ScreenRect WorkBottom = new(0, 0, 1920, 1032);   // 48 px taskbar at the bottom
    private static readonly ScreenRect WorkTop = new(0, 48, 1920, 1080);
    private static readonly ScreenRect WorkLeft = new(48, 0, 1920, 1080);
    private static readonly ScreenRect WorkRight = new(0, 0, 1872, 1080);

    /// <summary>The disc's bounding rectangle for a window.</summary>
    private static ScreenRect DiscOf(ScreenRect window) =>
        new(window.Left + Margin, window.Top + Margin, window.Right - Margin, window.Bottom - Margin);

    [Theory]
    [InlineData(960, 1033)]   // top edge of the taskbar
    [InlineData(960, 1056)]   // middle
    [InlineData(960, 1079)]   // last pixel row
    [InlineData(5, 1056)]     // left end
    [InlineData(1915, 1056)]  // right end
    public void BottomTaskbar_DiscSitsAboveTheTaskbarAndThePointerStaysInside(int x, int y)
    {
        var cursor = new PointPx(x, y);
        var r = WheelPlacement.ForCursor(cursor, Size, WorkBottom, Monitor);

        Assert.True(r.Contains(cursor), $"{r} must contain {cursor}");
        Assert.Equal(Math.Min(y + WheelPlacement.TaskbarAnchorGap, Monitor.Bottom), r.Bottom);   // far edge 4 px past the pointer (or the monitor edge)
        Assert.True(DiscOf(r).Bottom <= WorkBottom.Bottom + 12, "at most the rim of the disc overlaps the taskbar");
        // The tile ring (r = 139 of 150) is 11 px inside the disc: it is never covered.
        Assert.True(r.Bottom - Margin - 11 <= WorkBottom.Bottom, "the tiles never reach the taskbar");
        Assert.True(r.Left >= Monitor.Left && r.Right <= Monitor.Right);
    }

    [Theory]
    [InlineData(960, 2)]
    [InlineData(960, 20)]
    [InlineData(1915, 47)]
    public void TopTaskbar_DiscSitsBelowTheTaskbar(int x, int y)
    {
        var cursor = new PointPx(x, y);
        var r = WheelPlacement.ForCursor(cursor, Size, WorkTop, Monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(Math.Max(0, y - WheelPlacement.TaskbarAnchorGap), r.Top);
        Assert.True(DiscOf(r).Top >= WorkTop.Top - 12);
        Assert.True(r.Top + Margin + 11 >= WorkTop.Top, "the tiles never reach the taskbar");
    }

    [Theory]
    [InlineData(2, 540)]
    [InlineData(24, 540)]
    [InlineData(47, 1070)]
    public void LeftTaskbar_DiscSitsRightOfTheTaskbar(int x, int y)
    {
        var cursor = new PointPx(x, y);
        var r = WheelPlacement.ForCursor(cursor, Size, WorkLeft, Monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(Math.Max(0, x - WheelPlacement.TaskbarAnchorGap), r.Left);
        Assert.True(r.Left + Margin + 11 >= WorkLeft.Left, "the tiles never reach the taskbar");
        Assert.True(r.Top >= 0 && r.Bottom <= Monitor.Bottom);
    }

    [Theory]
    [InlineData(1873, 540)]
    [InlineData(1896, 540)]
    [InlineData(1919, 10)]
    public void RightTaskbar_DiscSitsLeftOfTheTaskbar(int x, int y)
    {
        var cursor = new PointPx(x, y);
        var r = WheelPlacement.ForCursor(cursor, Size, WorkRight, Monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(Math.Min(x + WheelPlacement.TaskbarAnchorGap, Monitor.Right), r.Right);
        Assert.True(r.Right - Margin - 11 <= WorkRight.Right, "the tiles never reach the taskbar");
        Assert.True(r.Top >= 0);
    }

    [Fact]
    public void EdgesAreDetectedPerAxis()
    {
        Assert.Equal(TaskbarEdges.Bottom, WheelPlacement.EdgesOver(new PointPx(960, 1050), WorkBottom, Monitor));
        Assert.Equal(TaskbarEdges.Top, WheelPlacement.EdgesOver(new PointPx(960, 10), WorkTop, Monitor));
        Assert.Equal(TaskbarEdges.Left, WheelPlacement.EdgesOver(new PointPx(10, 500), WorkLeft, Monitor));
        Assert.Equal(TaskbarEdges.Right, WheelPlacement.EdgesOver(new PointPx(1900, 500), WorkRight, Monitor));
        Assert.Equal(TaskbarEdges.None, WheelPlacement.EdgesOver(new PointPx(960, 500), WorkBottom, Monitor));
        Assert.Equal(TaskbarEdges.None, WheelPlacement.EdgesOver(new PointPx(960, 1031), WorkBottom, Monitor));
        Assert.Equal(TaskbarEdges.None, WheelPlacement.EdgesOver(new PointPx(5000, 5000), WorkBottom, Monitor));   // not on this monitor at all
    }

    [Fact]
    public void NoTaskbarMeansTheOldCentredPlacement()
    {
        var r = WheelPlacement.ForCursor(new PointPx(960, 500), Size, WorkBottom, Monitor);
        Assert.Equal(new ScreenRect(770, 310, 1150, 690), r);
    }

    // ---- monitors left of / above the primary (negative virtual-screen coordinates)

    [Fact]
    public void NegativeCoordinates_BottomTaskbarOnALeftMonitor()
    {
        var monitor = new ScreenRect(-1920, -100, 0, 980);          // 1920x1080, top at -100
        var work = new ScreenRect(-1920, -100, 0, 932);             // taskbar: y 932..980
        var cursor = new PointPx(-960, 960);
        var r = WheelPlacement.ForCursor(cursor, Size, work, monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(964, r.Bottom);
        Assert.Equal(584, r.Top);
        Assert.Equal(-960 - Size / 2, r.Left);
        Assert.True(r.Bottom - Margin - 11 <= work.Bottom);
    }

    [Fact]
    public void NegativeCoordinates_TopTaskbarOnAMonitorAbovePrimary()
    {
        var monitor = new ScreenRect(0, -1080, 1920, 0);
        var work = new ScreenRect(0, -1032, 1920, 0);               // taskbar y -1080..-1032
        var cursor = new PointPx(1000, -1060);
        var r = WheelPlacement.ForCursor(cursor, Size, work, monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(-1064, r.Top);
        Assert.True(r.Top >= monitor.Top);
        Assert.Equal(1000 - Size / 2, r.Left);
    }

    [Fact]
    public void NegativeCoordinates_LeftTaskbarOnAFarLeftMonitor()
    {
        var monitor = new ScreenRect(-3840, 0, -1920, 1080);
        var work = new ScreenRect(-3792, 0, -1920, 1080);           // taskbar x -3840..-3792
        var cursor = new PointPx(-3830, 500);
        var r = WheelPlacement.ForCursor(cursor, Size, work, monitor);

        Assert.True(r.Contains(cursor));
        Assert.Equal(-3834, r.Left);
        Assert.True(r.Left >= monitor.Left);
        Assert.Equal(500 - Size / 2, r.Top);
    }

    [Fact]
    public void HigherDpiUsesAWiderPhysicalGapAndTheSameProportions()
    {
        const double dpi = 2.0;
        var size = WheelGeometry.WindowSizePx(dpi);                 // 760
        var monitor = new ScreenRect(0, 0, 3840, 2160);
        var work = new ScreenRect(0, 0, 3840, 2064);                // 96 px taskbar
        var gap = (int)Math.Ceiling(WheelPlacement.TaskbarAnchorGap * dpi);
        var cursor = new PointPx(1920, 2112);
        var r = WheelPlacement.ForCursor(cursor, size, work, monitor, gap);

        Assert.Equal(2112 + gap, r.Bottom);
        Assert.True(r.Bottom - (int)(WheelGeometry.WindowMargin * dpi) - (int)(11 * dpi) <= work.Bottom);
    }

    [Fact]
    public void WindowLargerThanTheMonitorStillAlignsToItsTopLeft()
    {
        var tinyMonitor = new ScreenRect(0, 0, 300, 300);
        var tinyWork = new ScreenRect(0, 0, 300, 270);
        var r = WheelPlacement.ForCursor(new PointPx(150, 290), Size, tinyWork, tinyMonitor);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    // ---- the neutral zone with a pointer that enters outside the hit ring (the wheel is anchored beside a taskbar)

    [Fact]
    public void PointerEnteringOutsideTheHitRingHighlightsAsSoonAsItEntersTheRing()
    {
        var model = new WheelModel(WheelMode.Drag);
        var png = new MikeShift.Core.Actions.ActionDef("PNG", MikeShift.Core.Actions.WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var jpg = new MikeShift.Core.Actions.ActionDef("JPG", MikeShift.Core.Actions.WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var centre = new PointPx(500, 500);

        model.BeginNeutralZone(new PointPx(500, 686));     // 186 px below the hub: beyond HitOuterRadius (180)
        model.SetActions([png, jpg]);
        model.PointerMoved(new PointPx(500, 686), centre, 1.0);
        Assert.Null(model.HighlightedIndex);
        Assert.False(model.InNeutralZone, "nothing to protect: the entry point is not on a tile");

        model.PointerMoved(new PointPx(500, 600), centre, 1.0);   // 100 px: on the ring, straight down = second tile of two
        Assert.Equal(1, model.HighlightedIndex);
    }

    [Fact]
    public void PointerEnteringOnATileStillGetsTheNeutralZone()
    {
        var model = new WheelModel(WheelMode.Drag);
        var png = new MikeShift.Core.Actions.ActionDef("PNG", MikeShift.Core.Actions.WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var jpg = new MikeShift.Core.Actions.ActionDef("JPG", MikeShift.Core.Actions.WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var centre = new PointPx(500, 500);

        model.BeginNeutralZone(new PointPx(500, 600));
        model.SetActions([png, jpg]);
        model.PointerMoved(new PointPx(503, 604), centre, 1.0);   // 5 px from the entry point: still neutral
        Assert.Null(model.HighlightedIndex);
        Assert.True(model.InNeutralZone);
    }
}
