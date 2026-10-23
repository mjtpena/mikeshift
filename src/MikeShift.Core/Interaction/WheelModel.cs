using MikeShift.Core.Actions;

namespace MikeShift.Core.Interaction;

/// <summary>How the wheel was opened.</summary>
public enum WheelMode
{
    /// <summary>Shift-drag: the pointer drives the highlight through OLE <c>DragOver</c>; the entry point is a neutral zone.</summary>
    Drag,
    /// <summary>"Convert Files…": hover highlights, mouse-down commits, keyboard navigation works. No neutral zone.</summary>
    Click,
}

/// <summary>Keys the wheel understands in click mode (map from virtual keys with <see cref="WheelKeys.TryFromVirtualKey"/>).</summary>
public enum WheelKey
{
    Left,
    Right,
    Up,
    Down,
    Tab,
    Enter,
    Escape,
}

/// <summary>What changed in a <see cref="WheelModel"/> (flags; several can be set at once).</summary>
[Flags]
public enum WheelChange
{
    None = 0,
    /// <summary>The highlighted tile changed (including to/from none).</summary>
    Highlight = 1,
    /// <summary>The current page changed.</summary>
    Page = 2,
    /// <summary>The tile lists were replaced.</summary>
    Actions = 4,
    /// <summary>The centre pill text changed (follows Highlight, Page and Actions).</summary>
    CentreText = 8,
    /// <summary>The hub hint changed (follows Page, Actions and the tools key).</summary>
    HubHint = 16,
}

/// <summary>Mapping of Win32 virtual keys to <see cref="WheelKey"/>.</summary>
public static class WheelKeys
{
    public static bool TryFromVirtualKey(int vk, out WheelKey key)
    {
        switch (vk)
        {
            case VirtualKeys.Left: key = WheelKey.Left; return true;
            case VirtualKeys.Right: key = WheelKey.Right; return true;
            case VirtualKeys.Up: key = WheelKey.Up; return true;
            case VirtualKeys.Down: key = WheelKey.Down; return true;
            case VirtualKeys.Tab: key = WheelKey.Tab; return true;
            case VirtualKeys.Return: key = WheelKey.Enter; return true;
            case VirtualKeys.Escape: key = WheelKey.Escape; return true;
            default: key = default; return false;
        }
    }
}

/// <summary>
/// State of the wheel as the renderer and the UI Automation provider see it: the tiles of each page, which page is showing, the
/// highlighted tile, the neutral zone, keyboard navigation (click mode) and the hub texts. UI-free; raises <see cref="Changed"/>
/// after every mutation that changes something observable. Single-threaded (the wheel's UI thread).
/// </summary>
public sealed class WheelModel
{
    /// <summary>Centre pill text when nothing is highlighted on the Format page.</summary>
    public const string FormatTitle = WheelText.FormatTitle;

    /// <summary>Centre pill text when nothing is highlighted on the Tool page.</summary>
    public const string ToolTitle = WheelText.ToolTitle;

    private IReadOnlyList<ActionDef> _formats = [];
    private IReadOnlyList<ActionDef> _tools = [];
    private PointPx? _neutralCentre;
    private PointPx? _lastPointer;
    private PointPx _lastCentre;
    private double _lastDpi = 1;
    private bool _havePointerGeometry;
    private ToolsKey _toolsKey;

    public WheelModel(WheelMode mode = WheelMode.Drag, ToolsKey toolsKey = ToolsKey.Ctrl)
    {
        Mode = mode;
        _toolsKey = toolsKey;
    }

    /// <summary>Raised after a mutation; the argument says what changed.</summary>
    public event Action<WheelChange>? Changed;

    /// <summary>Raised by <see cref="PickHighlighted"/> / Enter with the chosen action.</summary>
    public event Action<ActionDef>? ActionPicked;

    /// <summary>Raised when the user cancels from the keyboard (Esc).</summary>
    public event Action? Cancelled;

    public WheelMode Mode { get; }

    /// <summary>Which page is showing.</summary>
    public WheelPage Page { get; private set; } = WheelPage.Format;

    /// <summary>The Format-page tiles in catalogue order.</summary>
    public IReadOnlyList<ActionDef> Formats => _formats;

    /// <summary>The Tool-page tiles in catalogue order (Zip last).</summary>
    public IReadOnlyList<ActionDef> Tools => _tools;

    /// <summary>The tiles of <see cref="Page"/>; this is what is drawn and what hit-test indices refer to.</summary>
    public IReadOnlyList<ActionDef> CurrentActions => Page == WheelPage.Format ? _formats : _tools;

    /// <summary>Index into <see cref="CurrentActions"/> of the highlighted tile, or null.</summary>
    public int? HighlightedIndex { get; private set; }

    /// <summary>The highlighted action, or null.</summary>
    public ActionDef? HighlightedAction => HighlightedIndex is { } i && i < CurrentActions.Count ? CurrentActions[i] : null;

    /// <summary>True when both pages have tiles, so toggling does something.</summary>
    public bool HasBothPages => _formats.Count > 0 && _tools.Count > 0;

    /// <summary>True while the entry point's neutral zone is suppressing highlights (drag mode).</summary>
    public bool InNeutralZone => _neutralCentre is not null;

    /// <summary>The key that toggles to Tools in drag mode (used for <see cref="HubHint"/>).</summary>
    public ToolsKey ToolsKey
    {
        get => _toolsKey;
        set
        {
            if (_toolsKey == value) return;
            _toolsKey = value;
            Raise(WheelChange.HubHint);
        }
    }

    /// <summary>
    /// Hub text: the hovered action as the shell says it ("Convert to PNG", "Compress"), else "Convert to" / "Tools". Sentence case,
    /// the same words as <see cref="DropDescription"/> and the UI Automation names.
    /// </summary>
    public string CentreTitle => HighlightedAction?.DropDescription ?? WheelText.PageTitle(Page);

    /// <summary>True when the hub shows a hovered action's name (drawn bold) instead of the page title.</summary>
    public bool CentreIsActionName => HighlightedAction is not null;

    /// <summary>True when the current page has exactly one tile: no ring, the hub itself is the button.</summary>
    public bool IsSingleTile => CurrentActions.Count == 1;

    /// <summary>
    /// The key that switches to the other page, as keycap + label: ("Ctrl", "Tools") on the Format page, ("Shift", "Formats") on the Tool
    /// page in drag mode; ("Tab", ...) in click mode. Empty if the other page has no tiles.
    /// </summary>
    public IReadOnlyList<KeyHint> KeyHints
    {
        get
        {
            if (Mode == WheelMode.Click)
                return !HasBothPages ? [] : [new KeyHint("Tab", Page == WheelPage.Format ? "Tools" : "Formats", 4)];
            if (Page == WheelPage.Format)
                return _tools.Count == 0 ? [] : [new KeyHint(ToolsKeyLabel(_toolsKey), "Tools", 4)];
            return _formats.Count == 0 ? [] : [new KeyHint("Shift", "Formats", 4)];
        }
    }

    /// <summary>Text under the hub title: "Ctrl  Tools" on the Format page, "Shift  Formats" on the Tool page ("Tab  Tools" in click mode); empty if the other page has no tiles.</summary>
    public string HubHint => string.Join("   ", KeyHints.Select(h => $"{h.Key}  {h.Label}"));

    /// <summary>The drop description for Explorer's drag image for the highlighted tile ("Convert to PNG", "Compress"), or null.</summary>
    public string? DropDescription => HighlightedAction?.DropDescription;

    /// <summary>The keycap label of the tools key as shown in the hints ("Ctrl", "Alt", "Scroll").</summary>
    public static string ToolsKeyLabel(ToolsKey key) => key switch
    {
        ToolsKey.Alt => "Alt",
        ToolsKey.MouseWheelOnly => "Scroll",
        _ => "Ctrl",
    };

    /// <summary>
    /// Replaces the tiles (from <see cref="ActionCatalog.For(IReadOnlyList{string}, Capabilities)"/>), splitting by <see cref="ActionDef.Page"/>
    /// and keeping order. Resets the highlight. The page becomes Format, or Tool if there are no formats.
    /// </summary>
    public void SetActions(IReadOnlyList<ActionDef> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _formats = actions.Where(a => a.Page == WheelPage.Format).ToList();
        _tools = actions.Where(a => a.Page == WheelPage.Tool).ToList();
        Page = _formats.Count > 0 || _tools.Count == 0 ? WheelPage.Format : WheelPage.Tool;
        HighlightedIndex = null;
        Raise(WheelChange.Actions | WheelChange.Page | WheelChange.Highlight);
        RehitIfPossible();
    }

    /// <summary>Starts the neutral zone at <paramref name="entryPoint"/> (physical px): nothing highlights until the pointer leaves it. Call on <c>DragEnter</c> in drag mode.</summary>
    public void BeginNeutralZone(PointPx entryPoint)
    {
        _neutralCentre = entryPoint;
        _lastPointer = null;
        SetHighlight(null);
    }

    /// <summary>The pointer is at <paramref name="point"/> while the wheel is centred on <paramref name="centre"/> (physical px) at <paramref name="dpiScale"/>.</summary>
    public void PointerMoved(PointPx point, PointPx centre, double dpiScale)
    {
        _lastCentre = centre;
        _lastDpi = dpiScale <= 0 ? 1 : dpiScale;
        _havePointerGeometry = true;
        if (_lastPointer == point && _neutralCentre is null) return; // no real pointer movement: do not override keyboard navigation
        _lastPointer = point;
        EvaluatePointer(point);
    }

    /// <summary>The pointer left the wheel (<c>DragLeave</c>, mouse leave): nothing is highlighted.</summary>
    public void PointerLeft()
    {
        _lastPointer = null;
        SetHighlight(null);
    }

    /// <summary>Switches page. Refused (returns false) if <paramref name="page"/> has no tiles. Clears the highlight; in drag mode the tile under the pointer is re-evaluated.</summary>
    public bool SetPage(WheelPage page) => SwitchPage(page, rehit: true);

    /// <summary>Toggles between Formats and Tools; false if the other page is empty.</summary>
    public bool TogglePage() => SwitchPage(Page == WheelPage.Format ? WheelPage.Tool : WheelPage.Format, rehit: true);

    /// <summary>Picks the highlighted tile (mouse-down in click mode; Enter). Returns it, or null if nothing is highlighted.</summary>
    public ActionDef? PickHighlighted()
    {
        var action = HighlightedAction;
        if (action is not null) ActionPicked?.Invoke(action);
        return action;
    }

    /// <summary>Click-mode keyboard handling. Returns true if the key was consumed.</summary>
    public bool HandleKey(WheelKey key)
    {
        switch (key)
        {
            case WheelKey.Right:
            case WheelKey.Down:
                Step(+1);
                return true;
            case WheelKey.Left:
            case WheelKey.Up:
                Step(-1);
                return true;
            case WheelKey.Tab:
                SwitchPage(Page == WheelPage.Format ? WheelPage.Tool : WheelPage.Format, rehit: false);
                return true;
            case WheelKey.Enter:
                PickHighlighted();
                return true;
            case WheelKey.Escape:
                Cancelled?.Invoke();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Highlights tile <paramref name="index"/> directly (UI Automation, tests); null clears.</summary>
    public void Highlight(int? index)
    {
        _neutralCentre = null;
        SetHighlight(index is { } i && i >= 0 && i < CurrentActions.Count ? i : null);
    }

    private void Step(int direction)
    {
        var n = CurrentActions.Count;
        if (n == 0) return;
        _neutralCentre = null; // the user is steering by keyboard now
        int next;
        if (HighlightedIndex is not { } cur) next = direction > 0 ? 0 : n - 1;
        else next = ((cur + direction) % n + n) % n;
        SetHighlight(next);
    }

    private bool SwitchPage(WheelPage page, bool rehit)
    {
        if (page == Page) return true;
        var target = page == WheelPage.Format ? _formats : _tools;
        if (target.Count == 0) return false;
        Page = page;
        HighlightedIndex = null;
        Raise(WheelChange.Page | WheelChange.Highlight);
        if (rehit) RehitIfPossible();
        return true;
    }

    private void RehitIfPossible()
    {
        if (_havePointerGeometry && _lastPointer is { } p) EvaluatePointer(p);
    }

    private void EvaluatePointer(PointPx point)
    {
        if (_neutralCentre is { } neutral)
        {
            // The neutral zone exists so a pointer that enters ON the tiles does not instantly pick one. A pointer that entered outside the
            // hit area (wheel anchored beside a taskbar: the pointer is ~186 px from the hub) has nothing to protect: it is already neutral
            // until it moves into the ring, so the zone is dropped at once.
            var entryOnTiles = WheelGeometry.HitTest(neutral, _lastCentre, CurrentActions.Count, _lastDpi) is not null;
            if (entryOnTiles && point.DistanceTo(neutral) <= WheelGeometry.NeutralRadius * _lastDpi)
            {
                SetHighlight(null);
                return;
            }
            _neutralCentre = null;
        }
        SetHighlight(WheelGeometry.HitTest(point, _lastCentre, CurrentActions.Count, _lastDpi));
    }

    private void SetHighlight(int? index)
    {
        if (HighlightedIndex == index) return;
        HighlightedIndex = index;
        Raise(WheelChange.Highlight);
    }

    private void Raise(WheelChange change)
    {
        if ((change & (WheelChange.Highlight | WheelChange.Page | WheelChange.Actions)) != 0) change |= WheelChange.CentreText;
        if ((change & (WheelChange.Page | WheelChange.Actions)) != 0) change |= WheelChange.HubHint;
        Changed?.Invoke(change);
    }
}
