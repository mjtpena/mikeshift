using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>The model's key hints (hub copy) and the single-tile state.</summary>
public class WheelModelKeyHintTests
{
    private static ActionDef Action(string title, WheelPage page) =>
        new(title, page, (_, _, _) => throw new NotSupportedException());

    private static WheelModel Create(WheelMode mode, params ActionDef[] actions)
    {
        var m = new WheelModel(mode);
        m.SetActions(actions);
        return m;
    }

    [Fact]
    public void DragModeHintsNameTheSwitchKey()
    {
        var m = Create(WheelMode.Drag, Action("PNG", WheelPage.Format), Action("Zip", WheelPage.Tool));
        Assert.Equal([new KeyHint("Ctrl", "Tools", 4)], m.KeyHints);
        Assert.Equal("Ctrl  Tools", m.HubHint);
        m.TogglePage();
        Assert.Equal([new KeyHint("Shift", "Formats", 4)], m.KeyHints);
        Assert.Equal("Shift  Formats", m.HubHint);
    }

    [Fact]
    public void ClickModeSwitchesPagesWithTab()
    {
        var m = Create(WheelMode.Click, Action("PNG", WheelPage.Format), Action("Zip", WheelPage.Tool));
        Assert.Equal([new KeyHint("Tab", "Tools", 4)], m.KeyHints);
        Assert.Equal("Tab  Tools", m.HubHint);
        m.HandleKey(WheelKey.Tab);
        Assert.Equal("Tab  Formats", m.HubHint);
    }

    [Fact]
    public void NoHintsWithOnlyOnePage()
    {
        foreach (var mode in new[] { WheelMode.Drag, WheelMode.Click })
        {
            var m = Create(mode, Action("Zip", WheelPage.Tool));
            Assert.Empty(m.KeyHints);
            Assert.Equal("", m.HubHint);
        }
    }

    [Fact]
    public void SingleTileIsDetectedPerPage()
    {
        var one = Create(WheelMode.Drag, Action("Zip", WheelPage.Tool));
        Assert.True(one.IsSingleTile);

        var many = Create(WheelMode.Drag, Action("PNG", WheelPage.Format), Action("JPG", WheelPage.Format), Action("Zip", WheelPage.Tool));
        Assert.False(many.IsSingleTile);
        many.TogglePage();
        Assert.True(many.IsSingleTile);       // the Tools page has just Zip
        Assert.False(Create(WheelMode.Drag).IsSingleTile);   // no tiles at all
    }

    [Fact]
    public void SingleTileIsHighlightedByTheHubHitTest()
    {
        var m = Create(WheelMode.Click, Action("Zip", WheelPage.Tool));
        var centre = new PointPx(500, 500);
        m.PointerMoved(new PointPx(500, 500 + 40), centre, 1.0);      // inside the hub button
        Assert.Equal(0, m.HighlightedIndex);
        Assert.Equal("Zip", m.CentreTitle);

        m.PointerMoved(new PointPx(500, 500 + 100), centre, 1.0);     // where a ring would be: nothing
        Assert.Null(m.HighlightedIndex);
        Assert.Equal("Tools", m.CentreTitle);
    }

    [Fact]
    public void SingleTileHubClickPicksIt()
    {
        var m = Create(WheelMode.Click, Action("Zip", WheelPage.Tool));
        ActionDef? picked = null;
        m.ActionPicked += a => picked = a;
        m.PointerMoved(new PointPx(510, 520), new PointPx(500, 500), 1.0);
        Assert.Equal("Zip", m.PickHighlighted()?.Title);
        Assert.Equal("Zip", picked?.Title);
    }

    [Fact]
    public void SingleTileKeyboardEnterPicksIt()
    {
        var m = Create(WheelMode.Click, Action("Zip", WheelPage.Tool));
        ActionDef? picked = null;
        m.ActionPicked += a => picked = a;
        m.HandleKey(WheelKey.Right);
        m.HandleKey(WheelKey.Enter);
        Assert.Equal("Zip", picked?.Title);
    }
}
