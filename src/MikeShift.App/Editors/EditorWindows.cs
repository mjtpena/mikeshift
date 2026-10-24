using Microsoft.UI.Windowing;
using MikeShift.App.Chrome;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace MikeShift.App.Editors;

/// <summary>Shared window plumbing for the editors: chrome, size, placement, foreground, beep.</summary>
internal static class EditorWindows
{
    /// <summary>The height of the title bar that is part of the client area, in device-independent pixels.</summary>
    public const int TitleBarDip = 32;

    /// <summary>
    /// Gives <paramref name="window"/> the shared chrome (Mica, icon, themed title bar with <paramref name="title"/>) and a client size
    /// in DIPs (content area below the title bar), centred on the monitor under the cursor. Editors are ordinary windows: they have a
    /// taskbar button and appear in Alt+Tab while a task is in progress (E-02), and they can be minimised.
    /// </summary>
    public static void Setup(Window window, string title, int clientWidthDip, int clientHeightDip, bool resizable = false, int minWidthDip = 0, int minHeightDip = 0)
    {
        WindowChrome.Apply(window, title);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = resizable;
        presenter.IsMaximizable = resizable;
        presenter.IsMinimizable = true;
        presenter.IsAlwaysOnTop = false;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = true;

        WindowChrome.PlaceCentered(window, clientWidthDip, clientHeightDip + TitleBarDip, minWidthDip, minHeightDip + (minHeightDip > 0 ? TitleBarDip : 0));
    }

    /// <summary>The window's last client size in DIPs for <paramref name="key"/> (clamped to the minimum), or the default.</summary>
    public static (int Width, int Height) RememberedSize(string key, int defaultWidth, int defaultHeight, int minWidth, int minHeight)
    {
        try
        {
            if (App.Current.Services.Preferences.GetWindowSize(key) is { } size)
                return (Math.Max(minWidth, size.Width), Math.Max(minHeight, size.Height));
        }
        catch (Exception)
        {
            // preferences unavailable (tests): use the default
        }
        return (defaultWidth, defaultHeight);
    }

    /// <summary>Remembers the window's current client size (DIPs, below the title bar) for <paramref name="key"/>.</summary>
    public static void RememberSize(Window window, string key)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return; // maximised or minimised
            var hwnd = new HWND(WindowNative.GetWindowHandle(window));
            var dpi = PInvoke.GetDpiForWindow(hwnd);
            var scale = dpi == 0 ? 1.0 : dpi / 96.0;
            var client = window.AppWindow.ClientSize;
            App.Current.Services.Preferences.SetWindowSize(key, (int)Math.Round(client.Width / scale), (int)Math.Round(client.Height / scale) - TitleBarDip);
        }
        catch (Exception)
        {
            // cosmetic
        }
    }

    /// <summary>Shows the window and takes the foreground even when the app was not the active one.</summary>
    public static void ShowInForeground(Window window)
    {
        window.Activate();
        var hwnd = new HWND(WindowNative.GetWindowHandle(window));
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground == hwnd) return;

        // Windows only lets the foreground thread hand out the foreground: join its input queue briefly.
        uint ownThread = PInvoke.GetCurrentThreadId();
        uint otherThread = foreground == HWND.Null ? 0 : PInvoke.GetWindowThreadProcessId(foreground, out _);
        var attached = otherThread != 0 && otherThread != ownThread && PInvoke.AttachThreadInput(ownThread, otherThread, true);
        try
        {
            PInvoke.BringWindowToTop(hwnd);
            PInvoke.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(ownThread, otherThread, false);
        }
    }

    /// <summary>The standard system "can't do that" sound.</summary>
    public static void Beep() => PInvoke.MessageBeep(MESSAGEBOX_STYLE.MB_OK);
}
