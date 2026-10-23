using Shiftvert.Core.Actions;
using Shiftvert.Core.Interaction;

namespace Shiftvert.Core.Tests.Interaction;

public class WheelPlacementTests
{
    private static readonly ScreenRect Primary = new(0, 0, 1920, 1040);   // work area (taskbar excluded)
    private static readonly ScreenRect LeftMonitor = new(-1920, -200, 0, 880);

    [Fact]
    public void CentredOnCursorWhenThereIsRoom()
    {
        var r = WheelPlacement.ForCursor(new PointPx(960, 500), 380, Primary);
        Assert.Equal(new ScreenRect(770, 310, 1150, 690), r);
        Assert.Equal(new PointPx(960, 500), r.Centre);
    }

    [Theory]
    [InlineData(5, 5, 0, 0)]
    [InlineData(1915, 1035, 1540, 660)]
    [InlineData(5, 500, 0, 310)]
    [InlineData(960, 1030, 770, 660)]
    public void ClampedToTheWorkAreaAtEdgesAndCorners(int x, int y, int left, int top)
    {
        var r = WheelPlacement.ForCursor(new PointPx(x, y), 380, Primary);
        Assert.Equal(left, r.Left);
        Assert.Equal(top, r.Top);
        Assert.Equal(380, r.Width);
        Assert.Equal(380, r.Height);
    }

    // Found by the GUI "test in anger" run (2026-10-23): Shift pressed with the cursor over the taskbar placed the wheel inside the work area
    // and therefore NOT under the cursor, so OLE never delivered DragEnter and the wheel silently hid.
    private static readonly ScreenRect PrimaryBounds = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(960, 1070)]   // taskbar at the bottom
    [InlineData(1915, 1078)]  // the very corner of the taskbar
    [InlineData(5, 1042)]
    public void CursorOverTheTaskbarStaysInsideTheWindow(int x, int y)
    {
        var cursor = new PointPx(x, y);
        var r = WheelPlacement.ForCursor(cursor, 380, Primary, PrimaryBounds);
        Assert.True(r.Contains(cursor), $"{r} must contain {cursor}");
        Assert.True(r.Left >= PrimaryBounds.Left && r.Right <= PrimaryBounds.Right && r.Top >= PrimaryBounds.Top && r.Bottom <= PrimaryBounds.Bottom);
        Assert.Equal(380, r.Width);
    }

    [Fact]
    public void CursorOverAVerticalTaskbarStaysInsideTheWindow()
    {
        var work = new ScreenRect(48, 0, 1920, 1080);   // taskbar on the left
        var r = WheelPlacement.ForCursor(new PointPx(10, 500), 380, work, PrimaryBounds);
        Assert.True(r.Contains(new PointPx(10, 500)));
        Assert.Equal(0, r.Left);
    }

    [Fact]
    public void CursorInsideTheWorkAreaStillClampsToTheWorkArea()
    {
        var r = WheelPlacement.ForCursor(new PointPx(960, 1030), 380, Primary, PrimaryBounds);
        Assert.Equal(660, r.Top);
        Assert.Equal(1040, r.Bottom);
    }

    [Fact]
    public void NegativeCoordinatesOnAMonitorLeftOfThePrimary()
    {
        var r = WheelPlacement.ForCursor(new PointPx(-1900, -190), 475, LeftMonitor);
        Assert.Equal(-1920, r.Left);
        Assert.Equal(-200, r.Top);

        var middle = WheelPlacement.ForCursor(new PointPx(-960, 340), 475, LeftMonitor);
        Assert.Equal(-960 - 237, middle.Left);
    }

    [Fact]
    public void WindowLargerThanTheWorkAreaIsAlignedTopLeft()
    {
        var tiny = new ScreenRect(100, 100, 300, 300);
        var r = WheelPlacement.ForCursor(new PointPx(200, 200), 380, tiny);
        Assert.Equal(100, r.Left);
        Assert.Equal(100, r.Top);
    }

    [Fact]
    public void ClickModeCentresInTheMonitor()
    {
        var r = WheelPlacement.CentredIn(Primary, 380);
        Assert.Equal(new PointPx(960, 520), r.Centre);
        Assert.Equal(380, r.Width);
    }

    [Fact]
    public void CollapseWindowGrowsAroundTheSameCentre()
    {
        var w = new ScreenRect(770, 310, 1150, 690);
        var size = WheelPlacement.CollapseWindowSizePx(1.0);
        Assert.Equal(646, size);
        var grown = WheelPlacement.Resized(w, size);
        Assert.Equal(w.Centre, grown.Centre);
        Assert.Equal(size, grown.Width);
        Assert.InRange(WheelPlacement.CollapseWindowSizePx(1.5), 969, 970);
    }

    [Fact]
    public void DropDescriptionSplitsMessageAndInsert()
    {
        var png = new ActionDef("PNG", WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var compress = new ActionDef("Compress", WheelPage.Tool, (_, _, _) => throw new NotSupportedException());

        Assert.Equal(new DropDescriptionText("Convert to %1", "PNG"), DropDescriptionText.For(png));
        Assert.Equal("Convert to PNG", DropDescriptionText.For(png).Rendered);
        Assert.Equal(png.DropDescription, DropDescriptionText.For(png).Rendered);

        Assert.Equal(new DropDescriptionText("Compress", ""), DropDescriptionText.For(compress));
        Assert.Equal(compress.DropDescription, DropDescriptionText.For(compress).Rendered);
    }
}
