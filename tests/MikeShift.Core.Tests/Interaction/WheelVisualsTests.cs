using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>Contrast (W-08, W-04), text scaling (W-05), the hint counter (W-04), and the wording of everything the wheel shows (W-10, W-15).</summary>
public class WheelVisualsTests
{
    // ------------------------------------------------------------------ contrast maths

    [Fact]
    public void BlackOnWhiteIsTwentyOne() => Assert.Equal(21, Contrast.Ratio(Rgb.Black, Rgb.White), 6);

    [Fact]
    public void RatioIsSymmetricAndOneForEqualColours()
    {
        var a = Rgb.Hex("#0078D4");
        var b = Rgb.Hex("#FFFFFF");
        Assert.Equal(Contrast.Ratio(a, b), Contrast.Ratio(b, a), 9);
        Assert.Equal(1, Contrast.Ratio(a, a), 9);
    }

    [Fact]
    public void KnownWcagValues()
    {
        // 767676 on white is the classic "just passes AA" grey: 4.54:1
        Assert.Equal(4.54, Contrast.Ratio(Rgb.Hex("#767676"), Rgb.White), 2);
        // The Windows default accent against white text (the label of the default highlighted tile in the light theme).
        Assert.Equal(4.53, Contrast.Ratio(Rgb.Hex("#0078D4"), Rgb.White), 2);
    }

    [Theory]
    [InlineData("#0078D4", "#FFFFFF")]   // default blue: white
    [InlineData("#0067C0", "#FFFFFF")]   // AccentDark1 of the default blue
    [InlineData("#60CDFF", "#000000")]   // AccentLight2 of the default blue: black
    [InlineData("#FFB900", "#000000")]   // yellow: black
    [InlineData("#FFF100", "#000000")]
    [InlineData("#00CC6A", "#000000")]   // light green
    [InlineData("#8E8CD8", "#000000")]   // lavender
    [InlineData("#C239B3", "#FFFFFF")]   // magenta
    [InlineData("#107C10", "#FFFFFF")]   // green
    [InlineData("#E81123", "#FFFFFF")]   // red
    public void TextOnFillPicksBlackOrWhiteByContrast(string fill, string expected)
    {
        var f = Rgb.Hex(fill);
        var text = Contrast.TextOnFill(f);
        Assert.Equal(Rgb.Hex(expected), text);
        // the pick always reaches AA text (white is preferred when both black and white do, as on the default blue)
        Assert.True(Contrast.Ratio(text, f) >= Contrast.AaText, $"{fill}: {Contrast.Ratio(text, f):0.00}");
    }

    [Fact]
    public void EveryFillInTheColourCubeGetsALabelThatReachesAa()
    {
        // The better of black and white always reaches at least 4.58:1 (worst case at luminance 0.179), so a pick below 4.5 would be a bug.
        for (var r = 0; r <= 255; r += 17)
            for (var g = 0; g <= 255; g += 51)
                for (var b = 0; b <= 255; b += 51)
                {
                    var fill = new Rgb((byte)r, (byte)g, (byte)b);
                    var text = Contrast.TextOnFill(fill);
                    Assert.True(Contrast.Ratio(text, fill) >= Contrast.AaText, $"{fill}: {Contrast.Ratio(text, fill):0.00}");
                    Assert.True(text == Rgb.White || text == Rgb.Black);
                }
    }

    [Fact]
    public void AGradientIsJudgedByItsWorstStop()
    {
        // white reaches 4.5 on the dark stop but not on the light stop: the label must be black (or at least the better of the two)
        var dark = Rgb.Hex("#0067C0");
        var light = Rgb.Hex("#4CA0E8");
        Assert.True(Contrast.Ratio(Rgb.White, dark) >= 4.5);
        Assert.True(Contrast.Ratio(Rgb.White, light) < 4.5);
        var picked = Contrast.TextOnFill(dark, light);
        var worstWhite = Contrast.WorstRatio(Rgb.White, [dark, light]);
        var worstBlack = Contrast.WorstRatio(Rgb.Black, [dark, light]);
        Assert.Equal(worstBlack > worstWhite ? Rgb.Black : Rgb.White, picked);
    }

    [Fact]
    public void CustomCandidatesAreHonoured()
    {
        var picked = Contrast.TextOnFill(Rgb.Hex("#1A1A1A"), Rgb.Hex("#FFFFFF"), [Rgb.Hex("#202020")]);
        Assert.Equal(Rgb.Hex("#FFFFFF"), picked);
        picked = Contrast.TextOnFill(Rgb.Hex("#FFFFFF"), Rgb.Hex("#1A1A1A"), [Rgb.Hex("#F0F0F0")]);
        Assert.Equal(Rgb.Hex("#1A1A1A"), picked);
    }

    [Fact]
    public void OverBlendsInSrgbSpace()
    {
        Assert.Equal(new Rgb(128, 128, 128), Rgb.White.Over(Rgb.Black, 0.5));
        Assert.Equal(Rgb.White, Rgb.White.Over(Rgb.Black, 1));
        Assert.Equal(Rgb.Black, Rgb.White.Over(Rgb.Black, 0));
        Assert.Equal(new Rgb(128, 128, 128), Rgb.Black.Mix(Rgb.White, 0.5));
    }

    // ------------------------------------------------------------------ the palette really is legible over any wallpaper

    public static IEnumerable<object[]> Themes() => [[true], [false]];

    [Theory]
    [MemberData(nameof(Themes))]
    public void PillTextAndSecondaryTextReachAaOnTheOpaquePill(bool dark)
    {
        var p = WheelPalette.For(dark);
        Assert.True(Contrast.Ratio(p.PillText, p.PillFill) >= 7, "primary text AAA");
        Assert.True(Contrast.Ratio(p.PillSecondary, p.PillFill) >= Contrast.AaText, "secondary (quiet hints, keycap outlines)");
        Assert.True(Contrast.Ratio(p.CautionText, p.CautionFill) >= 7);
        Assert.True(Contrast.Ratio(p.CautionGlyph, p.CautionFill) >= Contrast.AaLarge, "the info glyph is a graphic");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void HubTextAndHintReachAaOverTheWorstWallpapers(bool dark)
    {
        var p = WheelPalette.For(dark);
        foreach (var backdrop in WheelPalette.WorstBackdrops)
        {
            var hub = p.HubOver(backdrop);
            Assert.True(Contrast.Ratio(p.HubText, hub) >= 7, $"hub title over {backdrop}: {Contrast.Ratio(p.HubText, hub):0.0}");
            Assert.True(Contrast.Ratio(p.HubSecondary, hub) >= Contrast.AaText, $"hub hint over {backdrop}: {Contrast.Ratio(p.HubSecondary, hub):0.0}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TileLabelsReachAaOverTheWorstWallpapers(bool dark)
    {
        var p = WheelPalette.For(dark);
        foreach (var backdrop in WheelPalette.WorstBackdrops)
        {
            var tile = p.TileOver(backdrop);
            Assert.True(Contrast.Ratio(p.TileText, tile) >= Contrast.AaText, $"tile label over {backdrop}: {Contrast.Ratio(p.TileText, tile):0.0}");
        }
    }

    [Fact]
    public void LabelOnTheAccentFillIsLegibleForTheWindowsAccentPresets()
    {
        // The 48 Windows accent presets are not all enumerable here; these cover light, dark, warm and cool ends of the range.
        string[] accents = ["#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343", "#E74856", "#E81123",
            "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089", "#0078D4", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8",
            "#744DA9", "#B146C2", "#881798", "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
            "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10", "#767676", "#4C4A48", "#69797E",
            "#4A5459", "#647C64", "#525E54", "#847545", "#7E735F"];
        foreach (var hex in accents)
        {
            var accent = Rgb.Hex(hex);
            var text = Contrast.TextOnFill(accent);
            // white or black, whichever reaches AA text (4.5:1)
            Assert.True(Contrast.Ratio(text, accent) >= Contrast.AaText, hex);
        }
    }

    // ------------------------------------------------------------------ text scale

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.4, 1.4)]
    [InlineData(2.25, 1.4)]
    [InlineData(0.5, 1.0)]
    [InlineData(-3, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void TextScaleIsClampedToOneThroughOnePointFour(double factor, double expected) =>
        Assert.Equal(expected, WheelTypography.ClampScale(factor), 9);

    [Fact]
    public void TypeSizesAreNeverBelowTheFluentCaption()
    {
        Assert.True(WheelTypography.Caption >= 12);
        Assert.True(WheelTypography.Body >= 14);
        Assert.Equal(12, WheelTypography.Caption);          // tool captions, pill, hub hint
        Assert.Equal(14, WheelTypography.Body);             // format labels, hub title
        Assert.Equal(18, WheelTypography.TileGlyph);
        Assert.Equal(24, WheelTypography.SingleGlyph);
        Assert.True(WheelTypography.MinFitSize >= 12, "text that must fit a tile is never shrunk below 12 px");
        foreach (var scale in new[] { 1.0, 1.2, 1.4, 2.0 })
            Assert.True(WheelTypography.Scaled(WheelTypography.Caption, scale) >= 12);
    }

    [Fact]
    public void ScaledMultipliesByTheClampedFactor()
    {
        Assert.Equal(12, WheelTypography.Scaled(12, 1.0), 9);
        Assert.Equal(16.8, WheelTypography.Scaled(12, 1.4), 9);
        Assert.Equal(16.8, WheelTypography.Scaled(12, 2.25), 9);
    }

    [Fact]
    public void FitSizeShrinksProportionallyButNeverBelowCaption()
    {
        Assert.Equal(14, WheelTypography.FitSize(14, 60, 80), 9);          // fits
        Assert.Equal(14, WheelTypography.FitSize(14, 80, 80), 9);          // exactly fits
        Assert.Equal(12, WheelTypography.FitSize(14, 100, 80), 9);         // ...so it stops at 12
        Assert.Equal(18.06, WheelTypography.FitSize(21, 100, 86), 2);      // 21 * 0.86: proportional
        Assert.Equal(21, WheelTypography.FitSize(21, 0, 80), 9);           // nothing measured: unchanged
        Assert.Equal(21, WheelTypography.FitSize(21, 50, 0), 9);
    }

    [Fact]
    public void FitSizeNeverEnlargesOrDropsBelowTheFloorWhenTheStartIsAlreadySmaller()
    {
        Assert.Equal(12, WheelTypography.FitSize(12, 200, 50), 9);
        Assert.Equal(11, WheelTypography.FitSize(11, 200, 50), 9);   // a size below the floor is not "raised" by fitting
    }

    [Fact]
    public void TheHubHintMakesWayForALargeTitle()
    {
        Assert.True(WheelTypography.HubHintFits(1.0));
        Assert.True(WheelTypography.HubHintFits(1.2));
        Assert.False(WheelTypography.HubHintFits(1.25));
        Assert.False(WheelTypography.HubHintFits(1.4));
        Assert.False(WheelTypography.HubHintFits(2.0));
    }

    [Theory]
    [InlineData(12, 24)]
    [InlineData(14.4, 27)]
    [InlineData(16.8, 29)]
    public void PillHeightFollowsTheText(double font, double height) => Assert.Equal(height, WheelTypography.PillHeight(font));

    [Fact]
    public void ThePillAtMaximumScaleStillFitsTheMargin()
    {
        var h = WheelTypography.PillHeight(WheelTypography.Caption * WheelTypography.MaxTextScale);
        Assert.True(h + 5 <= WheelGeometry.WindowMargin, $"{h} + 5 px gap must fit in the {WheelGeometry.WindowMargin} px margin");
    }

    // ------------------------------------------------------------------ the first-N-wheels hint counter

    [Theory]
    [InlineData(0, HintEmphasis.Prominent)]
    [InlineData(1, HintEmphasis.Prominent)]
    [InlineData(19, HintEmphasis.Prominent)]
    [InlineData(20, HintEmphasis.Quiet)]
    [InlineData(500, HintEmphasis.Quiet)]
    public void HintsAreProminentForTheFirstTwentyWheels(int before, HintEmphasis expected) =>
        Assert.Equal(expected, WheelHints.EmphasisFor(before));

    [Theory]
    [InlineData(0, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]
    [InlineData(21, false)]
    public void OnlyTheFirstWheelsAreCounted(int before, bool counted) => Assert.Equal(counted, WheelHints.ShouldCount(before));

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("abc", 0)]
    [InlineData("-5", 0)]
    [InlineData("3.5", 0)]
    [InlineData("7", 7)]
    [InlineData(" 12\r\n", 12)]
    [InlineData("20", 20)]
    [InlineData("999999", 20)]
    [InlineData("99999999999999999999", 0)]
    public void StoredCountsAreParsedDefensively(string? stored, int expected) => Assert.Equal(expected, WheelHints.Parse(stored));

    [Fact]
    public void SerializeRoundTripsAndClamps()
    {
        for (var n = 0; n <= 20; n++) Assert.Equal(n, WheelHints.Parse(WheelHints.Serialize(n)));
        Assert.Equal("20", WheelHints.Serialize(55));
        Assert.Equal("0", WheelHints.Serialize(-4));
    }

    // ------------------------------------------------------------------ wording

    [Fact]
    public void HubWordsAreSentenceCase()
    {
        Assert.Equal("Convert to", WheelText.FormatTitle);
        Assert.Equal("Tools", WheelText.ToolTitle);
        Assert.Equal("Convert to", WheelText.PageTitle(WheelPage.Format));
        Assert.Equal("Tools", WheelText.PageTitle(WheelPage.Tool));
        Assert.DoesNotContain("CONVERT", WheelText.FormatTitle);
    }

    [Fact]
    public void HubHoverPillAndAutomationNameAreTheSameWords()
    {
        var png = new ActionDef("PNG", WheelPage.Format, (_, _, _) => throw new NotSupportedException());
        var compress = new ActionDef("Compress", WheelPage.Tool, (_, _, _) => throw new NotSupportedException());
        var m = new WheelModel(WheelMode.Drag);
        m.SetActions([png, compress]);
        m.Highlight(0);

        Assert.Equal("Convert to PNG", m.CentreTitle);
        Assert.Equal(m.CentreTitle, png.DropDescription);
        Assert.Equal(png.DropDescription, WheelText.AutomationName(png));
        Assert.Equal(DropDescriptionText.For(png).Rendered, WheelText.AutomationName(png));
        Assert.Equal(m.CentreTitle, WheelCallout.ForDrag(m, false, HintEmphasis.Quiet)!.Text);

        m.TogglePage();
        m.Highlight(0);
        Assert.Equal("Compress", m.CentreTitle);
        Assert.Equal("Compress", WheelText.AutomationName(compress));
    }

    [Fact]
    public void EveryRealActionHasAVerbFirstNameAndADescription()
    {
        foreach (var category in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
            foreach (var action in ActionCatalog.ListFor(category))
            {
                var name = WheelText.AutomationName(action);
                Assert.False(string.IsNullOrWhiteSpace(name));
                if (action.Page == WheelPage.Format) Assert.StartsWith("Convert to ", name);
                else Assert.True(char.IsUpper(name[0]), name);
                Assert.False(string.IsNullOrWhiteSpace(WheelText.Describe(action)));
                Assert.NotEqual(name, WheelText.Describe(action));
            }
        Assert.False(string.IsNullOrWhiteSpace(WheelText.Describe(ZipAction.Def)));
        Assert.Equal("Zip", WheelText.AutomationName(ZipAction.Def));
    }

    [Theory]
    [InlineData(WheelPage.Format, 7, "Formats page, 7 items")]
    [InlineData(WheelPage.Tool, 5, "Tools page, 5 items")]
    [InlineData(WheelPage.Tool, 1, "Tools page, 1 item")]
    public void PageChangeAnnouncement(WheelPage page, int count, string expected) =>
        Assert.Equal(expected, WheelText.PageAnnouncement(page, count));

    [Fact]
    public void ListNameAndHelpText()
    {
        Assert.Equal("MikeShift wheel", WheelText.ListName(false, WheelPage.Format));
        Assert.Equal("MikeShift wheel: convert to", WheelText.ListName(true, WheelPage.Format));
        Assert.Equal("MikeShift wheel: tools", WheelText.ListName(true, WheelPage.Tool));
        Assert.Contains("Enter", WheelText.Help(true, true, WheelPage.Format));
        Assert.Contains("Tab for tools", WheelText.Help(true, true, WheelPage.Format));
        Assert.Contains("Tab for formats", WheelText.Help(true, true, WheelPage.Tool));
        Assert.Contains("Escape", WheelText.Help(true, true, WheelPage.Format));
        Assert.DoesNotContain("Tab", WheelText.Help(true, false, WheelPage.Tool));
        Assert.Equal("Press Enter to choose", WheelText.TileHelp(true));
        Assert.Equal("Drop the files here", WheelText.TileHelp(false));
        Assert.Contains("Tab switches pages", WheelText.OpenAnnouncement(7, true));
        Assert.DoesNotContain("Tab", WheelText.OpenAnnouncement(1, false));
        Assert.Contains("1 option.", WheelText.OpenAnnouncement(1, false));
    }

    // ------------------------------------------------------------------ the selection summary (click-mode pill)

    [Fact]
    public void OneFileIsNamedByItsFileName()
    {
        Assert.Equal("photo.jpg", SelectionSummary.For([@"C:\Users\me\Pictures\photo.jpg"], null));
        Assert.Equal("My Folder", SelectionSummary.For([@"C:\Users\me\My Folder\"], null));
        Assert.Equal("C:", SelectionSummary.For([@"C:\"], null));
        Assert.Equal("", SelectionSummary.For([], null));
    }

    [Theory]
    [InlineData(Category.Image, "3 images")]
    [InlineData(Category.Video, "3 videos")]
    [InlineData(Category.Pdf, "3 PDFs")]
    [InlineData(Category.Document, "3 documents")]
    [InlineData(Category.Audio, "3 audio files")]
    [InlineData(Category.Other, "3 items")]
    public void SeveralFilesAreCountedByTheirKind(Category kind, string expected) =>
        Assert.Equal(expected, SelectionSummary.For(["a", "b", "c"], kind));

    [Fact]
    public void MixedSelectionsAreCountedAsItems() => Assert.Equal("3 items", SelectionSummary.For(["a", "b", "c"], null));
}
