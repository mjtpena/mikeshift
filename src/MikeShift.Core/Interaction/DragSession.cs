using MikeShift.Core.Actions;

namespace MikeShift.Core.Interaction;

/// <summary>
/// Which key opens the wheel while a file is being dragged (first-run choice, Settings). Both are equal options: Shift is the default (Shift-drag is
/// rare in daily use, so it interferes least), Ctrl is the alternative (Ctrl is Explorer's "copy" key, so a stray release can never move a file).
/// The other of the two keys then switches to the Tools page, and a fresh press of the arming key returns to Formats.
/// </summary>
public enum ArmKey
{
    /// <summary>Hold Shift while dragging (default). Ctrl then switches to Tools.</summary>
    Shift,
    /// <summary>Hold Ctrl while dragging. Shift then switches to Tools.</summary>
    Ctrl,
}

/// <summary>Which control toggles the wheel to its Tools page (the <see cref="ArmKey"/> always returns to Formats; the mouse wheel always toggles).</summary>
public enum ToolsKey
{
    /// <summary>
    /// A fresh Ctrl press (default). When Ctrl is also the <see cref="ArmKey"/> this means "the other key", i.e. Shift
    /// (see <see cref="ToolsKeys.Normalize"/>).
    /// </summary>
    Ctrl,
    /// <summary>No key: only a mouse-wheel tick toggles.</summary>
    MouseWheelOnly,
    /// <summary>A fresh Alt press (warning: Left Alt+Shift is the default input-language hotkey).</summary>
    Alt,
    /// <summary>A fresh Shift press (only meaningful when <see cref="ArmKey.Ctrl"/> arms the wheel; otherwise it means Ctrl).</summary>
    Shift,
}

/// <summary>How <see cref="ArmKey"/> and <see cref="ToolsKey"/> combine: which physical modifier does what.</summary>
public static class ToolsKeys
{
    /// <summary>The modifier of <paramref name="arm"/>.</summary>
    public static Modifiers ArmModifier(ArmKey arm) => arm == ArmKey.Ctrl ? Modifiers.Ctrl : Modifiers.Shift;

    /// <summary>
    /// The tools key that makes sense next to <paramref name="arm"/>: the arming key cannot also be the tools key, so it becomes the other
    /// one of Shift/Ctrl (<c>Ctrl</c> with Ctrl arming is <c>Shift</c>; <c>Shift</c> with Shift arming is <c>Ctrl</c>). Alt and mouse wheel are unchanged.
    /// </summary>
    public static ToolsKey Normalize(ArmKey arm, ToolsKey key) => (arm, key) switch
    {
        (ArmKey.Ctrl, ToolsKey.Ctrl) => ToolsKey.Shift,
        (ArmKey.Shift, ToolsKey.Shift) => ToolsKey.Ctrl,
        _ => key,
    };

    /// <summary>The modifier whose fresh press switches to the Tools page, or <see cref="Modifiers.None"/> (mouse wheel only).</summary>
    public static Modifiers ToolsModifier(ArmKey arm, ToolsKey key) => Normalize(arm, key) switch
    {
        ToolsKey.Ctrl => Modifiers.Ctrl,
        ToolsKey.Shift => Modifiers.Shift,
        ToolsKey.Alt => Modifiers.Alt,
        _ => Modifiers.None,
    };

    /// <summary>The keycap text of the tools key as shown in hints: "Shift", "Ctrl", "Alt", "Scroll".</summary>
    public static string Label(ArmKey arm, ToolsKey key) => Normalize(arm, key) switch
    {
        ToolsKey.Shift => "Shift",
        ToolsKey.Alt => "Alt",
        ToolsKey.MouseWheelOnly => "Scroll",
        _ => "Ctrl",
    };

    /// <summary>The keycap text of the arming key: "Shift" or "Ctrl".</summary>
    public static string Label(ArmKey arm) => arm == ArmKey.Ctrl ? "Ctrl" : "Shift";
}

/// <summary>Where <see cref="DragSession"/> is in a press-drag-release cycle.</summary>
public enum DragState
{
    /// <summary>No button held (or the session was ended).</summary>
    Idle,
    /// <summary>Left button is down, the drag threshold has not been crossed.</summary>
    Pressed,
    /// <summary>Threshold crossed, wheel not requested (no Shift yet, or the wheel was hidden silently).</summary>
    Dragging,
    /// <summary>The wheel was requested (Shift) and is visible (pending or populated).</summary>
    Armed,
    /// <summary>
    /// The button is up but the wheel is still shown waiting for the OLE drop (picked) or the 400 ms dismiss timeout.
    /// (Conceptually this is "idle after LBUTTONUP"; the extra state exists so the timeout has a home.)
    /// </summary>
    Released,
}

/// <summary>What <see cref="DragSession"/> asks the wheel layer to do.</summary>
public enum DragCommandKind
{
    /// <summary>Show the wheel disc (no tiles yet) centred on <see cref="DragCommand.Point"/>; nudge the mouse so OLE re-hit-tests.</summary>
    ShowPending,
    /// <summary>Switch to <see cref="DragCommand.Page"/>.</summary>
    PageToggle,
    /// <summary>Hide the wheel without animation: no <c>DragEnter</c> arrived in time, so this was not a file drag.</summary>
    HideSilently,
    /// <summary>The button was released 400 ms ago and nothing was picked: play the dismiss animation and hide.</summary>
    DismissAfterRelease,
    /// <summary>Dismiss immediately with animation (Esc while the wheel was shown).</summary>
    Dismiss,
    /// <summary>The session is over (always the last command of a session; the session is <see cref="DragState.Idle"/> again).</summary>
    End,
}

/// <summary>A command from <see cref="DragSession"/>. <see cref="Point"/> is meaningful for ShowPending, <see cref="Page"/> for ShowPending/PageToggle.</summary>
public readonly record struct DragCommand(DragCommandKind Kind, PointPx Point = default, WheelPage Page = WheelPage.Format);

/// <summary>Tunables for <see cref="DragSession"/>. Defaults are the shipped values.</summary>
public sealed record DragSessionOptions
{
    /// <summary><c>SM_CXDRAG</c>: horizontal movement (physical px) that turns a press into a drag. The caller passes the real metric.</summary>
    public int DragThresholdX { get; init; } = 4;

    /// <summary><c>SM_CYDRAG</c>.</summary>
    public int DragThresholdY { get; init; } = 4;

    /// <summary>The key that opens the wheel while dragging (Shift by default).</summary>
    public ArmKey ArmKey { get; init; } = ArmKey.Shift;

    /// <summary>The page-toggle key for the Tools page.</summary>
    public ToolsKey ToolsKey { get; init; } = ToolsKey.Ctrl;

    /// <summary>No <c>DragEnter</c> within this long of ShowPending: hide silently (250 ms).</summary>
    public int PendingTimeoutMs { get; init; } = 250;

    /// <summary>After button-up with nothing picked, dismiss after this long (400 ms).</summary>
    public int DismissAfterReleaseMs { get; init; } = 400;

    /// <summary>A button that reads up for longer than this (and the pointer is at rest) ends the session (100 ms).</summary>
    public int ButtonUpConfirmMs { get; init; } = 100;

    /// <summary>One wheel notch, <c>WHEEL_DELTA</c>. Smaller deltas (hi-res wheels) accumulate until a notch is reached.</summary>
    public int WheelNotch { get; init; } = 120;

    /// <summary>
    /// Optional probe for the real modifier state (an OLE <c>grfKeyState</c>-style mask, e.g. from <c>GetAsyncKeyState</c>),
    /// read once at mouse-down so that a Shift held before the click is known even though the hook never saw it go down.
    /// </summary>
    public Func<uint>? QueryKeyState { get; init; }
}

/// <summary>
/// Pure press/drag/shift state machine. Feed it <see cref="InputEvent"/>s from the input monitor (in order, on one
/// thread), call <see cref="Tick"/> from a timer while <see cref="NextDeadlineMs"/> is non-null, call <see cref="DragEnterArrived"/> when the
/// wheel's <c>IDropTarget.DragEnter</c> fires, <see cref="ReconcileModifiers"/> on each <c>DragOver</c>, and <see cref="PollButtonState"/>
/// from a ~50 ms timer while the session is not idle. It answers with <see cref="DragCommand"/>s on <see cref="Command"/>.
/// <code>
/// Idle -Down-> Pressed -moved ≥ threshold-> Dragging -arm key-> Armed -Up-> Released -(pick | 400 ms)-> Idle
///                        (arm key held at crossing → Armed directly)
/// </code>
/// The arm key is Shift by default, or Ctrl (<see cref="DragSessionOptions.ArmKey"/>); the other of the two switches to the Tools page.
/// Injected input is ignored entirely. After a silent hide (no DragEnter) the same press cannot re-arm until the arm key is freshly pressed again.
/// </summary>
public sealed class DragSession
{
    private readonly DragSessionOptions _options;
    private readonly Func<long> _clock;
    private readonly ModifierTracker _modifiers = new();

    private PointPx _pressPoint;
    private PointPx _lastPoint;
    private bool _shiftAtDown;
    private bool _armKeyAtDown;
    private bool _dragEntered;
    private long _pendingSince;
    private long _releasedAt;
    private long _lastMouseAt;
    private long? _buttonUpSince;
    private int _wheelAccum;

    public DragSession(DragSessionOptions? options = null, Func<long>? clock = null)
    {
        _options = options ?? new DragSessionOptions();
        _clock = clock ?? (() => Environment.TickCount64);
        ToolsKey = _options.ToolsKey;
        ArmKey = _options.ArmKey;
    }

    /// <summary>Raised synchronously, in order, for every command. Handlers must not call back into the session's input methods.</summary>
    public event Action<DragCommand>? Command;

    /// <summary>The current state.</summary>
    public DragState State { get; private set; }

    /// <summary>The page the wheel is on from the session's point of view (reset to Format by ShowPending).</summary>
    public WheelPage Page { get; private set; } = WheelPage.Format;

    /// <summary>The key that toggles to the Tools page; may be changed at any time (Settings). It is always read through <see cref="ToolsKeys.Normalize"/> next to <see cref="ArmKey"/>.</summary>
    public ToolsKey ToolsKey { get; set; }

    /// <summary>The key that opens the wheel while dragging; may be changed at any time (Settings).</summary>
    public ArmKey ArmKey { get; set; }

    /// <summary>Shift was down when the current press started.</summary>
    public bool ShiftAtMouseDown => _shiftAtDown;

    /// <summary>The <see cref="ArmKey"/> was down when the current press started (feeds <see cref="FilterSelection"/>: a Shift-click or Ctrl-click that extended the selection).</summary>
    public bool ArmKeyAtMouseDown => _armKeyAtDown;

    /// <summary>Where the current press started.</summary>
    public PointPx PressPoint => _pressPoint;

    /// <summary>The last pointer position seen.</summary>
    public PointPx LastPoint => _lastPoint;

    /// <summary>True once <see cref="DragEnterArrived"/> was called during the current armed period.</summary>
    public bool DragEntered => _dragEntered;

    /// <summary>The modifier tracker (also fed by <see cref="Process"/>).</summary>
    public ModifierTracker Tracker => _modifiers;

    /// <summary>True while a press-drag-release cycle is in progress.</summary>
    public bool IsActive => State != DragState.Idle;

    /// <summary>
    /// The clock value at which <see cref="Tick"/> will next do something, or null if nothing is timed.
    /// Lets the host schedule a single timer instead of polling.
    /// </summary>
    public long? NextDeadlineMs
    {
        get
        {
            switch (State)
            {
                case DragState.Armed when !_dragEntered:
                    return _pendingSince + _options.PendingTimeoutMs;
                case DragState.Released:
                    var dismiss = _releasedAt + _options.DismissAfterReleaseMs;
                    if (!_dragEntered) return Math.Min(dismiss, _pendingSince + _options.PendingTimeoutMs);
                    return dismiss;
                default:
                    return null;
            }
        }
    }

    /// <summary>Feeds one input event. Injected events are dropped.</summary>
    public void Process(in InputEvent e)
    {
        if (e.Injected) return;
        var now = _clock();

        switch (e.Kind)
        {
            case InputKind.LButtonDown:
                OnButtonDown(e.Point, now);
                break;

            case InputKind.LButtonUp:
                _lastPoint = e.Point;
                OnButtonUp(now);
                break;

            case InputKind.MouseMove:
                _lastPoint = e.Point;
                _lastMouseAt = now;
                OnMove(e.Point, now);
                break;

            case InputKind.KeyDown:
            case InputKind.KeyUp:
                if (e.Kind == InputKind.KeyDown && e.Vk == VirtualKeys.Escape && State == DragState.Armed)
                {
                    EscapeDismiss();
                    break;
                }
                HandleFresh(_modifiers.OnEvent(e), now);
                break;

            case InputKind.MouseWheel:
                OnWheel(e.Delta);
                break;
        }
    }

    /// <summary>Reconciles modifiers with an OLE <c>grfKeyState</c> (call on every <c>DragEnter</c>/<c>DragOver</c>). A press the hook missed acts like a key event.</summary>
    public void ReconcileModifiers(uint grfKeyState)
    {
        if (State == DragState.Idle)
        {
            _modifiers.Reconcile(grfKeyState);
            return;
        }
        HandleFresh(_modifiers.Reconcile(grfKeyState), _clock());
    }

    /// <summary>The wheel's drop target received <c>DragEnter</c>: this really is an OLE drag, cancel the silent-hide timer.</summary>
    public void DragEnterArrived()
    {
        if (State is DragState.Armed or DragState.Released) _dragEntered = true;
    }

    /// <summary>
    /// Reports the physical left-button state from <c>GetAsyncKeyState</c>. If the button reads up for longer than
    /// <see cref="DragSessionOptions.ButtonUpConfirmMs"/> while the pointer is at rest, the (missed) button-up is synthesised.
    /// </summary>
    public void PollButtonState(bool isDown)
    {
        if (State is DragState.Idle or DragState.Released) return;
        var now = _clock();
        if (isDown)
        {
            _buttonUpSince = null;
            return;
        }
        if (now - _lastMouseAt <= _options.ButtonUpConfirmMs)
        {
            _buttonUpSince = null; // still moving: not at rest, do not trust a single sample
            return;
        }
        _buttonUpSince ??= now;
        if (now - _buttonUpSince.Value > _options.ButtonUpConfirmMs)
            OnButtonUp(now);
    }

    /// <summary>The user picked a tile (drop happened): the session ends now without a dismiss animation.</summary>
    public void Picked()
    {
        if (State is DragState.Armed or DragState.Released) End();
    }

    /// <summary>Ends whatever is in progress immediately (shutdown, a click-mode wheel taking over, ...). Emits <see cref="DragCommandKind.End"/>.</summary>
    public void Abort()
    {
        if (State != DragState.Idle) End();
    }

    /// <summary>Overrides the session's notion of the current page (the model refused a toggle because the page is empty).</summary>
    public void SetPage(WheelPage page) => Page = page;

    /// <summary>Evaluates timers against the injected clock. Cheap; safe to call as often as desired.</summary>
    public void Tick()
    {
        var now = _clock();
        switch (State)
        {
            case DragState.Armed when !_dragEntered && now >= _pendingSince + _options.PendingTimeoutMs:
                Emit(new DragCommand(DragCommandKind.HideSilently));
                State = DragState.Dragging; // suppressed: only a fresh Shift press arms again
                break;

            case DragState.Released:
                if (!_dragEntered && now >= _pendingSince + _options.PendingTimeoutMs)
                {
                    Emit(new DragCommand(DragCommandKind.HideSilently));
                    End();
                }
                else if (now >= _releasedAt + _options.DismissAfterReleaseMs)
                {
                    Emit(new DragCommand(DragCommandKind.DismissAfterRelease));
                    End();
                }
                break;
        }
    }

    /// <summary>
    /// The Shift-click gotcha. If Shift was down at mouse-down (the user Shift-clicked to extend a selection),
    /// more than one item is selected, the previous conversion finished less than 20 s ago and at least one selected item is not one of its
    /// outputs, the previous outputs are removed from the selection. Otherwise <paramref name="paths"/> is returned unchanged.
    /// </summary>
    public static IReadOnlyList<string> FilterSelection(
        IReadOnlyList<string> paths,
        bool shiftAtDown,
        IReadOnlyCollection<string>? lastOutputs,
        DateTimeOffset? lastFinishedAt,
        DateTimeOffset now)
    {
        if (!shiftAtDown || paths.Count <= 1 || lastOutputs is not { Count: > 0 } || lastFinishedAt is not { } finished) return paths;
        var elapsed = now - finished;
        if (elapsed < TimeSpan.Zero || elapsed >= ShiftClickWindow) return paths;

        var produced = new HashSet<string>(lastOutputs.Select(NormalisePath), StringComparer.OrdinalIgnoreCase);
        var fresh = paths.Where(p => !produced.Contains(NormalisePath(p))).ToList();
        return fresh.Count == 0 || fresh.Count == paths.Count ? paths : fresh;
    }

    /// <summary>How long after a conversion finishes the Shift-click exclusion applies.</summary>
    public static readonly TimeSpan ShiftClickWindow = TimeSpan.FromSeconds(20);

    private static string NormalisePath(string path)
    {
        try { path = Path.GetFullPath(path); } catch (ArgumentException) { /* keep as given */ }
        return path.Length > 3 ? path.TrimEnd('\\', '/') : path;
    }

    // ------------------------------------------------------------------ transitions

    private void OnButtonDown(PointPx pt, long now)
    {
        if (State == DragState.Released) Finish(); // an unpicked wheel from the previous press: dismiss it, then start over
        if (State != DragState.Idle) End();         // a button-down without a button-up in between: the up was missed

        if (_options.QueryKeyState is { } query) _modifiers.Reconcile(query());

        _pressPoint = _lastPoint = pt;
        _lastMouseAt = now;
        _shiftAtDown = _modifiers.Shift;
        _armKeyAtDown = IsDown(ArmKey);
        _dragEntered = false;
        _buttonUpSince = null;
        _wheelAccum = 0;
        Page = WheelPage.Format;
        State = DragState.Pressed;
    }

    private void OnMove(PointPx pt, long now)
    {
        if (State != DragState.Pressed) return;
        if (Math.Abs(pt.X - _pressPoint.X) < _options.DragThresholdX && Math.Abs(pt.Y - _pressPoint.Y) < _options.DragThresholdY) return;

        State = DragState.Dragging;
        if (IsDown(ArmKey)) Arm(now); // the arm key held since before the click (or pressed mid-press): arm as soon as it is a drag
    }

    private void OnButtonUp(long now)
    {
        switch (State)
        {
            case DragState.Pressed:
            case DragState.Dragging:
                End();
                break;
            case DragState.Armed:
                State = DragState.Released;
                _releasedAt = now;
                break;
        }
    }

    private void OnWheel(int delta)
    {
        if (State != DragState.Armed || delta == 0) return;
        if (Math.Sign(delta) != Math.Sign(_wheelAccum)) _wheelAccum = 0;
        _wheelAccum += delta;
        if (Math.Abs(_wheelAccum) < _options.WheelNotch) return;
        _wheelAccum = 0;
        SwitchPage(Page == WheelPage.Format ? WheelPage.Tool : WheelPage.Format);
    }

    private void HandleFresh(Modifiers fresh, long now)
    {
        if (fresh == Modifiers.None) return;
        var arm = ToolsKeys.ArmModifier(ArmKey);
        if (State == DragState.Dragging && (fresh & arm) != 0)
        {
            Arm(now);
            return;
        }
        if (State != DragState.Armed) return;

        if ((fresh & arm) != 0) SwitchPage(WheelPage.Format);
        var tools = ToolsKeys.ToolsModifier(ArmKey, ToolsKey);
        if (tools != Modifiers.None && (fresh & tools) != 0) SwitchPage(WheelPage.Tool);
    }

    private bool IsDown(ArmKey key) => key == ArmKey.Ctrl ? _modifiers.Ctrl : _modifiers.Shift;

    private void Arm(long now)
    {
        State = DragState.Armed;
        _dragEntered = false;
        _pendingSince = now;
        _wheelAccum = 0;
        Page = WheelPage.Format;
        Emit(new DragCommand(DragCommandKind.ShowPending, _lastPoint, Page));
    }

    private void SwitchPage(WheelPage page)
    {
        if (Page == page) return;
        Page = page;
        Emit(new DragCommand(DragCommandKind.PageToggle, _lastPoint, page));
    }

    private void EscapeDismiss()
    {
        Emit(new DragCommand(DragCommandKind.Dismiss));
        End();
    }

    /// <summary>Released-state replacement by a new press: dismiss the leftover wheel.</summary>
    private void Finish()
    {
        Emit(new DragCommand(DragCommandKind.DismissAfterRelease));
        End();
    }

    private void End()
    {
        State = DragState.Idle;
        _dragEntered = false;
        _buttonUpSince = null;
        Emit(new DragCommand(DragCommandKind.End));
    }

    private void Emit(DragCommand command) => Command?.Invoke(command);
}
