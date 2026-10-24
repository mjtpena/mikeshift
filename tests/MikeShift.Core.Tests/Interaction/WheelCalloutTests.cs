using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>Where the callout pill goes and what it says.</summary>
public class WheelCalloutTests
{
    private static ActionDef Action(string title, WheelPage page) =>
        new(title, page, (_, _, _) => throw new NotSupportedException());

    private static IReadOnlyList<ActionDef> Mixed() =>
    [
        Action("PNG", WheelPage.Format), Action("JPG", WheelPage.Format), Action("WebP", WheelPage.Format),
        Action("Compress", WheelPage.Tool), Action("Resize 50%", WheelPage.Tool), Action("Zip", WheelPage.Tool),
    ];

    private static WheelModel Model(WheelMode mode = WheelMode.Drag, IReadOnlyList<ActionDef>? actions = null, ToolsKey key = ToolsKey.Ctrl)
    {
        var m = new WheelModel(mode, key);
        m.SetActions(actions ?? Mixed());
        return m;
    }

    // ------------------------------------------------------------------ side

    [Theory]
    [InlineData(-150, CalloutSide.Bottom)]   // pointer near the top of the disc: the pill goes below
    [InlineData(-30, CalloutSide.Bottom)]
    [InlineData(30, CalloutSide.Top)]        // pointer in the lower half: the pill goes above
    [InlineData(186, CalloutSide.Top)]       // pointer over a taskbar under the disc
    public void ThePillIsOnTheSideOppositeThePointer(double dy, CalloutSide expected) =>
        Assert.Equal(expected, WheelCallout.SideFor(dy));

    [Fact]
    public void WithNothingKnownThePointerCountsAsLowerHalf() => Assert.Equal(CalloutSide.Top, WheelCallout.SideFor(0));

    [Theory]
    [InlineData(-5, CalloutSide.Top)]
    [InlineData(5, CalloutSide.Top)]
    [InlineData(-14, CalloutSide.Top)]
    public void NearTheCentreLineThePreviousSideIsKept(double dy, CalloutSide previous) =>
        Assert.Equal(previous, WheelCallout.SideFor(dy, previous));

    [Theory]
    [InlineData(-5, CalloutSide.Bottom)]
    [InlineData(5, CalloutSide.Bottom)]
    public void NearTheCentreLineABottomPillStaysBottom(double dy, CalloutSide previous) =>
        Assert.Equal(previous, WheelCallout.SideFor(dy, previous));

    [Fact]
    public void PastTheDeadZoneThePillMoves()
    {
        Assert.Equal(CalloutSide.Bottom, WheelCallout.SideFor(-(WheelCallout.SideDeadZone + 1), CalloutSide.Top));
        Assert.Equal(CalloutSide.Top, WheelCallout.SideFor(WheelCallout.SideDeadZone + 1, CalloutSide.Bottom));
    }

    [Fact]
    public void NoJitterWhileThePointerWigglesAcrossTheCentreLine()
    {
        CalloutSide? side = null;
        var changes = 0;
        foreach (var dy in new double[] { 40, 20, 10, -5, 8, -9, 3, -11, 12, 25 })
        {
            var next = WheelCallout.SideFor(dy, side);
            if (side is not null && next != side) changes++;
            side = next;
        }
        Assert.Equal(0, changes);
    }

    [Fact]
    public void PillBandsAreCentredInTheTopAndBottomMargins()
    {
        Assert.Equal(20, WheelCallout.BandCentreY(CalloutSide.Top));
        Assert.Equal(360, WheelCallout.BandCentreY(CalloutSide.Bottom));
        Assert.Equal(40, WheelGeometry.WindowMargin);
    }

    // ------------------------------------------------------------------ drag content

    [Fact]
    public void HighlightedFormatNamesTheTileTheSameWayTheShellDoes()
    {
        var m = Model();
        m.Highlight(0);
        var c = WheelCallout.ForDrag(m, virtualFiles: false, HintEmphasis.Prominent)!;
        Assert.Equal("Convert to PNG", c.Text);
        Assert.Equal(m.HighlightedAction!.DropDescription, c.Text);
        Assert.Equal(CalloutTone.Normal, c.Tone);
    }

    [Fact]
    public void HighlightedToolShowsItsPlainName()
    {
        var m = Model();
        m.TogglePage();
        m.Highlight(1);
        Assert.Equal("Resize 50%", WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!.Text);
    }

    [Fact]
    public void ProminentHintsAreShownIdleAndWhileHighlighted()
    {
        var m = Model();
        var idle = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!;
        Assert.Null(idle.Text);
        Assert.Equal([new KeyHint("Ctrl", "Tools", 4)], idle.KeyHints);
        Assert.Equal(HintEmphasis.Prominent, idle.Emphasis);

        m.Highlight(2);
        var hot = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!;
        Assert.Equal("Convert to WebP", hot.Text);
        Assert.Single(hot.KeyHints);
    }

    [Fact]
    public void QuietHintsOnlyShowWhileNothingIsHighlighted()
    {
        var m = Model();
        var idle = WheelCallout.ForDrag(m, false, HintEmphasis.Quiet)!;
        Assert.Single(idle.KeyHints);
        Assert.Equal(HintEmphasis.Quiet, idle.Emphasis);

        m.Highlight(0);
        var hot = WheelCallout.ForDrag(m, false, HintEmphasis.Quiet)!;
        Assert.Equal("Convert to PNG", hot.Text);
        Assert.Empty(hot.KeyHints);
    }

    [Theory]
    [InlineData(ToolsKey.Ctrl, "Ctrl")]
    [InlineData(ToolsKey.Alt, "Alt")]
    [InlineData(ToolsKey.MouseWheelOnly, "Scroll")]
    public void TheHintNamesTheConfiguredToolsKey(ToolsKey key, string cap)
    {
        var c = WheelCallout.ForDrag(Model(key: key), false, HintEmphasis.Prominent)!;
        Assert.Equal(cap, c.KeyHints[0].Key);
        Assert.Equal("Tools", c.KeyHints[0].Label);
    }

    [Fact]
    public void ToolPageHintSaysShiftFormats()
    {
        var m = Model();
        m.TogglePage();
        var c = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!;
        Assert.Equal(new KeyHint("Shift", "Formats", 4), c.KeyHints[0]);
    }

    [Fact]
    public void NoHintWhenTheOtherPageIsEmpty()
    {
        var m = Model(actions: [Action("PNG", WheelPage.Format), Action("JPG", WheelPage.Format)]);
        Assert.Null(WheelCallout.ForDrag(m, false, HintEmphasis.Prominent));   // nothing to say until a tile is highlighted
        m.Highlight(1);
        Assert.Equal("Convert to JPG", WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!.Text);
    }

    [Fact]
    public void PendingWheelHasNoPill() => Assert.Null(WheelCallout.ForDrag(Model(actions: []), false, HintEmphasis.Prominent));

    // ------------------------------------------------------------------ virtual files (W-02) and the single tile (W-09)

    [Fact]
    public void VirtualFilesGetACautionPillWithTheInfoGlyph()
    {
        var c = WheelCallout.ForDrag(Model(actions: []), virtualFiles: true, HintEmphasis.Prominent)!;
        Assert.Equal("Save the file first, then drag it", c.Text);
        Assert.Equal(CalloutTone.Caution, c.Tone);
        Assert.Equal("", c.Glyph);
        Assert.Empty(c.KeyHints);
    }

    [Fact]
    public void ShellDropDescriptionAndPillSayTheSameThing()
    {
        Assert.Equal(WheelText.SaveFirst, DropDescriptionText.SaveFirst.Rendered);
        Assert.Equal("Save the file first, then drag it", DropDescriptionText.SaveFirst.Message);
        Assert.Equal("", DropDescriptionText.SaveFirst.Insert);
    }

    [Fact]
    public void SingleTileWheelExplainsWhy()
    {
        var m = Model(actions: [Action("Zip", WheelPage.Tool)]);
        Assert.True(m.IsSingleTile);
        var c = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!;
        Assert.Equal("Only Zip works for these files", c.Text);
        Assert.Empty(c.KeyHints);

        m.Highlight(0);   // hovering the hub button keeps the explanation
        Assert.Equal("Only Zip works for these files", WheelCallout.ForDrag(m, false, HintEmphasis.Quiet)!.Text);
    }

    // ------------------------------------------------------------------ Word is off (a setting explains the missing tiles)

    [Fact]
    public void ADocWithWordOffGetsTheTurnOnWordPillInsteadOfOnlyZip()
    {
        using var tmp = new TempDir();
        var paths = new[] { tmp.WriteFile("old.doc", Fixtures.Noise(64)) };
        var caps = Capabilities.All.WithWord(false);
        var note = WheelText.WordNote(paths, caps);
        Assert.Equal("Turn on Microsoft Word in Settings to convert .doc files", note);

        var m = Model(actions: ActionCatalog.For(paths, caps));
        Assert.True(m.IsSingleTile);
        var c = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent, note)!;
        Assert.Equal(WheelText.TurnOnWord, c.Text);
        Assert.Equal(CalloutTone.Caution, c.Tone);
        Assert.Equal(WheelCallout.InfoGlyph, c.Glyph);
        Assert.Equal("Only Zip works for these files", WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!.Text); // no note: the old line

        var click = WheelCallout.ForClick(Model(WheelMode.Click, ActionCatalog.For(paths, caps)), "old.doc", note);
        Assert.Equal(WheelText.TurnOnWord, click.Main!.Text);
        Assert.Equal(CalloutTone.Caution, click.Main.Tone);
        Assert.NotEmpty(click.Secondary!.KeyHints); // the keys stay
    }

    [Fact]
    public void TheWordNoteOnlyAppearsWhenTurningWordOnWouldHelp()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("old.doc", Fixtures.Noise(64));
        var docx = tmp.WriteFile("a.docx", Fixtures.DocxSample());
        Assert.NotNull(WheelText.WordNote([doc], Capabilities.All.WithWord(false)));
        Assert.NotNull(WheelText.WordNote([doc, doc], Capabilities.All.WithWord(false)));
        Assert.Null(WheelText.WordNote([doc], Capabilities.All));                       // Word on: the tiles are there
        Assert.Null(WheelText.WordNote([doc], Capabilities.None));                      // Word not installed: the setting would change nothing
        Assert.Null(WheelText.WordNote([docx], Capabilities.All.WithWord(false)));      // docx never needs Word
        Assert.Null(WheelText.WordNote([doc, tmp.WriteFile("a.png", Fixtures.Png())], Capabilities.All.WithWord(false))); // mixed: only Zip either way
    }

    [Fact]
    public void ANoteNeverReplacesRealFormatTiles()
    {
        var m = Model();
        Assert.NotEqual(WheelText.TurnOnWord, WheelCallout.ForDrag(m, false, HintEmphasis.Prominent, WheelText.TurnOnWord)!.Text);
        Assert.NotEqual(WheelText.TurnOnWord, WheelCallout.ForClick(m, "3 images", WheelText.TurnOnWord).Main!.Text);
    }

    [Fact]
    public void ASingleFormatWithToolsAvailableIsNotTheOnlyOptionPill()
    {
        var m = Model(actions: [Action("PNG", WheelPage.Format), Action("Zip", WheelPage.Tool)]);
        Assert.True(m.IsSingleTile);
        Assert.True(m.HasBothPages);
        var c = WheelCallout.ForDrag(m, false, HintEmphasis.Prominent)!;
        Assert.Null(c.Text);
        Assert.Equal("Tools", c.KeyHints[0].Label);
    }

    // ------------------------------------------------------------------ click mode (W-11)

    [Fact]
    public void ClickModeShowsTheSelectionAboveAndTheKeysBelow()
    {
        var m = Model(WheelMode.Click);
        var callouts = WheelCallout.ForClick(m, "3 images");
        Assert.Equal("3 images", callouts.Main!.Text);
        Assert.Empty(callouts.Main.KeyHints);
        Assert.Equal(["↑↓←→", "Enter", "Tab", "Esc"], callouts.Secondary!.KeyHints.Select(h => h.Key));
        Assert.Equal(["Select", "Convert", "Tools", "Close"], callouts.Secondary.KeyHints.Select(h => h.Label));
    }

    [Fact]
    public void ClickHintsFollowThePage()
    {
        var m = Model(WheelMode.Click);
        m.TogglePage();
        var keys = WheelCallout.ClickHints(m);
        Assert.Contains(new KeyHint("Enter", "Run", 4), keys);
        Assert.Contains(keys, h => h is { Key: "Tab", Label: "Formats" });
    }

    [Fact]
    public void ClickHintsOmitTabWhenThereIsOnePage()
    {
        var m = Model(WheelMode.Click, [Action("Zip", WheelPage.Tool)]);
        Assert.DoesNotContain(WheelCallout.ClickHints(m), h => h.Key == "Tab");
    }

    [Fact]
    public void ClickModeWithoutASummaryHasNoTopPill()
    {
        Assert.Null(WheelCallout.ForClick(Model(WheelMode.Click), "").Main);
    }

    // ------------------------------------------------------------------ fitting the hints in a narrow pill

    [Fact]
    public void FitHintsKeepsEverythingThatFits()
    {
        var hints = WheelCallout.ClickHints(Model(WheelMode.Click));
        var kept = WheelCallout.FitHints(hints, 1000, _ => 60, 10);
        Assert.Equal(hints, kept);
    }

    [Fact]
    public void FitHintsDropsTheLeastImportantFirstAndKeepsTheOrder()
    {
        var hints = WheelCallout.ClickHints(Model(WheelMode.Click));   // Select(1) Enter(4) Tab(3) Esc(2)
        var kept = WheelCallout.FitHints(hints, 3 * 60 + 2 * 10, _ => 60, 10);
        Assert.Equal(["Enter", "Tab", "Esc"], kept.Select(h => h.Key));

        kept = WheelCallout.FitHints(hints, 2 * 60 + 10, _ => 60, 10);
        Assert.Equal(["Enter", "Tab"], kept.Select(h => h.Key));

        kept = WheelCallout.FitHints(hints, 70, _ => 60, 10);
        Assert.Equal(["Enter"], kept.Select(h => h.Key));

        Assert.Empty(WheelCallout.FitHints(hints, 10, _ => 60, 10));
    }

    [Fact]
    public void FitHintsAmongEqualPrioritiesDropsTheLastFirst()
    {
        KeyHint[] hints = [new("A", "a"), new("B", "b"), new("C", "c")];
        var kept = WheelCallout.FitHints(hints, 2 * 50 + 10, _ => 50, 10);
        Assert.Equal(["A", "B"], kept.Select(h => h.Key));
    }
}
