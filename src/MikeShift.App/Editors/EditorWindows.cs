using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace MikeShift.App.Editors;

/// <summary>Shared window plumbing for the floating editors: size, placement, topmost, foreground, beep.</summary>
internal static class EditorWindows
{
    /// <summary>
    /// Makes <paramref name="window"/> a fixed-size, topmost, taskbar-less window with the given client size (in DIPs),
    /// centred on the monitor under the cursor (the monitor the user is working on).
    /// </summary>
    public static void Setup(Window window, string title, int clientWidthDip, int clientHeightDip)
    {
        window.Title = title;
        window.SystemBackdrop = new MicaBackdrop();

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "MikeShift.ico");
        if (File.Exists(icon))
        {
            try { window.AppWindow.SetIcon(icon); }
            catch (Exception) { /* cosmetic */ }
        }

        PInvoke.GetCursorPos(out var cursor);
        var work = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;

        // Move onto the target monitor first so GetDpiForWindow reports that monitor's scale.
        window.AppWindow.Move(new PointInt32(work.X, work.Y));
        var hwnd = new HWND(WindowNative.GetWindowHandle(window));
        var dpi = PInvoke.GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var width = (int)Math.Round(clientWidthDip * scale);
        var height = (int)Math.Round(clientHeightDip * scale);
        window.AppWindow.ResizeClient(new SizeInt32(width, height));

        var outer = window.AppWindow.Size;
        if (outer.Height > work.Height)
        {
            // Small screens: shrink to fit; the content scrolls.
            window.AppWindow.Resize(new SizeInt32(outer.Width, work.Height));
            outer = window.AppWindow.Size;
        }

        window.AppWindow.Move(new PointInt32(
            work.X + Math.Max(0, (work.Width - outer.Width) / 2),
            work.Y + Math.Max(0, (work.Height - outer.Height) / 2)));
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
