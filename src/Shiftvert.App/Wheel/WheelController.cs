using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Shiftvert.App.Input;
using Shiftvert.App.Tray;
using Shiftvert.Core;
using Shiftvert.Core.Actions;
using Shiftvert.Core.Interaction;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using AppToolsKey = Shiftvert.App.Settings.ToolsKey;
using CoreToolsKey = Shiftvert.Core.Interaction.ToolsKey;
using Preferences = Shiftvert.App.Settings.Preferences;

namespace Shiftvert.App.Wheel;

/// <summary>An immutable picture of the wheel for other threads (UI Automation providers read it).</summary>
internal sealed record WheelSnapshot(
    bool Visible,
    bool ClickMode,
    WheelPage Page,
    IReadOnlyList<string> Titles,
    int? Highlighted,
    string CentreText,
    ScreenRect Rect,
    double DpiScale)
{
    public static WheelSnapshot Hidden { get; } = new(false, false, WheelPage.Format, [], null, "", default, 1);
}

/// <summary>
/// Everything that happens to the wheel, on the wheel thread (PLAN §3.1, §3.2): it owns the window, the renderer, the models, the animation clock,
/// the <see cref="DragSession"/> and the OLE entry points called by <see cref="WheelDropTarget"/>. Nothing in here may throw into a native
/// caller: every public entry point is wrapped by its caller (the loop, the window procedure or the drop target).
/// </summary>
internal sealed class WheelController : IDisposable
{
    private enum Mode { None, Drag, Click }

    private const uint Infinite = 0xFFFFFFFF;
    private const long FrameMs = 16;
    private const long PollMs = 50;
    private const long NudgeRetryMs = 90;
    private static readonly double s_ticksPerSecond = Stopwatch.Frequency;

    /// <summary>
    /// How long the wheel must have been idle before the Win2D device and its buffers are released (about 45 MB of resident memory).
    /// Any left-button press re-creates them (<see cref="PrewarmIfCold"/>), which takes ~100 ms and is long finished by the time a
    /// drag has moved and Shift is pressed. <c>SHIFTVERT_TRIM_MS</c> overrides it for measurements.
    /// </summary>
    private static readonly long TrimAfterMs =
        long.TryParse(Environment.GetEnvironmentVariable("SHIFTVERT_TRIM_MS"), out var v) && v >= 0 ? v : 15_000;

    private readonly AppServices _services;
    private readonly WheelThread _thread;
    private readonly bool _installDropTarget;
    private readonly ConcurrentQueue<InputEvent> _inputs = new();

    private WheelWindow _window = null!;
    private WheelRenderer _renderer = null!;
    private WheelDropTarget? _dropTarget;
    private WheelAutomation _automation = null!;
    private DragSession _session = null!;
    private readonly WheelModel _dragModel = new(WheelMode.Drag);
    private readonly WheelModel _clickModel = new(WheelMode.Click);
    private readonly WheelAnimation _anim;
    private WheelTheme _theme = WheelTheme.Read();

    private Mode _mode;
    private ScreenRect _rect;
    private double _dpi = 1;
    private IReadOnlyList<ActionDef> _shownTiles = [];
    private IReadOnlyList<ActionDef> _outgoing = [];
    private string? _message;
    private IReadOnlyList<string> _paths = [];
    private System.Runtime.InteropServices.ComTypes.IDataObject? _dataObject;
    private ActionDef? _describedFor;
    private bool _descriptionSet;
    private PointPx _entryPoint;
    private bool _haveActions;
    private bool _needsFrame;
    private long _nextFrameMs;
    private long _pollDueTick;
    private long _nudgeRetryTick;
    private int _nudgeStage;
    private int _timerResolutionUsers;
    private long _clickForegroundCheckTick;
    private long _forcedDismissTick;
    private long _trimDueTick;
    private bool _disposed;
    private int _frames;
    private long _shownAtMs;
    private WheelSnapshot _snapshot = WheelSnapshot.Hidden;

    public WheelController(AppServices services, WheelThread thread, bool installDropTarget = true)
    {
        _services = services;
        _thread = thread;
        _installDropTarget = installDropTarget;
        _anim = new WheelAnimation(() => _theme.AnimationsEnabled);
    }

    /// <summary>Latest picture of the wheel (any thread).</summary>
    public WheelSnapshot Snapshot => Volatile.Read(ref _snapshot);

    internal WheelWindow Window => _window;
    internal WheelModel CurrentModel => _mode == Mode.Click ? _clickModel : _dragModel;
    internal bool ClickMode => _mode == Mode.Click;
    internal bool IsShown => _window is { IsShown: true };

    private static double NowSeconds => Stopwatch.GetTimestamp() / s_ticksPerSecond;
    private static long NowMs => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    // ------------------------------------------------------------------ setup

    public void Initialize()
    {
        _window = new WheelWindow();
        _window.Create();
        _window.Attach();
        _window.MouseMoved += OnWindowMouseMoved;
        _window.MouseDown += OnWindowMouseDown;
        _window.MouseLeft += () => { if (_mode == Mode.Click) _clickModel.PointerLeft(); };
        _window.KeyDown += OnWindowKeyDown;
        _window.Deactivated += OnWindowDeactivated;

        _renderer = new WheelRenderer();
        _automation = new WheelAutomation(this);
        _window.GetObjectHandler = _automation.HandleGetObject;

        if (_installDropTarget)
        {
            _dropTarget = new WheelDropTarget(this, _window.Hwnd);
            _dropTarget.Register();
        }

        _session = new DragSession(AsyncInput.CreateSessionOptions(Map(_services.Preferences.ToolsKey)));
        _session.Command += OnSessionCommand;
        _dragModel.ToolsKey = _session.ToolsKey;
        _dragModel.Changed += change => OnModelChanged(_dragModel, change);
        _clickModel.Changed += change => OnModelChanged(_clickModel, change);
        _clickModel.ActionPicked += OnClickPicked;
        _clickModel.Cancelled += () => BeginDismiss("escape (click mode)");
        _services.Preferences.Changed += OnPreferenceChanged;

        WheelLog.Info($"wheel ready (drop target: {_dropTarget is not null}; the Win2D device is created on demand)");
        WheelLog.Stage("wheel ready");
    }

    /// <summary>
    /// Draws a throw-away frame with glass, shadow, gradients, text and the icon font so the first real show does not pay for shader
    /// compilation, font loading or geometry building (PLAN §6 goal: wheel visible in &lt; 100 ms).
    /// </summary>
    public void Warmup()
    {
        // Only ever between interactions: it plays with the shared animation clock.
        if (_mode != Mode.None || _window.IsShown) return;
        try
        {
            var started = Stopwatch.GetTimestamp();
            _renderer.EnsureDevice();
            ActionDef Dummy(string title, WheelPage page, string? glyph = null) =>
                new(title, page, (_, _, _) => Task.FromResult(new ActionResult())) { Glyph = glyph };
            IReadOnlyList<ActionDef> tiles =
            [
                Dummy("PNG", WheelPage.Format), Dummy("JPG", WheelPage.Format), Dummy("TIFF", WheelPage.Format), Dummy("WebP", WheelPage.Format),
                Dummy("Compress", WheelPage.Tool, Glyphs.Compress), Dummy("Resize 50%", WheelPage.Tool, Glyphs.Resize),
            ];
            var sizePx = WheelGeometry.WindowSizePx(_dpi);
            var glass = new byte[sizePx * sizePx * 4];
            Array.Fill(glass, (byte)128);
            _renderer.SetGlass(glass, sizePx, _dpi);

            var now = NowSeconds;
            _anim.Show(now);
            _anim.TilesArrived(now);
            _anim.HighlightChanged(now, 1);
            _anim.PageChanged(now);
            var later = now + 0.05;
            for (var pass = 0; pass < 2; pass++)
            {
                _renderer.Render(new WheelScene
                {
                    SizePx = sizePx, DpiScale = _dpi, Theme = _theme, Animation = _anim, Now = later + pass * 0.3,
                    Tiles = tiles.Where(t => t.Page == WheelPage.Format).ToList(),
                    Outgoing = tiles.Where(t => t.Page == WheelPage.Tool).ToList(),
                    CentreText = "PNG", CentreBold = true, HubHint = "Ctrl  tools",
                });
            }
            _renderer.Render(new WheelScene
            {
                SizePx = sizePx, DpiScale = _dpi, Theme = _theme, Animation = _anim, Now = later + 1,
                Tiles = tiles.Where(t => t.Page == WheelPage.Tool).ToList(), CentreText = "TOOLS", Message = "Save the file first",
            });
            _renderer.ClearGlass();
            _anim.Hide();
            WheelLog.Info($"renderer warm-up took {(Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency} ms (software renderer: {_renderer.IsSoftware})");
        }
        catch (Exception ex)
        {
            WheelLog.Error("renderer warm-up failed", ex);
            try { _renderer.ClearGlass(); } catch (Exception) { }
            _anim.Hide();
        }
        _trimDueTick = Math.Max(1, Environment.TickCount64 + TrimAfterMs);
    }

    /// <summary>A left press anywhere may start a drag: make sure the renderer is ready (or keep it ready) before the gesture needs it.</summary>
    private void PrewarmIfCold()
    {
        if (_mode != Mode.None || _window.IsShown) return;
        if (_renderer.IsWarm)
        {
            if (_trimDueTick != 0) _trimDueTick = Math.Max(1, Environment.TickCount64 + TrimAfterMs);
            return;
        }
        WheelLog.Info("left press with a cold renderer: warming up");
        Warmup();
    }

    /// <summary>Hands the graphics memory back while nothing is going on.</summary>
    private void TrimMemory()
    {
        _trimDueTick = 0;
        if (_mode != Mode.None || _window.IsShown || !_renderer.IsWarm) return;
        var before = GC.GetTotalMemory(false);
        _renderer.Release();
        MemoryTrim.CompactNow();
        WheelLog.Info($"renderer released after {TrimAfterMs} ms idle (managed heap {before / 1024} KB -> {GC.GetTotalMemory(false) / 1024} KB, working set trimmed)");
    }

    private static CoreToolsKey Map(AppToolsKey key) => key switch
    {
        AppToolsKey.MouseWheelOnly => CoreToolsKey.MouseWheelOnly,
        AppToolsKey.Alt => CoreToolsKey.Alt,
        _ => CoreToolsKey.Ctrl,
    };

    private void OnPreferenceChanged(string key)
    {
        if (key != Preferences.ToolsKeyKey) return;
        _thread.Post(() =>
        {
            var mapped = Map(_services.Preferences.ToolsKey);
            _session.ToolsKey = mapped;
            _dragModel.ToolsKey = mapped;
            WheelLog.Stage("tools key = " + mapped);
        });
    }

    // ------------------------------------------------------------------ input from the hook thread

    /// <summary>Queues one hook event for the session (any thread).</summary>
    public void Enqueue(in InputEvent e)
    {
        _inputs.Enqueue(e);
        _thread.Wake();
    }

    private void DrainInput()
    {
        while (_inputs.TryDequeue(out var e))
        {
            try
            {
                if (e.Kind == InputKind.LButtonDown) PrewarmIfCold();
                _session.Process(e);
            }
            catch (Exception ex) { WheelLog.Error("session.Process failed", ex); }
        }
    }

    // ------------------------------------------------------------------ loop hooks

    public uint NextTimeoutMs()
    {
        if (!_inputs.IsEmpty) return 0;
        long best = long.MaxValue;
        var tick = Environment.TickCount64;

        if (_window is { IsShown: true } && (_needsFrame || _anim.IsAnimating(NowSeconds)))
            best = Math.Min(best, Math.Max(0, _nextFrameMs - NowMs));
        if (_session.IsActive)
        {
            best = Math.Min(best, Math.Max(0, _pollDueTick - tick));
            if (_session.NextDeadlineMs is { } deadline) best = Math.Min(best, Math.Max(0, deadline - tick));
        }
        if (_nudgeRetryTick != 0) best = Math.Min(best, Math.Max(0, _nudgeRetryTick - tick));
        if (_forcedDismissTick != 0) best = Math.Min(best, Math.Max(0, _forcedDismissTick - tick));
        if (_clickForegroundCheckTick != 0) best = Math.Min(best, Math.Max(0, _clickForegroundCheckTick - tick));
        if (_trimDueTick != 0) best = Math.Min(best, Math.Max(0, _trimDueTick - tick));
        return best == long.MaxValue ? Infinite : (uint)Math.Min(best, int.MaxValue);
    }

    public void Pump()
    {
        DrainInput();
        var tick = Environment.TickCount64;

        if (_session.IsActive)
        {
            if (tick >= _pollDueTick)
            {
                _pollDueTick = tick + PollMs;
                _session.PollButtonState(AsyncInput.IsLeftButtonDown());
            }
            _session.Tick();
        }

        if (_forcedDismissTick != 0 && tick >= _forcedDismissTick)
        {
            _forcedDismissTick = 0;
            if (_mode == Mode.Drag) BeginDismiss("the source never completed the drop");
        }
        if (_nudgeRetryTick != 0 && tick >= _nudgeRetryTick) NudgeRetry();
        if (_clickForegroundCheckTick != 0 && tick >= _clickForegroundCheckTick)
        {
            _clickForegroundCheckTick = 0;
            if (_mode == Mode.Click && _window.IsShown && !_window.IsForeground)
            {
                _window.TakeForeground();
                WheelLog.Stage("click mode: foreground retried, now foreground=" + _window.IsForeground);
            }
        }

        if (_window.IsShown && (_needsFrame || _anim.IsAnimating(NowSeconds)) && NowMs >= _nextFrameMs)
            RenderFrame();

        if (_trimDueTick != 0 && tick >= _trimDueTick)
        {
            if (_mode == Mode.None && !_window.IsShown && !_session.IsActive) TrimMemory();
            else _trimDueTick = tick + TrimAfterMs; // busy: look again later
        }
    }

    private void RequestFrame()
    {
        if (_window is { IsShown: true }) _needsFrame = true;
    }

    // ------------------------------------------------------------------ session commands

    private void OnSessionCommand(DragCommand command)
    {
        try
        {
            WheelLog.Stage($"session command {command.Kind}" + (command.Kind is DragCommandKind.ShowPending or DragCommandKind.PageToggle ? $" page={command.Page}" : ""));
            switch (command.Kind)
            {
                case DragCommandKind.ShowPending:
                    ShowDragWheel(command.Point);
                    break;
                case DragCommandKind.PageToggle:
                    if (_mode == Mode.Drag && !_dragModel.SetPage(command.Page)) _session.SetPage(_dragModel.Page);
                    break;
                case DragCommandKind.HideSilently:
                    if (_mode == Mode.Drag) HideNow("no DragEnter within the timeout (not a file drag)");
                    break;
                case DragCommandKind.DismissAfterRelease:
                    if (_mode != Mode.Drag) break;
                    if (_dataObject is not null && _forcedDismissTick == 0)
                    {
                        // The OLE drag is still attached to the wheel: its Drop (or DragLeave) is about to arrive and must still see the
                        // highlighted tile. Dismiss only if the source never completes the drop.
                        _forcedDismissTick = Environment.TickCount64 + 3000;
                        WheelLog.Stage("released while the OLE drag is still attached: waiting for Drop/DragLeave");
                    }
                    else
                    {
                        BeginDismiss("released over nothing");
                    }
                    break;
                case DragCommandKind.Dismiss:
                    if (_mode == Mode.Drag) BeginDismiss("escape");
                    break;
                case DragCommandKind.End:
                    OnSessionEnded();
                    break;
            }
        }
        catch (Exception ex)
        {
            WheelLog.Error($"session command {command.Kind} failed", ex);
        }
    }

    private void OnSessionEnded()
    {
        if (_mode == Mode.Drag && _window.IsShown && _anim.Phase == WheelPhase.Visible)
            BeginDismiss("drag session ended");
        _nudgeRetryTick = 0;
        try { Kind.ClearCache(); } catch (Exception) { /* cache housekeeping only */ }
    }

    // ------------------------------------------------------------------ showing and hiding

    private void ShowDragWheel(PointPx cursor)
    {
        if (_mode == Mode.Click) FinishHide("drag started while the click-mode wheel was open");

        _theme = WheelTheme.Read();
        _mode = Mode.Drag;
        _paths = [];
        _haveActions = false;
        _message = null;
        _outgoing = [];
        _dataObject = null;
        _forcedDismissTick = 0;
        _describedFor = null;
        _descriptionSet = false;

        var monitor = WheelWindow.MonitorAt(cursor);
        _dpi = monitor.DpiScale;
        var size = WheelGeometry.WindowSizePx(_dpi);
        _rect = WheelPlacement.ForCursor(cursor, size, monitor.WorkArea);

        _dragModel.ToolsKey = _session.ToolsKey;
        _dragModel.SetActions([]);
        _shownTiles = _dragModel.CurrentActions;

        PrepareGlass();
        _window.SetActivatable(false);
        var generation = _anim.Show(NowSeconds);
        _frames = 0;
        _shownAtMs = NowMs;
        PublishSnapshot();
        RenderFrame();
        _window.ShowNoActivate();
        PublishSnapshot();
        BeginTimerResolution();
        WheelLog.Stage($"wheel shown pending gen={generation} at ({cursor.X},{cursor.Y}) rect=({_rect.Left},{_rect.Top},{_rect.Width}) dpi={_dpi:0.##}");

        Nudge();
        _nudgeStage = 1;
        _nudgeRetryTick = Environment.TickCount64 + NudgeRetryMs;
    }

    private void PrepareGlass()
    {
        _renderer.ClearGlass();
        if (_theme.HighContrast) return;
        var pixels = ScreenCapture.Grab(_rect);
        if (pixels is not null)
        {
            try { _renderer.SetGlass(pixels, _rect.Width, _dpi); }
            catch (Exception ex) { WheelLog.Error("glass blur failed", ex); _renderer.ClearGlass(); }
        }
    }

    /// <summary>Hides immediately without animation (silent hide, non-file drags, shutdown).</summary>
    private void HideNow(string reason) => FinishHide(reason);

    private void BeginDismiss(string reason)
    {
        if (!_window.IsShown) return;
        if (_anim.Phase != WheelPhase.Visible) return;
        WheelLog.Stage("dismiss: " + reason);
        _forcedDismissTick = 0;
        ClearDescription();
        _dataObject = null;
        _clickModel.PointerLeft();
        _dragModel.PointerLeft();
        _anim.Dismiss(NowSeconds);
        RequestFrame();
    }

    private void FinishHide(string reason)
    {
        if (!_window.IsShown && _mode == Mode.None) return;
        WheelLog.Stage($"wheel hidden: {reason} (rendered {_frames} frames in {NowMs - _shownAtMs} ms)");
        _window.Hide();
        _anim.Hide();
        _renderer.ClearGlass();
        _mode = Mode.None;
        _dataObject = null;
        _paths = [];
        _haveActions = false;
        _message = null;
        _outgoing = [];
        _needsFrame = false;
        _nudgeRetryTick = 0;
        _forcedDismissTick = 0;
        _clickForegroundCheckTick = 0;
        _describedFor = null;
        _descriptionSet = false;
        _dragModel.SetActions([]);
        _clickModel.SetActions([]);
        EndTimerResolution();
        _trimDueTick = Math.Max(1, Environment.TickCount64 + TrimAfterMs);
        PublishSnapshot();
        _automation.OnHidden();
    }

    private void BeginTimerResolution()
    {
        if (_timerResolutionUsers++ == 0) PInvoke.timeBeginPeriod(1);
    }

    private void EndTimerResolution()
    {
        if (_timerResolutionUsers > 0 && --_timerResolutionUsers == 0) PInvoke.timeEndPeriod(1);
    }

    // ------------------------------------------------------------------ the zero-move nudge (PLAN §3.1 item 2)

    private static unsafe void SendRelativeMove(int dx, int dy)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi.dx = dx;
        input.Anonymous.mi.dy = dy;
        input.Anonymous.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE;
        if (PInvoke.SendInput(1, &input, sizeof(INPUT)) == 0)
            WheelLog.Error("SendInput failed, win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    private static void Nudge()
    {
        SendRelativeMove(0, 0);
        WheelLog.Stage("nudge: zero-move SendInput");
    }

    private void NudgeRetry()
    {
        _nudgeRetryTick = 0;
        if (_mode != Mode.Drag || !_window.IsShown || _session.DragEntered || _session.State != DragState.Armed) return;

        // The zero-move did not make OLE re-target. Wiggle by a single pixel and back (injected: DragSession ignores it).
        WheelLog.Stage("nudge: no DragEnter after the zero-move, wiggling by 1 px");
        SendRelativeMove(1, 0);
        SendRelativeMove(-1, 0);
        _nudgeStage = 2;
    }

    // ------------------------------------------------------------------ OLE entry points (WheelDropTarget)

    public uint OnDragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keyState, PointPx pt, uint allowedEffects)
    {
        if (_mode != Mode.Drag || !_window.IsShown || _anim.Phase != WheelPhase.Visible)
        {
            WheelLog.Stage($"DragEnter ignored (mode={_mode}, shown={_window.IsShown}, phase={_anim.Phase})");
            return DropEffects.None;
        }

        _dataObject = data;
        _nudgeRetryTick = 0;
        _session.DragEnterArrived();
        _session.ReconcileModifiers(keyState);
        _entryPoint = pt;

        var reentry = _haveActions && _paths.Count > 0;
        var payload = reentry ? null : DataObjectInspector.Inspect(data);
        WheelLog.Stage($"DragEnter at ({pt.X},{pt.Y}) keys=0x{keyState:X} nudgeStage={_nudgeStage} kind={(reentry ? "Files (re-entry)" : payload!.Kind)} paths={(reentry ? _paths.Count : payload!.Paths.Count)}"
                       + (payload is { Paths.Count: > 0 } ? " first=" + payload.Paths[0] : ""));

        if (reentry)
        {
            _dragModel.BeginNeutralZone(pt);
            return DropEffects.None;
        }

        if (payload!.Kind != DragKind.Files) WheelLog.Stage("data object formats: " + OleNative.Describe(data));
        switch (payload!.Kind)
        {
            case DragKind.Files:
                BeginCatalog(payload.Paths, pt);
                break;
            case DragKind.VirtualFiles:
                _message = "Save the file first";
                _dragModel.SetActions([]);
                RequestFrame();
                PublishSnapshot();
                break;
            default:
                HideNow("the drag carries no files");
                break;
        }
        return DropEffects.None;
    }

    private void BeginCatalog(IReadOnlyList<string> pathsFromDrag, PointPx entry)
    {
        var jobs = _services.Jobs;
        var paths = DragSession.FilterSelection(pathsFromDrag, _session.ShiftAtMouseDown, jobs.LastOutputs, jobs.LastFinishedAt, DateTimeOffset.UtcNow);
        if (paths.Count != pathsFromDrag.Count)
            WheelLog.Stage($"Shift-click rule applied: {pathsFromDrag.Count} -> {paths.Count} item(s)");
        _paths = paths;

        var generation = _anim.Generation;
        // This runs inside OLE's DragEnter on the wheel thread, and Explorer's drag loop waits for us: it must never block on the
        // startup capability probe (HEIF/Media Foundation/Word probes: well under a second warm, several when cold). If the probe is
        // not done yet, the first catalog is built from a conservative snapshot and replaced once the probe completes.
        var probed = _services.TryGetCapabilities(out var caps);
        _ = Task.Run(async () =>
        {
            ApplyOnWheelThread(generation, paths, BuildCatalog(paths, caps), entry, refresh: false);
            if (probed) return;

            Capabilities full;
            try { full = await _services.CapabilitiesAsync.ConfigureAwait(false); }
            catch (Exception ex)
            {
                WheelLog.Error("the capability probe failed", ex);
                return;
            }
            WheelLog.Stage("capability probe finished during the drag: refreshing the tiles");
            ApplyOnWheelThread(generation, paths, BuildCatalog(paths, full), entry, refresh: true);
        });
    }

    private static IReadOnlyList<ActionDef> BuildCatalog(IReadOnlyList<string> paths, Capabilities caps)
    {
        try
        {
            return ActionCatalog.For(paths, caps);
        }
        catch (Exception ex)
        {
            WheelLog.Error("ActionCatalog.For failed", ex);
            return [];
        }
    }

    private void ApplyOnWheelThread(int generation, IReadOnlyList<string> paths, IReadOnlyList<ActionDef> actions, PointPx entry, bool refresh)
    {
        _thread.Post(() =>
        {
            if (refresh) RefreshActions(generation, paths, actions);
            else ApplyActions(generation, paths, actions, entry);
        });
    }

    /// <summary>
    /// Replaces the tiles of a wheel that is already showing the first (conservative) catalog, once the capability probe is done.
    /// Keeps the page the user is on if it still has tiles; the pointer is re-evaluated against the new tiles.
    /// </summary>
    private void RefreshActions(int generation, IReadOnlyList<string> paths, IReadOnlyList<ActionDef> actions)
    {
        if (!_haveActions || !_anim.IsCurrent(generation) || _mode != Mode.Drag || !_window.IsShown || !ReferenceEquals(paths, _paths))
        {
            WheelLog.Stage("catalog refresh discarded (stale)");
            return;
        }
        var page = _dragModel.Page;
        _dragModel.SetActions(actions);
        if (page != _dragModel.Page) _dragModel.SetPage(page);
        _session.SetPage(_dragModel.Page);
        WheelLog.Stage("tiles (refreshed): " + string.Join(",", _dragModel.Formats.Select(a => a.Title)) + " | " + string.Join(",", _dragModel.Tools.Select(a => a.Title)));
        if (_lastPointer is { } p) _dragModel.PointerMoved(p, _rect.Centre, _dpi);
    }

    private void ApplyActions(int generation, IReadOnlyList<string> paths, IReadOnlyList<ActionDef> actions, PointPx entry)
    {
        if (!_anim.IsCurrent(generation) || _mode != Mode.Drag || !_window.IsShown || _anim.Phase != WheelPhase.Visible || !ReferenceEquals(paths, _paths))
        {
            WheelLog.Stage("catalog result discarded (stale)");
            return;
        }
        _haveActions = true;
        _dragModel.ToolsKey = _session.ToolsKey;
        _dragModel.BeginNeutralZone(entry);
        _dragModel.SetActions(actions);
        _session.SetPage(_dragModel.Page);
        WheelLog.Stage("tiles: " + string.Join(",", _dragModel.Formats.Select(a => a.Title)) + " | " + string.Join(",", _dragModel.Tools.Select(a => a.Title)));
        if (_lastPointer is { } p) _dragModel.PointerMoved(p, _rect.Centre, _dpi);
    }

    private PointPx? _lastPointer;

    public uint OnDragOver(uint keyState, PointPx pt, uint allowedEffects)
    {
        if (_mode != Mode.Drag || !_window.IsShown) return DropEffects.None;
        _lastPointer = pt;
        _session.ReconcileModifiers(keyState);
        _dragModel.PointerMoved(pt, _rect.Centre, _dpi);
        return _dragModel.HighlightedAction is not null ? DropEffects.Copy & allowedEffects : DropEffects.None;
    }

    public void OnDragLeave()
    {
        if (_mode != Mode.Drag) return;
        WheelLog.Stage("DragLeave");
        _lastPointer = null;
        _dragModel.PointerLeft();
        ClearDescription();
        _dataObject = null;
        if (_session.State == DragState.Released) BeginDismiss("released on the wheel without a tile");
    }

    public uint OnDrop(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keyState, PointPx pt, uint allowedEffects)
    {
        if (_mode != Mode.Drag || !_window.IsShown)
        {
            WheelLog.Stage("Drop ignored (wheel not in drag mode)");
            return DropEffects.None;
        }

        _lastPointer = pt;
        _dragModel.PointerMoved(pt, _rect.Centre, _dpi);
        var action = _dragModel.HighlightedAction;
        var paths = _paths;
        if (action is null || paths.Count == 0)
        {
            WheelLog.Stage("Drop on no tile: dismissing, nothing converted");
            _dataObject = null;
            BeginDismiss("dropped on no tile");
            return DropEffects.None;
        }

        // A Shift-drag is a MOVE to Explorer; we answer COPY and say so, so the originals stay where they are.
        DataObjectInspector.SetPerformedDropEffect(data, DropEffects.Copy);
        var effect = DropEffects.Copy & allowedEffects;
        WheelLog.Stage($"Drop on {action.Title} ({paths.Count} item(s)) effect={(effect == DropEffects.Copy ? "COPY" : effect.ToString())} allowed=0x{allowedEffects:X}");

        _forcedDismissTick = 0;
        EnqueueJob(action, paths);
        BeginPick();
        _dataObject = null;
        _session.Picked();
        return effect;
    }

    private void EnqueueJob(ActionDef action, IReadOnlyList<string> paths)
    {
        var snapshot = paths.ToArray();
        var services = _services;
        if (!services.Dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var id = services.Jobs.Enqueue(action, snapshot, services.Interaction);
                    WheelLog.Stage($"job {id} enqueued: {action.Title} on {snapshot.Length} item(s)");
                }
                catch (Exception ex)
                {
                    WheelLog.Error("enqueueing the job failed", ex);
                }
            }))
        {
            WheelLog.Error("could not enqueue the job: the UI thread is gone");
        }
    }

    private void BeginPick()
    {
        var index = CurrentModel.HighlightedIndex ?? 0;
        _anim.Pick(NowSeconds, index);
        ClearDescription();
        _rect = WheelPlacement.Resized(_rect, WheelPlacement.CollapseWindowSizePx(_dpi));
        RequestFrame();
    }

    // ------------------------------------------------------------------ drop description and model reactions

    private void ClearDescription()
    {
        if (_dataObject is { } data && _descriptionSet)
            DataObjectInspector.SetDropDescription(data, DataObjectInspector.DropImageInvalid, "", "");
        _describedFor = null;
        _descriptionSet = false;
    }

    private void UpdateDescription()
    {
        if (_mode != Mode.Drag || _dataObject is not { } data) return;
        var action = _dragModel.HighlightedAction;
        if (ReferenceEquals(action, _describedFor) && (_descriptionSet || action is null)) return;
        _describedFor = action;
        if (action is null)
        {
            if (_descriptionSet) DataObjectInspector.SetDropDescription(data, DataObjectInspector.DropImageInvalid, "", "");
            _descriptionSet = false;
        }
        else
        {
            var text = DropDescriptionText.For(action);
            _descriptionSet = DataObjectInspector.SetDropDescription(data, DataObjectInspector.DropImageCopy, text.Message, text.Insert);
        }
        WheelLog.Stage("highlight: " + (action is null ? "none" : action.Title) + $" (drop description set: {_descriptionSet})");
    }

    private void OnModelChanged(WheelModel model, WheelChange change)
    {
        if (!ReferenceEquals(model, CurrentModel) && _mode != Mode.None) return;
        var now = NowSeconds;

        if ((change & WheelChange.Actions) != 0)
        {
            _outgoing = [];
            _shownTiles = model.CurrentActions;
            _anim.TileCountHint = Math.Max(1, _shownTiles.Count);
            if (_shownTiles.Count > 0) _anim.TilesArrived(now);
            else _anim.TilesCleared();
        }
        else if ((change & WheelChange.Page) != 0)
        {
            _outgoing = _shownTiles;
            _shownTiles = model.CurrentActions;
            _anim.PageChanged(now);
            WheelLog.Stage("page = " + model.Page);
        }

        if ((change & WheelChange.Highlight) != 0)
        {
            _anim.HighlightChanged(now, model.HighlightedIndex);
            if (model.Mode == WheelMode.Drag) UpdateDescription();
            else if (_mode == Mode.Click) WheelLog.Stage("highlight: " + (model.HighlightedAction?.Title ?? "none"));
        }

        PublishSnapshot();
        if ((change & WheelChange.Highlight) != 0 && model.Mode == WheelMode.Click) _automation.OnHighlightChanged();
        RequestFrame();
    }

    private void PublishSnapshot()
    {
        var model = CurrentModel;
        var shown = _window is { IsShown: true } && _mode != Mode.None;
        Volatile.Write(ref _snapshot, shown
            ? new WheelSnapshot(true, _mode == Mode.Click, model.Page, model.CurrentActions.Select(a => a.DropDescription).ToArray(),
                model.HighlightedIndex, _message ?? model.CentreTitle, _rect, _dpi)
            : WheelSnapshot.Hidden);
    }

    // ------------------------------------------------------------------ click mode (Convert Files…)

    /// <summary>Opens the wheel for "Convert Files…" at the centre of the monitor under the cursor.</summary>
    public void ShowClickMode(IReadOnlyList<string> paths)
    {
        _ = Task.Run(() =>
        {
            IReadOnlyList<ActionDef> actions;
            try { actions = ActionCatalog.For(paths, _services.Capabilities); } // may wait for the startup probe: off the UI thread
            catch (Exception ex) { WheelLog.Error("ActionCatalog.For failed (click mode)", ex); actions = []; }
            _thread.Post(() => ShowClick(paths, actions));
        });
    }

    private void ShowClick(IReadOnlyList<string> paths, IReadOnlyList<ActionDef> actions)
    {
        if (actions.Count == 0)
        {
            _services.Hud.Show("Nothing to do with those files");
            return;
        }

        _session.Abort();
        if (_window.IsShown) FinishHide("click mode takes over");

        _theme = WheelTheme.Read();
        _mode = Mode.Click;
        _paths = paths.ToArray();
        _message = null;
        _outgoing = [];

        PInvoke.GetCursorPos(out var cursor);
        var monitor = WheelWindow.MonitorAt(new PointPx(cursor.X, cursor.Y));
        _dpi = monitor.DpiScale;
        _rect = WheelPlacement.CentredIn(monitor.WorkArea, WheelGeometry.WindowSizePx(_dpi));

        PrepareGlass();
        var generation = _anim.Show(NowSeconds);
        _frames = 0;
        _shownAtMs = NowMs;
        _clickModel.SetActions(actions);   // raises Changed: the tiles pop in together with the disc
        _shownTiles = _clickModel.CurrentActions;
        _window.SetActivatable(true);
        PublishSnapshot();
        RenderFrame();
        _window.ShowActivated();
        PublishSnapshot();
        BeginTimerResolution();
        _clickForegroundCheckTick = Environment.TickCount64 + 150;
        WheelLog.Stage("tiles: " + string.Join(",", _clickModel.Formats.Select(a => a.Title)) + " | " + string.Join(",", _clickModel.Tools.Select(a => a.Title)));
        WheelLog.Stage($"click-mode wheel shown gen={generation} rect=({_rect.Left},{_rect.Top},{_rect.Width}) tiles={actions.Count} foreground={_window.IsForeground}");
        _automation.OnShown();
    }

    private void OnClickPicked(ActionDef action)
    {
        if (_mode != Mode.Click) return;
        WheelLog.Stage("click mode picked " + action.Title);
        EnqueueJob(action, _paths);
        BeginPick();
    }

    private void OnWindowMouseMoved(PointPx screen)
    {
        if (_mode == Mode.Click && _anim.Phase == WheelPhase.Visible) _clickModel.PointerMoved(screen, _rect.Centre, _dpi);
    }

    private void OnWindowMouseDown(PointPx screen)
    {
        if (_mode != Mode.Click || _anim.Phase != WheelPhase.Visible) return;
        _clickModel.PointerMoved(screen, _rect.Centre, _dpi);
        if (_clickModel.PickHighlighted() is not null) return;

        // Outside the disc a click dismisses; on the hub it does nothing.
        if (screen.DistanceTo(_rect.Centre) > WheelGeometry.DiscRadius * _dpi) BeginDismiss("click outside the disc");
    }

    private void OnWindowKeyDown(int vk)
    {
        if (_mode != Mode.Click || _anim.Phase != WheelPhase.Visible) return;
        if (WheelKeys.TryFromVirtualKey(vk, out var key)) _clickModel.HandleKey(key);
    }

    private void OnWindowDeactivated()
    {
        if (_mode == Mode.Click && _anim.Phase == WheelPhase.Visible) BeginDismiss("focus lost");
    }

    // ------------------------------------------------------------------ drawing

    private void RenderFrame()
    {
        var now = NowSeconds;
        if (_anim.IsFinished(now))
        {
            FinishHide("animation finished");
            return;
        }

        var model = CurrentModel;
        var sizePx = _rect.Width;
        var scene = new WheelScene
        {
            SizePx = sizePx,
            DpiScale = _dpi,
            Theme = _theme,
            Animation = _anim,
            Now = now,
            Tiles = _shownTiles,
            Outgoing = _outgoing,
            CentreText = model.CentreTitle,
            CentreBold = model.CentreIsActionName,
            // Click mode toggles pages with Tab (the model's hint names the drag-mode keys).
            HubHint = _mode == Mode.Click ? (model.HasBothPages ? (model.Page == WheelPage.Format ? "Tab  tools" : "Tab  formats") : "") : model.HubHint,
            Message = _message,
            HubEmpty = _mode == Mode.Drag && _shownTiles.Count == 0 && _message is null,
        };

        try
        {
            var t0 = Stopwatch.GetTimestamp();
            var pixels = _renderer.Render(scene);
            var t1 = Stopwatch.GetTimestamp();
            _window.Present(pixels, sizePx, sizePx, _rect);
            _frames++;
            var t2 = Stopwatch.GetTimestamp();
            var renderMs = (t1 - t0) * 1000 / Stopwatch.Frequency;
            var presentMs = (t2 - t1) * 1000 / Stopwatch.Frequency;
            if (renderMs + presentMs > 40) WheelLog.Stage($"slow frame: render {renderMs} ms, present {presentMs} ms, size {sizePx}px, phase {_anim.Phase}");
        }
        catch (Exception ex)
        {
            WheelLog.Error("frame failed", ex);
            try { _renderer.RecreateDevice(); }
            catch (Exception ex2) { WheelLog.Error("device recreation failed", ex2); }
        }

        _needsFrame = false;
        _nextFrameMs = NowMs + FrameMs;
    }

    // ------------------------------------------------------------------ UI Automation entry points (any thread)

    /// <summary>UIA <c>Invoke</c> on a tile: highlight and pick it (click mode only).</summary>
    internal void InvokeTile(int index) => _thread.Post(() =>
    {
        if (_mode != Mode.Click || _anim.Phase != WheelPhase.Visible) return;
        _clickModel.Highlight(index);
        _clickModel.PickHighlighted();
    });

    /// <summary>UIA <c>SetFocus</c> on a tile: move the highlight there (click mode only).</summary>
    internal void HighlightTile(int index) => _thread.Post(() =>
    {
        if (_mode == Mode.Click && _anim.Phase == WheelPhase.Visible) _clickModel.Highlight(index);
    });

    /// <summary>UIA <c>SetFocus</c> on the root.</summary>
    internal void FocusWindow() => _thread.Post(() =>
    {
        if (_mode == Mode.Click && _window.IsShown) _window.TakeForeground();
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _services.Preferences.Changed -= OnPreferenceChanged; } catch (Exception) { }
        _dropTarget?.Dispose();
        _automation?.Disconnect();
        if (_window is not null)
        {
            _window.Hide();
            _window.Dispose();
        }
        while (_timerResolutionUsers > 0) EndTimerResolution();
        _renderer?.Dispose();
    }
}
