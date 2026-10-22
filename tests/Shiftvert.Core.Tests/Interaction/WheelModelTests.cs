using Shiftvert.Core.Actions;
using Shiftvert.Core.Interaction;

namespace Shiftvert.Core.Tests.Interaction;

public class WheelModelTests
{
    private static readonly PointPx Centre = new(500, 500);

    private static ActionDef Action(string title, WheelPage page) =>
        new(title, page, (_, _, _) => throw new NotSupportedException());

    private static IReadOnlyList<ActionDef> Mixed() =>
    [
        Action("PNG", WheelPage.Format),
        Action("JPG", WheelPage.Format),
        Action("WebP", WheelPage.Format),
        Action("Compress", WheelPage.Tool),
        Action("Resize 50%", WheelPage.Tool),
        Action("Zip", WheelPage.Tool),
    ];

    private static WheelModel Create(WheelMode mode = WheelMode.Click, ToolsKey key = ToolsKey.Ctrl, IReadOnlyList<ActionDef>? actions = null)
    {
        var m = new WheelModel(mode, key);
        m.SetActions(actions ?? Mixed());
        return m;
    }

    // Offset of the pointer from the centre in the middle of tile i of n (radius 100 px at 100 %).
    private static PointPx OverTile(int i, int n, double dpi = 1.0)
    {
        var p = WheelGeometry.PointAt(100 * dpi, WheelGeometry.SegmentCentreAngle(i, n));
        return new PointPx(Centre.X + (int)Math.Round(p.X), Centre.Y + (int)Math.Round(p.Y));
    }

    // ------------------------------------------------------------------ pages

    [Fact]
    public void ActionsAreSplitByPageKeepingCatalogueOrder()
    {
        var m = Create();
        Assert.Equal(["PNG", "JPG", "WebP"], m.Formats.Select(a => a.Title));
        Assert.Equal(["Compress", "Resize 50%", "Zip"], m.Tools.Select(a => a.Title));
        Assert.Equal(WheelPage.Format, m.Page);
        Assert.Same(m.Formats, m.CurrentActions);
        Assert.True(m.HasBothPages);
    }

    [Fact]
    public void ZipOnlySelectionStartsOnTheToolPage()
    {
        var m = Create(actions: [Action("Zip", WheelPage.Tool)]);
        Assert.Equal(WheelPage.Tool, m.Page);
        Assert.False(m.HasBothPages);
        Assert.Equal("TOOLS", m.CentreTitle);
        Assert.Equal("", m.HubHint);
    }

    [Fact]
    public void EmptyModelIsSafe()
    {
        var m = new WheelModel();
        Assert.Empty(m.CurrentActions);
        Assert.Null(m.HighlightedIndex);
        Assert.False(m.HandleKey(WheelKey.Right) && m.HighlightedIndex is not null);
        m.PointerMoved(new PointPx(500, 400), Centre, 1.0);
        Assert.Null(m.HighlightedIndex);
        Assert.Equal("CONVERT", m.CentreTitle);
    }

    [Fact]
    public void TogglePageSwitchesAndClearsTheHighlight()
    {
        var m = Create();
        m.Highlight(1);
        Assert.True(m.TogglePage());
        Assert.Equal(WheelPage.Tool, m.Page);
        Assert.Null(m.HighlightedIndex);
        Assert.True(m.TogglePage());
        Assert.Equal(WheelPage.Format, m.Page);
    }

    [Fact]
    public void TogglingToAnEmptyPageIsRefused()
    {
        var m = Create(actions: [Action("PNG", WheelPage.Format), Action("JPG", WheelPage.Format)]);
        Assert.False(m.TogglePage());
        Assert.False(m.SetPage(WheelPage.Tool));
        Assert.Equal(WheelPage.Format, m.Page);
    }

    [Fact]
    public void SetPageToTheCurrentPageSucceedsWithoutEvents()
    {
        var m = Create();
        var raised = 0;
        m.Changed += _ => raised++;
        Assert.True(m.SetPage(WheelPage.Format));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void ReplacingActionsResetsHighlightAndPage()
    {
        var m = Create();
        m.TogglePage();
        m.Highlight(2);
        m.SetActions([Action("MP3", WheelPage.Format)]);
        Assert.Equal(WheelPage.Format, m.Page);
        Assert.Null(m.HighlightedIndex);
        Assert.Single(m.CurrentActions);
    }

    [Fact]
    public void ActionsAreRequired() => Assert.Throws<ArgumentNullException>(() => new WheelModel().SetActions(null!));

    // ------------------------------------------------------------------ hub texts

    [Theory]
    [InlineData(ToolsKey.Ctrl, "Ctrl  tools")]
    [InlineData(ToolsKey.Alt, "Alt  tools")]
    [InlineData(ToolsKey.MouseWheelOnly, "Scroll  tools")]
    public void HubHintOnTheFormatPageNamesTheConfiguredToolsKey(ToolsKey key, string expected)
    {
        var m = Create(key: key);
        Assert.Equal(expected, m.HubHint);
    }

    [Theory]
    [InlineData(ToolsKey.Ctrl)]
    [InlineData(ToolsKey.Alt)]
    [InlineData(ToolsKey.MouseWheelOnly)]
    public void HubHintOnTheToolPageAlwaysSaysShiftFormats(ToolsKey key)
    {
        var m = Create(key: key);
        m.TogglePage();
        Assert.Equal("Shift  formats", m.HubHint);
    }

    [Fact]
    public void ChangingTheToolsKeyUpdatesTheHintAndRaisesAnEvent()
    {
        var m = Create();
        WheelChange seen = WheelChange.None;
        m.Changed += c => seen |= c;
        m.ToolsKey = ToolsKey.Alt;
        Assert.Equal("Alt  tools", m.HubHint);
        Assert.Equal(WheelChange.HubHint, seen);
        seen = WheelChange.None;
        m.ToolsKey = ToolsKey.Alt;
        Assert.Equal(WheelChange.None, seen);
    }

    [Fact]
    public void CentreTitleIsThePageNameUntilATileIsHovered()
    {
        var m = Create();
        Assert.Equal("CONVERT", m.CentreTitle);
        Assert.False(m.CentreIsActionName);
        m.Highlight(1);
        Assert.Equal("JPG", m.CentreTitle);
        Assert.True(m.CentreIsActionName);
        m.TogglePage();
        Assert.Equal("TOOLS", m.CentreTitle);
        m.Highlight(1);
        Assert.Equal("Resize 50%", m.CentreTitle);
    }

    [Fact]
    public void DropDescriptionFollowsTheHighlight()
    {
        var m = Create();
        Assert.Null(m.DropDescription);
        m.Highlight(0);
        Assert.Equal("Convert to PNG", m.DropDescription);
        m.TogglePage();
        m.Highlight(0);
        Assert.Equal("Compress", m.DropDescription);
    }

    // ------------------------------------------------------------------ pointer highlighting

    [Fact]
    public void HoveringATileHighlightsItAndLeavingTheHubClears()
    {
        var m = Create();
        m.PointerMoved(OverTile(1, 3), Centre, 1.0);
        Assert.Equal(1, m.HighlightedIndex);
        Assert.Equal("JPG", m.HighlightedAction!.Title);
        m.PointerMoved(new PointPx(Centre.X + 10, Centre.Y), Centre, 1.0); // hub
        Assert.Null(m.HighlightedIndex);
        m.PointerMoved(OverTile(2, 3), Centre, 1.0);
        Assert.Equal(2, m.HighlightedIndex);
        m.PointerLeft();
        Assert.Null(m.HighlightedIndex);
    }

    [Fact]
    public void HoveringBeyondTheOuterRadiusHighlightsNothing()
    {
        var m = Create();
        m.PointerMoved(new PointPx(Centre.X, Centre.Y - 181), Centre, 1.0);
        Assert.Null(m.HighlightedIndex);
    }

    [Fact]
    public void HitTestUsesTheCurrentPagesTileCount()
    {
        var m = Create(actions:
        [
            Action("A", WheelPage.Format), Action("B", WheelPage.Format),
            Action("C", WheelPage.Tool), Action("D", WheelPage.Tool), Action("E", WheelPage.Tool), Action("F", WheelPage.Tool),
        ]);
        var p = OverTile(1, 2); // lower-left half... tile 1 of 2
        m.PointerMoved(p, Centre, 1.0);
        Assert.Equal(1, m.HighlightedIndex);
        m.TogglePage(); // 4 tiles now; the pointer has not moved, but the new page is re-evaluated under it
        Assert.Equal(WheelGeometry.HitTest(p, Centre, 4, 1.0), m.HighlightedIndex);
    }

    [Fact]
    public void ActionsPopulatedAfterThePointerArrivedAreHitTestedImmediately()
    {
        var m = new WheelModel(WheelMode.Click);
        m.PointerMoved(OverTile(0, 3), Centre, 1.0); // wheel still pending: no tiles
        Assert.Null(m.HighlightedIndex);
        m.SetActions(Mixed());
        Assert.Equal(0, m.HighlightedIndex);
    }

    // ------------------------------------------------------------------ neutral zone

    [Fact]
    public void NothingHighlightsInsideTheNeutralZoneEvenOverATile()
    {
        var m = Create(WheelMode.Drag);
        var entry = OverTile(1, 3); // wheel clamped against a screen edge: the entry point sits on a tile
        m.BeginNeutralZone(entry);
        Assert.True(m.InNeutralZone);
        m.PointerMoved(entry, Centre, 1.0);
        Assert.Null(m.HighlightedIndex);
        m.PointerMoved(new PointPx(entry.X + 8, entry.Y + 8), Centre, 1.0); // 11.3 px: still inside 12
        Assert.Null(m.HighlightedIndex);
        Assert.True(m.InNeutralZone);
    }

    [Fact]
    public void LeavingTheNeutralZoneHighlightsAndNeverReturnsToNeutral()
    {
        var m = Create(WheelMode.Drag);
        var entry = OverTile(1, 3);
        m.BeginNeutralZone(entry);
        m.PointerMoved(new PointPx(entry.X + 13, entry.Y), Centre, 1.0);
        Assert.NotNull(m.HighlightedIndex);
        Assert.False(m.InNeutralZone);
        m.PointerMoved(entry, Centre, 1.0); // back where we entered: now it counts
        Assert.NotNull(m.HighlightedIndex);
    }

    [Theory]
    [InlineData(12, false)]  // radius is inclusive of the boundary: still neutral
    [InlineData(13, true)]
    public void NeutralRadiusBoundaryAtDpiOne(int distance, bool highlights)
    {
        var m = Create(WheelMode.Drag);
        var entry = OverTile(0, 3);
        m.BeginNeutralZone(entry);
        m.PointerMoved(new PointPx(entry.X + distance, entry.Y), Centre, 1.0);
        Assert.Equal(highlights, m.HighlightedIndex is not null);
    }

    [Theory]
    [InlineData(1.0, 12)]
    [InlineData(1.5, 18)]
    [InlineData(2.0, 24)]
    public void NeutralRadiusScalesWithDpi(double dpi, int radius)
    {
        var m = Create(WheelMode.Drag);
        var entry = OverTile(0, 3, dpi);
        m.BeginNeutralZone(entry);
        m.PointerMoved(new PointPx(entry.X + radius, entry.Y), Centre, dpi);
        Assert.Null(m.HighlightedIndex);
        m.PointerMoved(new PointPx(entry.X + radius + 1, entry.Y), Centre, dpi);
        Assert.NotNull(m.HighlightedIndex);
    }

    [Fact]
    public void AnEntryInTheHubStaysNeutralUntilTheHubIsLeftTowardATile()
    {
        var m = Create(WheelMode.Drag);
        m.BeginNeutralZone(Centre);
        m.PointerMoved(new PointPx(Centre.X + 5, Centre.Y), Centre, 1.0);
        Assert.Null(m.HighlightedIndex);
        m.PointerMoved(OverTile(2, 3), Centre, 1.0);
        Assert.Equal(2, m.HighlightedIndex);
    }

    [Fact]
    public void ClickModeHasNoNeutralZone()
    {
        var m = Create(WheelMode.Click);
        m.PointerMoved(OverTile(0, 3), Centre, 1.0);
        Assert.Equal(0, m.HighlightedIndex);
        Assert.False(m.InNeutralZone);
    }

    [Fact]
    public void NeutralZoneIsStillActiveAfterActionsArrive()
    {
        var m = new WheelModel(WheelMode.Drag);
        var entry = OverTile(0, 3);
        m.BeginNeutralZone(entry);
        m.SetActions(Mixed());
        Assert.True(m.InNeutralZone);
        Assert.Null(m.HighlightedIndex);
    }

    [Fact]
    public void PageToggleInsideTheNeutralZoneHighlightsNothing()
    {
        var m = Create(WheelMode.Drag);
        var entry = OverTile(0, 3);
        m.BeginNeutralZone(entry);
        m.PointerMoved(entry, Centre, 1.0);
        m.TogglePage();
        Assert.Null(m.HighlightedIndex);
        Assert.True(m.InNeutralZone);
    }

    [Fact]
    public void NeutralRadiusConstantIsTwelveLogicalPixels() => Assert.Equal(12, WheelGeometry.NeutralRadius);

    // ------------------------------------------------------------------ keyboard

    [Fact]
    public void RightAndDownMoveClockwiseAndWrapAround()
    {
        var m = Create();
        m.HandleKey(WheelKey.Right);
        Assert.Equal(0, m.HighlightedIndex);
        m.HandleKey(WheelKey.Down);
        Assert.Equal(1, m.HighlightedIndex);
        m.HandleKey(WheelKey.Right);
        Assert.Equal(2, m.HighlightedIndex);
        m.HandleKey(WheelKey.Right);
        Assert.Equal(0, m.HighlightedIndex);
    }

    [Fact]
    public void LeftAndUpMoveCounterClockwiseAndWrapAround()
    {
        var m = Create();
        m.HandleKey(WheelKey.Left);
        Assert.Equal(2, m.HighlightedIndex); // from nothing: the last tile
        m.HandleKey(WheelKey.Up);
        Assert.Equal(1, m.HighlightedIndex);
        m.HandleKey(WheelKey.Left);
        Assert.Equal(0, m.HighlightedIndex);
        m.HandleKey(WheelKey.Left);
        Assert.Equal(2, m.HighlightedIndex);
    }

    [Fact]
    public void SingleTileWheelWrapsOntoItself()
    {
        var m = Create(actions: [Action("Zip", WheelPage.Tool)]);
        m.HandleKey(WheelKey.Right);
        m.HandleKey(WheelKey.Right);
        m.HandleKey(WheelKey.Left);
        Assert.Equal(0, m.HighlightedIndex);
    }

    [Fact]
    public void TabTogglesPageAndClearsHighlight()
    {
        var m = Create();
        m.HandleKey(WheelKey.Right);
        Assert.True(m.HandleKey(WheelKey.Tab));
        Assert.Equal(WheelPage.Tool, m.Page);
        Assert.Null(m.HighlightedIndex);
        m.HandleKey(WheelKey.Tab);
        Assert.Equal(WheelPage.Format, m.Page);
    }

    [Fact]
    public void TabIsConsumedButHarmlessWithOnePage()
    {
        var m = Create(actions: [Action("Zip", WheelPage.Tool)]);
        Assert.True(m.HandleKey(WheelKey.Tab));
        Assert.Equal(WheelPage.Tool, m.Page);
    }

    [Fact]
    public void EnterPicksTheHighlightedTile()
    {
        var m = Create();
        ActionDef? picked = null;
        m.ActionPicked += a => picked = a;
        m.HandleKey(WheelKey.Right);
        m.HandleKey(WheelKey.Right);
        Assert.True(m.HandleKey(WheelKey.Enter));
        Assert.Equal("JPG", picked?.Title);
    }

    [Fact]
    public void EnterWithNothingHighlightedPicksNothing()
    {
        var m = Create();
        var picks = 0;
        m.ActionPicked += _ => picks++;
        Assert.True(m.HandleKey(WheelKey.Enter));
        Assert.Equal(0, picks);
        Assert.Null(m.PickHighlighted());
    }

    [Fact]
    public void MouseDownCommitsTheHoveredTileViaPickHighlighted()
    {
        var m = Create();
        ActionDef? picked = null;
        m.ActionPicked += a => picked = a;
        m.PointerMoved(OverTile(2, 3), Centre, 1.0);
        Assert.Equal("WebP", m.PickHighlighted()?.Title);
        Assert.Equal("WebP", picked?.Title);
    }

    [Fact]
    public void EscapeCancels()
    {
        var m = Create();
        var cancelled = 0;
        m.Cancelled += () => cancelled++;
        Assert.True(m.HandleKey(WheelKey.Escape));
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void KeyboardNavigationIsNotOverriddenByAnUnmovedPointer()
    {
        var m = Create();
        var p = OverTile(0, 3);
        m.PointerMoved(p, Centre, 1.0);
        m.HandleKey(WheelKey.Right);
        Assert.Equal(1, m.HighlightedIndex);
        m.PointerMoved(p, Centre, 1.0); // WM_MOUSEMOVE with an unchanged position
        Assert.Equal(1, m.HighlightedIndex);
        m.PointerMoved(OverTile(2, 3), Centre, 1.0); // real movement takes over again
        Assert.Equal(2, m.HighlightedIndex);
    }

    [Fact]
    public void KeyboardEndsTheNeutralZone()
    {
        var m = Create(WheelMode.Drag);
        m.BeginNeutralZone(OverTile(0, 3));
        m.HandleKey(WheelKey.Right);
        Assert.False(m.InNeutralZone);
        Assert.Equal(0, m.HighlightedIndex);
    }

    [Theory]
    [InlineData(VirtualKeys.Left, WheelKey.Left)]
    [InlineData(VirtualKeys.Right, WheelKey.Right)]
    [InlineData(VirtualKeys.Up, WheelKey.Up)]
    [InlineData(VirtualKeys.Down, WheelKey.Down)]
    [InlineData(VirtualKeys.Tab, WheelKey.Tab)]
    [InlineData(VirtualKeys.Return, WheelKey.Enter)]
    [InlineData(VirtualKeys.Escape, WheelKey.Escape)]
    public void VirtualKeysMapToWheelKeys(int vk, WheelKey expected)
    {
        Assert.True(WheelKeys.TryFromVirtualKey(vk, out var key));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void OtherVirtualKeysAreNotMapped() => Assert.False(WheelKeys.TryFromVirtualKey(0x41, out _));

    // ------------------------------------------------------------------ change events

    [Fact]
    public void HighlightChangesRaiseHighlightAndCentreText()
    {
        var m = Create();
        var events = new List<WheelChange>();
        m.Changed += events.Add;
        m.Highlight(1);
        var e = Assert.Single(events);
        Assert.True(e.HasFlag(WheelChange.Highlight));
        Assert.True(e.HasFlag(WheelChange.CentreText));
        Assert.False(e.HasFlag(WheelChange.Page));
        m.Highlight(1);
        Assert.Single(events); // unchanged: silent
    }

    [Fact]
    public void PageChangesRaisePageHubHintCentreTextAndHighlight()
    {
        var m = Create();
        m.Highlight(0);
        var events = new List<WheelChange>();
        m.Changed += events.Add;
        m.TogglePage();
        var e = Assert.Single(events);
        Assert.True(e.HasFlag(WheelChange.Page | WheelChange.HubHint | WheelChange.CentreText | WheelChange.Highlight));
    }

    [Fact]
    public void SetActionsRaisesActionsAndEverythingDerived()
    {
        var m = new WheelModel();
        WheelChange seen = WheelChange.None;
        m.Changed += c => seen |= c;
        m.SetActions(Mixed());
        Assert.True(seen.HasFlag(WheelChange.Actions | WheelChange.Page | WheelChange.HubHint | WheelChange.CentreText));
    }

    [Fact]
    public void HighlightRejectsOutOfRangeIndices()
    {
        var m = Create();
        m.Highlight(7);
        Assert.Null(m.HighlightedIndex);
        m.Highlight(-1);
        Assert.Null(m.HighlightedIndex);
    }
}
