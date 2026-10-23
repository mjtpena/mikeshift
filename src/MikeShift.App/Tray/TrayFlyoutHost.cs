using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MikeShift.App.Editors;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;
using WinUIEx;

namespace MikeShift.App.Tray;

/// <summary>
/// Hosts the tray <see cref="MenuFlyout"/> (UX review T-01). A WinUI flyout needs a XAML element in a window to open from, and a tray app
/// has no window, so this owns a tiny, transparent, tool-style window parked at the cursor and shows the flyout from it. The flyout is
/// not constrained to that window (<see cref="FlyoutBase.ShouldConstrainToRootBounds"/> = false), so WinUI gives it its own popup
/// window with the system acrylic, shadow, rounded corners and open animation, and it follows light, dark and high contrast like any
/// other Windows 11 menu. It opens above the pointer when the pointer is in the lower half of its monitor (a bottom taskbar) and below
/// it otherwise. It takes keyboard focus (Esc, arrows, Enter, Space) and closes on any click outside, Esc or a switch to another window.
/// <para>
/// The light dismiss depends on the host window being the foreground (and then losing it). When Windows refuses the foreground (an
/// elevated window in front of a second launch, a foreground lock) the host is never activated and would never be deactivated, so the flyout
/// could stay open for good. In that case a short watchdog timer closes the flyout as soon as the foreground belongs to another program.
/// </para>
/// </summary>
internal sealed class TrayFlyoutHost : IDisposable
{
    private Window? _window;
    private Grid? _anchor;
    private MenuFlyout? _flyout;
    private DispatcherQueueTimer? _watchdog;

    /// <summary>How often the watchdog looks at the foreground window.</summary>
    internal static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>True while the flyout is open.</summary>
    public bool IsOpen => _flyout?.IsOpen == true;

    /// <summary>The host window's handle (0 until first shown; tests and the capture harness).</summary>
    internal nint Handle => _window is null ? 0 : WindowNative.GetWindowHandle(_window);

    /// <summary>Opens <paramref name="flyout"/> at the physical screen point <paramref name="cursor"/>. UI thread only.</summary>
    public void Show(MenuFlyout flyout, PointInt32 cursor)
    {
        EnsureWindow();
        var window = _window!;

        var area = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Nearest);
        var work = area.WorkArea;
        var below = cursor.Y < work.Y + work.Height / 2;
        var alignRight = cursor.X > work.X + work.Width / 2;

        // The window itself is invisible; it only has to exist at the pointer and be the foreground window so the flyout is light-dismissed.
        window.AppWindow.MoveAndResize(new RectInt32(cursor.X, cursor.Y, 2, 2));
        window.AppWindow.Show(activateWindow: false);
        var foreground = TakeForeground(window);

        if (_flyout is not null) _flyout.Closed -= OnClosed;
        _flyout = flyout;
        flyout.ShouldConstrainToRootBounds = false;
        flyout.Closed += OnClosed;
        var options = new FlyoutShowOptions
        {
            // Standard, not Transient: a transient flyout never takes focus, so Esc, the arrows and Enter went to the empty host window.
            // Standard moves keyboard focus into the menu (focus rings only appear for keyboard use) and is light-dismissed all the same.
            ShowMode = FlyoutShowMode.Standard,
            Placement = below
                ? (alignRight ? FlyoutPlacementMode.BottomEdgeAlignedRight : FlyoutPlacementMode.BottomEdgeAlignedLeft)
                : (alignRight ? FlyoutPlacementMode.TopEdgeAlignedRight : FlyoutPlacementMode.TopEdgeAlignedLeft),
        };

        void Open()
        {
            flyout.ShowAt(_anchor, options);
            // Without the foreground the host is never deactivated, so nothing would light-dismiss the flyout: watch for it ourselves.
            if (!foreground) StartWatchdog();
        }

        // The very first time, the anchor is not in a live visual tree yet (flyouts cannot open from such an element): wait for it.
        if (_anchor!.IsLoaded)
        {
            Open();
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            _anchor.Loaded -= OnLoaded;
            Open();
        }
        _anchor.Loaded += OnLoaded;
    }

    /// <summary>Closes the flyout if it is open.</summary>
    public void Hide() => _flyout?.Hide();

    // ---- watchdog (only when the host could not become the foreground window) ----

    private void StartWatchdog()
    {
        if (_window is null) return;
        _watchdog ??= CreateWatchdog();
        _watchdog.Start();
    }

    private DispatcherQueueTimer CreateWatchdog()
    {
        var timer = _window!.DispatcherQueue.CreateTimer();
        timer.Interval = WatchdogInterval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => CheckForeground();
        return timer;
    }

    private void StopWatchdog() => _watchdog?.Stop();

    /// <summary>Closes the flyout when the foreground window is neither the host nor one of the flyout's own popup windows.</summary>
    private void CheckForeground()
    {
        if (_flyout?.IsOpen != true || _window is null)
        {
            StopWatchdog();
            return;
        }
        var host = new HWND(WindowNative.GetWindowHandle(_window));
        var foreground = PInvoke.GetForegroundWindow();
        if (!BelongsToFlyout(foreground, host)) _flyout.Hide();
    }

    /// <summary>
    /// The host window, or a window of this process that is not an ordinary app window: the flyout's acrylic popup is a separate top-level
    /// window of another class than the <c>WinUIDesktopWin32WindowClass</c> windows (Settings, the toast, dialogs) that must close it.
    /// </summary>
    private static unsafe bool BelongsToFlyout(HWND foreground, HWND host)
    {
        if (foreground == host) return true;
        if (foreground == HWND.Null) return true; // nothing is foreground for a moment (windows are switching): look again at the next tick
        _ = PInvoke.GetWindowThreadProcessId(foreground, out var processId);
        if (processId != Environment.ProcessId) return false;
        Span<char> name = stackalloc char[64];
        int length;
        fixed (char* p = name) length = PInvoke.GetClassName(foreground, p, name.Length);
        return length > 0 && !name[..length].SequenceEqual("WinUIDesktopWin32WindowClass");
    }

    /// <summary>
    /// Makes <paramref name="window"/> the foreground window. The process that handles a tray callback may take the foreground with a
    /// plain <c>SetForegroundWindow</c>, and that keeps Windows' activation state honest: the thread-attach trick of
    /// <see cref="EditorWindows.ShowInForeground"/> left the host "active" after another window had taken the foreground, so the flyout
    /// (which closes when its window is deactivated) never closed on a switch to another app. The trick stays as the fallback for when
    /// the process has no foreground rights (a second launch forwarding to this instance, dev scenes).
    /// </summary>
    private static bool TakeForeground(Window window)
    {
        window.Activate();
        var hwnd = new HWND(WindowNative.GetWindowHandle(window));
        PInvoke.SetForegroundWindow(hwnd);
        if (PInvoke.GetForegroundWindow() != hwnd) EditorWindows.ShowInForeground(window);
        return PInvoke.GetForegroundWindow() == hwnd;
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;

        _anchor = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        var window = new Window { Title = "MikeShift tray menu", Content = _anchor };
        window.SystemBackdrop = new TransparentTintBackdrop();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        var hwnd = new HWND(WindowNative.GetWindowHandle(window));
        var ex = (nint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_LAYERED);
        ex &= ~(nint)WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        _ = PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
        // Fully transparent: the window must exist (and be the foreground one) at the pointer, but nothing of it may be seen.
        PInvoke.SetLayeredWindowAttributes(hwnd, new COLORREF(0), 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        _window = window;
    }

    private void OnClosed(object? sender, object e)
    {
        StopWatchdog();
        _window?.AppWindow.Hide();
    }

    public void Dispose()
    {
        StopWatchdog();
        _watchdog = null;
        if (_flyout is not null) _flyout.Closed -= OnClosed;
        _window?.Close();
        _window = null;
        _anchor = null;
        _flyout = null;
    }
}
