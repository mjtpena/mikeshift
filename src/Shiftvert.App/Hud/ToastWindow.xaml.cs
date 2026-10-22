using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Shiftvert.App.Hud;

/// <summary>
/// The HUD toast (PLAN §3.3): a small, topmost, non-activating, taskbar-less window at the bottom centre of the monitor
/// under the cursor. Updates in place and hides 2.5 s after the last change. Must be created and used on the UI thread
/// (use <see cref="HudController"/> from anywhere else).
/// </summary>
public sealed partial class ToastWindow : Window
{
    /// <summary>How long the toast stays after its last update.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(2.5);

    private const double BottomMarginDip = 56;

    private readonly HWND _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _visible;

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

        // Tool window (no taskbar/Alt-Tab entry), never takes focus, always on top.
        var ex = (nint)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);
        ex &= ~(nint)WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        _ = PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);

        // Rounded corners (Windows 11; silently ignored on Windows 10).
        unsafe
        {
            var pref = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            _ = PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &pref, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        }

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Interval = Lifetime;
        _hideTimer.Tick += (_, _) => HideToast();
    }

    /// <summary>The text currently shown (empty when hidden).</summary>
    public string CurrentText => _visible ? Label.Text : string.Empty;

    /// <summary>True while the toast is on screen.</summary>
    public bool IsToastVisible => _visible;

    /// <summary>Shows or updates the toast and restarts the 2.5 s hide timer.</summary>
    public void ShowText(string text)
    {
        Label.Text = text;
        Place();

        if (!_visible)
        {
            // SW_SHOWNOACTIVATE: show without stealing focus from Explorer or whatever the user is doing.
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            _visible = true;
        }

        // Keep it above other topmost windows without activating.
        PInvoke.SetWindowPos(_hwnd, new HWND(-1), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Hides the toast now.</summary>
    public void HideToast()
    {
        _hideTimer.Stop();
        if (!_visible) return;
        _visible = false;
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    /// <summary>Sizes to the text and puts the window at the bottom centre of the monitor under the cursor.</summary>
    private void Place()
    {
        PInvoke.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;

        // First move onto the target monitor so GetDpiForWindow reports that monitor's scale.
        AppWindow.MoveAndResize(new RectInt32(work.X, work.Y, 100, 40));
        var dpi = PInvoke.GetDpiForWindow(_hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        ((FrameworkElement)Content).Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = ((FrameworkElement)Content).DesiredSize;
        var width = (int)Math.Ceiling(Math.Max(desired.Width, 120) * scale);
        var height = (int)Math.Ceiling(Math.Max(desired.Height, 40) * scale);

        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + work.Height - height - (int)(BottomMarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
