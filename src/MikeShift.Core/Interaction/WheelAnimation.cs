namespace MikeShift.Core.Interaction;

/// <summary>Where the wheel's life-cycle is, from the animation's point of view.</summary>
public enum WheelPhase
{
    /// <summary>Not shown.</summary>
    Hidden,
    /// <summary>Shown (opening animation first, then steady).</summary>
    Visible,
    /// <summary>A tile was picked: the picked tile pulses and the disc grows and fades (<see cref="WheelTimings"/>).</summary>
    Collapsing,
    /// <summary>Released on nothing / Esc / focus loss: fading out.</summary>
    Dismissing,
}

/// <summary>
/// The wheel's animation clock (PLAN §3.2), kept free of UI so it can be unit-tested: feed it events (<see cref="Show"/>,
/// <see cref="TilesArrived"/>, <see cref="PageChanged"/>, <see cref="HighlightChanged"/>, <see cref="Pick"/>, <see cref="Dismiss"/>)
/// with a monotonic time in seconds and ask it for values at any later time. All values follow <see cref="WheelTimings"/> and
/// <see cref="Easing"/>; with animations disabled every transition is instantaneous.
/// <para>
/// <see cref="Generation"/> increments on every <see cref="Show"/>: a delayed callback (a dismiss timer, the end of a collapse)
/// captures it and acts only if <see cref="IsCurrent"/> still holds, so a stale dismiss can never hide a newer wheel.
/// </para>
/// </summary>
public sealed class WheelAnimation
{
    /// <summary>How long the highlight push of a tile takes to settle (not specified by the PLAN; short and snappy).</summary>
    public const double HighlightSeconds = 0.083;

    private readonly Func<bool> _animationsEnabled;
    private bool _enabled = true;
    private double _openStart;
    private double? _tilesStart;
    private double? _pageStart;
    private double _phaseStart;
    private double _dismissFromOpacity = 1;
    private int _picked = -1;
    private readonly Dictionary<int, (double From, double To, double Start)> _highlights = [];

    /// <param name="animationsEnabled">Queried at <see cref="Show"/> (the OS "animation effects" switch). Null = always on.</param>
    public WheelAnimation(Func<bool>? animationsEnabled = null) => _animationsEnabled = animationsEnabled ?? (() => true);

    /// <summary>Incremented by every <see cref="Show"/>.</summary>
    public int Generation { get; private set; }

    public WheelPhase Phase { get; private set; } = WheelPhase.Hidden;

    /// <summary>Whether animations run for the current show (false = everything is instantaneous).</summary>
    public bool Enabled => _enabled;

    /// <summary>The index of the tile picked by <see cref="Pick"/>, or -1.</summary>
    public int PickedIndex => _picked;

    /// <summary>True while <paramref name="generation"/> is the latest show.</summary>
    public bool IsCurrent(int generation) => generation == Generation;

    /// <summary>The wheel is shown (disc opens now). Returns the new <see cref="Generation"/>.</summary>
    public int Show(double now)
    {
        _enabled = _animationsEnabled();
        Generation++;
        Phase = WheelPhase.Visible;
        _openStart = now;
        _phaseStart = now;
        _tilesStart = null;
        _pageStart = null;
        _picked = -1;
        _dismissFromOpacity = 1;
        _highlights.Clear();
        return Generation;
    }

    /// <summary>Tiles were (re)populated: they pop out staggered starting now. Clears highlights.</summary>
    public void TilesArrived(double now)
    {
        _tilesStart = now;
        _pageStart = null;
        _highlights.Clear();
    }

    /// <summary>The tile lists were replaced without a pop (e.g. the "Save the file first" hint cleared them).</summary>
    public void TilesCleared()
    {
        _tilesStart = null;
        _pageStart = null;
        _highlights.Clear();
    }

    /// <summary>The page changed: the old tiles fade out, then the new ones slide in. Clears highlights.</summary>
    public void PageChanged(double now)
    {
        _highlights.Clear();
        _pageStart = _enabled ? now : null;
    }

    /// <summary>The highlighted tile moved to <paramref name="index"/> (null = none).</summary>
    public void HighlightChanged(double now, int? index)
    {
        foreach (var key in _highlights.Keys.ToArray())
        {
            if (key == index) continue;
            var current = Highlight(key, now);
            if (current <= 0 && _highlights[key].To <= 0) { _highlights.Remove(key); continue; }
            _highlights[key] = (current, 0, now);
        }
        if (index is { } i)
        {
            var current = _highlights.ContainsKey(i) ? Highlight(i, now) : 0;
            _highlights[i] = (current, 1, now);
        }
    }

    /// <summary>The user picked tile <paramref name="index"/>: it pulses outward while the disc grows and fades. Generation is unchanged.</summary>
    public void Pick(double now, int index)
    {
        if (Phase is not (WheelPhase.Visible)) return;
        _picked = index;
        Phase = WheelPhase.Collapsing;
        _phaseStart = now;
    }

    /// <summary>Fade out (release on nothing, Esc, focus loss).</summary>
    public void Dismiss(double now)
    {
        if (Phase is not WheelPhase.Visible) return;
        _dismissFromOpacity = Opacity(now);
        Phase = WheelPhase.Dismissing;
        _phaseStart = now;
    }

    /// <summary>Hidden immediately (silent hide, end of a collapse/dismiss).</summary>
    public void Hide()
    {
        Phase = WheelPhase.Hidden;
        _picked = -1;
        _tilesStart = null;
        _pageStart = null;
        _highlights.Clear();
    }

    // ------------------------------------------------------------------ queries

    /// <summary>True when a collapse or dismiss has played to the end and the window should be hidden.</summary>
    public bool IsFinished(double now) => Phase switch
    {
        WheelPhase.Collapsing => now - _phaseStart >= WheelTimings.Scaled(WheelTimings.CollapseSeconds, _enabled),
        WheelPhase.Dismissing => now - _phaseStart >= WheelTimings.Scaled(WheelTimings.DismissSeconds, _enabled),
        _ => false,
    };

    /// <summary>True while any value is still changing at <paramref name="now"/> (the render loop runs only while this holds).</summary>
    public bool IsAnimating(double now)
    {
        switch (Phase)
        {
            case WheelPhase.Hidden:
                return false;
            case WheelPhase.Collapsing:
            case WheelPhase.Dismissing:
                return true; // until IsFinished lets the caller hide it
        }

        if (!_enabled) return false;
        if (now - _openStart < WheelTimings.OpenSeconds) return true;
        if (_tilesStart is { } t && now - t < WheelTimings.OpenTotalSeconds(TileCountHint)) return true;
        if (_pageStart is { } p && now - p < WheelTimings.PageFadeSeconds + WheelTimings.PageSlideSeconds) return true;
        foreach (var h in _highlights.Values)
            if (now - h.Start < HighlightSeconds) return true;
        return false;
    }

    /// <summary>How many tiles are being animated in (for the total duration of the stagger); set by the renderer.</summary>
    public int TileCountHint { get; set; } = 12;

    /// <summary>Scale of the whole wheel: 0.94→1 while opening (never above 1), 1 when steady, 1→0.96 while collapsing or dismissing.</summary>
    public double Scale(double now) => Phase switch
    {
        WheelPhase.Hidden => WheelTimings.OpenScaleFrom,
        WheelPhase.Visible => WheelTimings.OpenScale(now - _openStart, _enabled),
        WheelPhase.Collapsing => WheelTimings.CollapseScale(now - _phaseStart, _enabled),
        WheelPhase.Dismissing => WheelTimings.DismissScale(now - _phaseStart, _enabled),
        _ => 1,
    };

    /// <summary>Opacity of the whole wheel (the pixel floor of alpha 1 for OLE hit-testing is the renderer's business).</summary>
    public double Opacity(double now) => Phase switch
    {
        WheelPhase.Hidden => 0,
        WheelPhase.Visible => Math.Clamp(WheelTimings.OpenOpacity(now - _openStart, _enabled), 0, 1),
        WheelPhase.Collapsing => WheelTimings.CollapseOpacity(now - _phaseStart, _enabled),
        WheelPhase.Dismissing => _dismissFromOpacity * WheelTimings.DismissOpacity(now - _phaseStart, _enabled),
        _ => 1,
    };

    /// <summary>Tile <paramref name="index"/>'s pop progress: 0 before its stagger starts (or with no tiles), up to 1 (may overshoot slightly).</summary>
    public double TilePop(int index, double now) =>
        _tilesStart is { } start ? WheelTimings.TilePop(index, now - start, _enabled) : 0;

    /// <summary>How far tile <paramref name="index"/> is highlighted (0..1): drives the 9 px push and the accent colour.</summary>
    public double Highlight(int index, double now)
    {
        if (!_highlights.TryGetValue(index, out var h)) return 0;
        if (!_enabled) return h.To;
        return h.From + (h.To - h.From) * Easing.Decelerate.Evaluate(Math.Clamp((now - h.Start) / HighlightSeconds, 0, 1));
    }

    /// <summary>Extra outward push of the picked tile (0 until picked).</summary>
    public double PickPush(double now) =>
        Phase == WheelPhase.Collapsing ? WheelTimings.PickPulseOffset(now - _phaseStart, _enabled) : 0;

    /// <summary>True while an old-page → new-page transition is playing.</summary>
    public bool PageSwitching(double now) =>
        _pageStart is { } p && now - p < WheelTimings.PageFadeSeconds + WheelTimings.PageSlideSeconds;

    /// <summary>Opacity of the outgoing page's tiles (1 → 0 over the first 0.12 s; 0 afterwards).</summary>
    public double OutgoingOpacity(double now) =>
        _pageStart is { } p ? WheelTimings.PageOutOpacity(now - p) : 0;

    /// <summary>Opacity of the incoming page's tiles (0 until the outgoing ones are gone, then 0 → 1; 1 when no transition runs).</summary>
    public double IncomingOpacity(double now) =>
        _pageStart is { } p ? WheelTimings.PageInOpacity(now - p - WheelTimings.PageFadeSeconds) : 1;

    /// <summary>Radial offset (logical px, negative = toward the centre) of the incoming tiles: -16 → 0.</summary>
    public double IncomingOffset(double now) =>
        _pageStart is { } p ? WheelTimings.PageInOffset(now - p - WheelTimings.PageFadeSeconds) : 0;
}
