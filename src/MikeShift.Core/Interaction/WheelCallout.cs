using MikeShift.Core.Actions;

namespace MikeShift.Core.Interaction;

/// <summary>Which vertical margin of the wheel window a callout pill is drawn in.</summary>
public enum CalloutSide
{
    /// <summary>Above the disc.</summary>
    Top,
    /// <summary>Below the disc.</summary>
    Bottom,
}

/// <summary>How a callout reads: normal, or the "cannot do that" caution look (tinted surface, info glyph).</summary>
public enum CalloutTone
{
    Normal,
    Caution,
}

/// <summary>How loud the key hints are: full-strength keycaps for the first wheels, then quiet secondary text.</summary>
public enum HintEmphasis
{
    Prominent,
    Quiet,
}

/// <summary>A keyboard hint drawn as a keycap and a label ("Ctrl" + "Tools"). Higher <see cref="Priority"/> survives longer when the pill is too narrow.</summary>
public readonly record struct KeyHint(string Key, string Label, int Priority = 0);

/// <summary>What one callout pill says: optional text, optional leading glyph (Segoe Fluent Icons), and key hints.</summary>
public sealed record CalloutContent(
    string? Text,
    CalloutTone Tone = CalloutTone.Normal,
    string? Glyph = null,
    IReadOnlyList<KeyHint>? Hints = null,
    HintEmphasis Emphasis = HintEmphasis.Prominent)
{
    /// <summary>The hints (never null).</summary>
    public IReadOnlyList<KeyHint> KeyHints { get; init; } = Hints ?? [];

    /// <summary>True if there is nothing to draw.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text) && KeyHints.Count == 0;
}

/// <summary>The pills of one wheel frame: <see cref="Main"/> (drag mode: on the side away from the pointer; click mode: above the disc) and the click-mode <see cref="Secondary"/> below it.</summary>
public sealed record WheelCallouts(CalloutContent? Main, CalloutContent? Secondary = null)
{
    public static WheelCallouts None { get; } = new((CalloutContent?)null);
}

/// <summary>Chooses the callout side and the content (UX review W-01, W-02, W-04, W-09, W-11).</summary>
public static class WheelCallout
{
    /// <summary>The "Info" glyph (Segoe Fluent Icons E946) of the caution pill and the hub of a virtual-file drag.</summary>
    public const string InfoGlyph = "";

    /// <summary>The pointer must be this far (logical px) past the centre line before the pill changes side, so it does not jitter while the pointer crosses the middle.</summary>
    public const double SideDeadZone = 14;

    /// <summary>
    /// The side for the pill: opposite the pointer, so Explorer's drag thumbnail (attached to the pointer) never covers it. Pointer in the
    /// top half: the pill is below the disc; bottom half: above. <paramref name="pointerDy"/> is the pointer's offset below the wheel centre
    /// (logical px, negative = above). Within <see cref="SideDeadZone"/> of the centre line the <paramref name="previous"/> side is kept
    /// (and with no previous side the pointer counts as being in the bottom half: pill above).
    /// </summary>
    public static CalloutSide SideFor(double pointerDy, CalloutSide? previous = null)
    {
        if (previous is { } keep && Math.Abs(pointerDy) <= SideDeadZone) return keep;
        return pointerDy < 0 ? CalloutSide.Bottom : CalloutSide.Top;
    }

    /// <summary>The vertical margin band of the window (logical px from the window's top edge) the pill is centred in for <paramref name="side"/>.</summary>
    public static double BandCentreY(CalloutSide side) =>
        side == CalloutSide.Top ? WheelGeometry.WindowMargin / 2 : WheelGeometry.WindowSize - WheelGeometry.WindowMargin / 2;

    /// <summary>
    /// The callout of a drag-mode wheel. A virtual-file drag says to save the file first (caution look); a single-tile wheel explains why
    /// there is only one tile; otherwise the pill names the highlighted tile ("Convert to PNG") and shows the key hints (always for the first
    /// wheels, only while nothing is highlighted afterwards).
    /// </summary>
    public static CalloutContent? ForDrag(WheelModel model, bool virtualFiles, HintEmphasis emphasis, string? note = null)
    {
        if (virtualFiles)
            return new CalloutContent(WheelText.SaveFirst, CalloutTone.Caution, InfoGlyph);

        var tiles = model.CurrentActions;
        if (tiles.Count == 0) return null;

        // A note (see WheelText.WordNote) explains why there are no format tiles; it replaces the "Only Zip" line.
        if (note is not null && model.Formats.Count == 0)
            return new CalloutContent(note, CalloutTone.Caution, InfoGlyph);

        if (tiles.Count == 1 && !model.HasBothPages)
            return new CalloutContent(WheelText.OnlyOption(tiles[0].Title));

        var highlighted = model.HighlightedAction;
        var hints = model.KeyHints;
        // Quiet phase: the hints step aside while a tile is highlighted, so the tile's name is all there is to read.
        if (highlighted is not null && emphasis == HintEmphasis.Quiet) hints = [];
        var content = new CalloutContent(highlighted?.DropDescription, Hints: hints, Emphasis: emphasis);
        return content.IsEmpty ? null : content;
    }

    /// <summary>
    /// The pills of a click-mode wheel: what is selected above the disc, the keys below it
    /// ("↑↓←→ Select · Enter Convert · Tab Tools · Esc Close"); a <paramref name="note"/> that explains the missing format tiles takes the place of the selection.
    /// </summary>
    public static WheelCallouts ForClick(WheelModel model, string selectionSummary, string? note = null)
    {
        var main = note is not null && model.Formats.Count == 0 ? new CalloutContent(note, CalloutTone.Caution, InfoGlyph)
            : string.IsNullOrEmpty(selectionSummary) ? null : new CalloutContent(selectionSummary);
        return new WheelCallouts(main, new CalloutContent(null, Hints: ClickHints(model), Emphasis: HintEmphasis.Prominent));
    }

    /// <summary>The click-mode key hints for the current page, most important first by <see cref="KeyHint.Priority"/>.</summary>
    public static IReadOnlyList<KeyHint> ClickHints(WheelModel model)
    {
        var hints = new List<KeyHint>
        {
            new("↑↓←→", "Select", 1),
            new("Enter", model.Page == WheelPage.Format ? "Convert" : "Run", 4),
        };
        if (model.HasBothPages)
            hints.Add(new KeyHint("Tab", model.Page == WheelPage.Format ? "Tools" : "Formats", 3));
        hints.Add(new KeyHint("Esc", "Close", 2));
        return hints;
    }

    /// <summary>
    /// Drops hints, lowest <see cref="KeyHint.Priority"/> first (later ones first among equals), until the rest fit in
    /// <paramref name="available"/> logical px. <paramref name="measure"/> returns one hint's width; <paramref name="gap"/> separates hints.
    /// The surviving hints keep their original order.
    /// </summary>
    public static IReadOnlyList<KeyHint> FitHints(IReadOnlyList<KeyHint> hints, double available, Func<KeyHint, double> measure, double gap)
    {
        var keep = hints.Select((h, i) => (Hint: h, Index: i)).ToList();
        double Width() => keep.Sum(k => measure(k.Hint)) + Math.Max(0, keep.Count - 1) * gap;
        while (keep.Count > 0 && Width() > available)
        {
            var drop = keep.OrderBy(k => k.Hint.Priority).ThenByDescending(k => k.Index).First();
            keep.Remove(drop);
        }
        return keep.Select(k => k.Hint).ToList();
    }
}
