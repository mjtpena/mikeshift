using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using MikeShift.Core.Feedback;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace MikeShift.App.Hud;

/// <summary>
/// The HUD toast: a small, topmost, non-activating, taskbar-less window at the bottom centre of
/// the monitor under the cursor. A status glyph (progress ring, check, warning), a title and detail line, and "Show in folder" when a
/// file was written. Updates in place; hides after <see cref="ToastContent.Lifetime"/> (paused while the pointer is over it).
/// <para>
/// Frame: DWM draws the (rounded) window edge and its border colour, the XAML fill is square and covers the whole client area, so
/// there is no hairline or corner sliver. Size: the <i>client</i> area is sized to the measured content (<c>ResizeClient</c>), so text
/// is never clipped. First frame: the window is cloaked until the first XAML frame has been composed, so it never flashes black.
/// </para>
/// Must be created and used on the UI thread (use <see cref="HudController"/> from anywhere else).
/// </summary>
public sealed partial class ToastWindow : Window
{
    private const double BottomMarginDip = 56;
    private const double MinWidthDip = 240;

    private readonly HWND _hwnd;
    private readonly SUBCLASSPROC _subclass;
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _uncloakFallback;
    private ToastContent? _content;
    private string? _revealPath;
    private bool _visible;
    private bool _uncloakPending;
    private int _framesSeen;
    private ulong _monitorId;

    public ToastWindow()
    {
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        _hwnd = new HWND(WindowNative.GetWindowHandle(this));

        ApplyToolStyles();
        SetAlpha(255);
        _subclass = SubclassProc;
        if (!PInvoke.SetWindowSubclass(_hwnd, _subclass, 1, 0))
            Wheel.WheelLog.Error("toast: SetWindowSubclass failed, the window keeps its classic frame");
        PInvoke.SetWindowPos(_hwnd, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        // Hidden from the first instant: nothing may be visible before the first XAML frame exists.
        SetCloak(true);
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        ApplyFrameTheme();
        Root.ActualThemeChanged += (_, _) => ApplyFrameTheme();

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideToast();

        _uncloakFallback = DispatcherQueue.CreateTimer();
        _uncloakFallback.IsRepeating = false;
        _uncloakFallback.Interval = TimeSpan.FromMilliseconds(400);
        _uncloakFallback.Tick += (_, _) => Uncloak();

        _fadeTimer = DispatcherQueue.CreateTimer();
        _fadeTimer.IsRepeating = true;
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(15);
        _fadeTimer.Tick += (_, _) => OnFadeTick();

        Root.PointerEntered += (_, _) => _hideTimer.Stop();
        // A progress toast stays until the job's result replaces it; the others hide shortly after the pointer leaves.
        Root.PointerExited += (_, _) => RestartHideTimer(_content is { Kind: ToastKind.Progress } c ? c.Lifetime : TimeSpan.FromSeconds(1.5));
    }

    /// <summary>The text currently shown (empty when hidden).</summary>
    public string CurrentText => _visible && _content is { } c ? c.Text : string.Empty;

    /// <summary>True while the toast is on screen.</summary>
    public bool IsToastVisible => _visible;

    /// <summary>The window handle (tests and the capture harness).</summary>
    internal nint Handle => WindowNative.GetWindowHandle(this);

    /// <summary>Shows or updates the toast and restarts its hide timer.</summary>
    public void ShowContent(ToastContent content)
    {
        _content = content;
        _revealPath = content.RevealPath;
        Apply(content);
        Place();

        if (!_visible)
        {
            // A fade-out may still be running (HideToast, then a new result a moment later): its completion would hide the window we
            // are about to show again, and it leaves the opacity part-way. Cancel it.
            _fadeTimer.Stop();
            _fadeDone = null;
            SetAlpha(255);

            // Compose the first frame while cloaked, then reveal it: SW_SHOWNOACTIVATE shows without stealing focus.
            SetCloak(true);
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            _visible = true;
            _uncloakPending = true;
            _framesSeen = 0;
            CompositionTarget.Rendering += OnRendering;
            _uncloakFallback.Stop();
            _uncloakFallback.Start();
        }

        // Keep it above other topmost windows without activating.
        PInvoke.SetWindowPos(_hwnd, new HWND(-1), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        RestartHideTimer(content.Lifetime);
        Announce(content);
    }

    /// <summary>Shows plain text as an info toast.</summary>
    public void ShowText(string text) => ShowContent(new ToastContent(text));

    /// <summary>Hides the toast now.</summary>
    public void HideToast()
    {
        _hideTimer.Stop();
        if (!_visible) return;
        _visible = false;
        CompositionTarget.Rendering -= OnRendering;
        _uncloakFallback.Stop();
        var wasHidden = _uncloakPending; // never revealed: nothing to fade out
        _uncloakPending = false;
        if (wasHidden || !Wheel.WheelTheme.ReadAnimationsEnabled())
        {
            FinishHide();
            return;
        }
        Fade(1, 0, FadeOut, 0, FinishHide);
    }

    private void FinishHide()
    {
        _fadeTimer.Stop();
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        SetAlpha(255);
    }

    // ---- motion: 167 ms decelerating fade in with an 8 px rise, 100 ms fade out; none when animations are off ----

    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(167);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(100);
    private const int RisePixelsDip = 8;

    private readonly DispatcherQueueTimer _fadeTimer;
    private Action? _fadeDone;
    private DateTime _fadeStart;
    private double _fadeFrom, _fadeTo;
    private TimeSpan _fadeLength;
    private int _riseFrom;
    private PointInt32 _restPosition;

    /// <summary>Animates the window's opacity from <paramref name="from"/> to <paramref name="to"/> (0..1) and its top edge from <paramref name="rise"/> px below its resting place.</summary>
    private void Fade(double from, double to, TimeSpan length, int rise, Action? done)
    {
        _fadeFrom = from;
        _fadeTo = to;
        _fadeLength = length;
        _riseFrom = rise;
        _fadeDone = done;
        _fadeStart = DateTime.UtcNow;
        SetAlpha((byte)Math.Round(from * 255));
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        var t = Math.Clamp((DateTime.UtcNow - _fadeStart) / _fadeLength, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3); // decelerate
        SetAlpha((byte)Math.Round((_fadeFrom + (_fadeTo - _fadeFrom) * eased) * 255));
        if (_riseFrom != 0)
            AppWindow.Move(new PointInt32(_restPosition.X, _restPosition.Y + (int)Math.Round(_riseFrom * (1 - eased))));
        if (t < 1) return;
        _fadeTimer.Stop();
        _fadeDone?.Invoke();
    }

    private void SetAlpha(byte alpha) =>
        PInvoke.SetLayeredWindowAttributes(_hwnd, new COLORREF(0), alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

    /// <summary>
    /// A tool popup that takes no focus and has no taskbar or Alt+Tab entry. WinUI keeps WS_DLGFRAME and WS_EX_WINDOWEDGE on a window
    /// whose presenter has no border (the styles cannot be removed: they are put back), and Windows draws those as a classic 3D frame,
    /// the white + button-face grey hairline. Instead the whole window is declared client area (see
    /// <see cref="SubclassProc"/>), so there is no non-client frame left to draw.
    /// </summary>
    private void ApplyToolStyles()
    {
        var ex = (nint)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_LAYERED);
        ex &= ~(nint)WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        _ = PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
    }

    private LRESULT SubclassProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nuint subclassId, nuint refData)
    {
        // WM_NCCALCSIZE with wParam TRUE and a result of 0: the client area is the entire window rectangle.
        if (message == PInvoke.WM_NCCALCSIZE && wParam.Value != 0) return default;
        // The window is going away: take the subclass off so the procedure is never called after its delegate is released.
        if (message == PInvoke.WM_NCDESTROY) PInvoke.RemoveWindowSubclass(hwnd, _subclass, 1);
        return PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void RestartHideTimer(TimeSpan after)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = after;
        _hideTimer.Start();
    }

    // ---- content ----

    private void Apply(ToastContent c)
    {
        TitleText.Text = c.Title;
        DetailText.Text = c.Detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrEmpty(c.Detail) ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.Visibility = c.HasAction ? Visibility.Visible : Visibility.Collapsed;

        Ring.IsActive = c.Kind == ToastKind.Progress;
        Ring.Visibility = c.Kind == ToastKind.Progress ? Visibility.Visible : Visibility.Collapsed;

        switch (c.Kind)
        {
            case ToastKind.Success:
                ShowGlyph("", "SystemFillColorSuccessBrush");
                break;
            case ToastKind.Warning:
                ShowGlyph("", "SystemFillColorCautionBrush");
                break;
            default:
                StatusIcon.Visibility = Visibility.Collapsed;
                break;
        }
        StatusHost.Visibility = c.Kind == ToastKind.Info ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowGlyph(string glyph, string brushKey)
    {
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = (Brush)Application.Current.Resources[brushKey];
        StatusIcon.Visibility = Visibility.Visible;
    }

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        if (_revealPath is { } path) ShellReveal.ShowInFolder(path);
        HideToast();
    }

    /// <summary>
    /// Tells Narrator what the toast says. Windows only reliably announces live regions of the focused window, so this raises a
    /// notification event on the text's automation peer, the same path system toasts use.
    /// </summary>
    private void Announce(ToastContent c)
    {
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(TitleText) ?? FrameworkElementAutomationPeer.CreatePeerForElement(TitleText);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent,
                c.SpokenText, "MikeShiftResult");
        }
        catch (Exception)
        {
            // accessibility is best effort; never worth a crash
        }
    }

    // ---- frame ----

    private void ApplyFrameTheme()
    {
        var dark = Root.ActualTheme != ElementTheme.Light;
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1u : 0u);
        // A thin, theme-matched border drawn by DWM along the rounded edge (no white hairline, no corner slivers).
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, dark ? 0x00454545u : 0x00D0D0D0u);
    }

    private unsafe void SetDwm(DWMWINDOWATTRIBUTE attribute, uint value) =>
        _ = PInvoke.DwmSetWindowAttribute(_hwnd, attribute, &value, sizeof(uint));

    private void SetCloak(bool cloak) => SetDwm(DWMWINDOWATTRIBUTE.DWMWA_CLOAK, cloak ? 1u : 0u);

    private void OnRendering(object? sender, object e)
    {
        // The first tick is the first time the controls have their templates (a button measures 0 wide before that): measure and size
        // again now, let the resized window compose, then reveal it.
        _framesSeen++;
        if (_framesSeen == 1) Place();
        else if (_framesSeen >= 3) Uncloak();
    }

    private void Uncloak()
    {
        CompositionTarget.Rendering -= OnRendering;
        _uncloakFallback.Stop();
        if (!_uncloakPending) return;
        _uncloakPending = false;
        // Make sure there is something to reveal: a hide that raced with this show may have hidden the window.
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        if (Wheel.WheelTheme.ReadAnimationsEnabled())
        {
            var rise = (int)Math.Round(RisePixelsDip * (PInvoke.GetDpiForWindow(_hwnd) / 96.0));
            AppWindow.Move(new PointInt32(_restPosition.X, _restPosition.Y + rise));
            SetAlpha(0);
            SetCloak(false);
            Fade(0, 1, FadeIn, rise, null);
        }
        else
        {
            SetCloak(false);
        }
    }


    /// <summary>Sizes the client area to the content and puts the window at the bottom centre of the monitor under the cursor.</summary>
    private void Place()
    {
        PInvoke.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;

        // GetDpiForWindow reports the scale of the monitor the window is on. Only when the cursor moved to another monitor is the window
        // moved onto it first (to its corner); on every other update the window is already there and is not shifted, so there is no
        // one-frame jump to the corner while it is on screen.
        var monitor = area.DisplayId.Value;
        if (monitor != _monitorId)
        {
            AppWindow.Move(new PointInt32(work.X, work.Y));
            _monitorId = monitor;
        }
        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        Root.MinWidth = MinWidthDip;
        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = Root.DesiredSize;

        // ResizeClient (not MoveAndResize): the frame and DPI rounding never eat into the content, so nothing is clipped.
        var width = (int)Math.Ceiling(desired.Width * scale);
        var height = (int)Math.Ceiling(desired.Height * scale);
        AppWindow.ResizeClient(new SizeInt32(width, height));

        var outer = AppWindow.Size;
        var x = work.X + (work.Width - outer.Width) / 2;
        var y = work.Y + work.Height - outer.Height - (int)(BottomMarginDip * scale);
        _restPosition = new PointInt32(x, y);
        AppWindow.Move(_restPosition);
    }
}
