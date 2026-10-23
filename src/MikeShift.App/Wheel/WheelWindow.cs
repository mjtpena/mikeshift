using System.Drawing;
using System.Runtime.InteropServices;
using MikeShift.Core.Interaction;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Wheel;

/// <summary>Where the pointer is relative to monitors: the work area and scale of the monitor under a point.</summary>
internal readonly record struct MonitorGeometry(ScreenRect Bounds, ScreenRect WorkArea, double DpiScale);

/// <summary>
/// The wheel's Win32 window (PLAN §3.1 item 3, §3.2): a layered popup that is created once, hidden, on the wheel thread and reused
/// for every interaction. <c>WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED</c>; <b>never</b> <c>WS_EX_TRANSPARENT</b>
/// (it would make the window invisible to OLE hit-testing). Its pixels come from <see cref="Present"/> (premultiplied BGRA) through
/// <c>UpdateLayeredWindow</c>; the caller keeps every pixel at alpha ≥ 1 while shown so the whole square is a hit target.
/// All members are for the wheel thread only.
/// </summary>
internal sealed unsafe class WheelWindow : IDisposable
{
    public const string ClassName = "MikeShiftWheel";

    // Window messages.
    private const uint WM_DESTROY = 0x0002, WM_ACTIVATE = 0x0006, WM_ERASEBKGND = 0x0014, WM_MOUSEACTIVATE = 0x0021,
        WM_GETOBJECT = 0x003D, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201,
        WM_RBUTTONDOWN = 0x0204, WM_MOUSELEAVE = 0x02A3, WM_SETCURSOR = 0x0020, WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_APP_INVOKE = 0x8000 + 10;

    private const uint MA_ACTIVATE = 1, MA_NOACTIVATE = 3;
    private const uint WA_INACTIVE = 0;

    private static WNDPROC? s_wndProc; // rooted for the life of the process
    private static bool s_classRegistered;

    private HWND _hwnd;
    private HDC _memDc;
    private HBITMAP _dib;
    private HGDIOBJ _oldBitmap;
    private void* _dibBits;
    private int _dibWidth, _dibHeight;
    private bool _activatable;
    private bool _trackingMouse;
    private bool _disposed;

    /// <summary>The window handle (valid after <see cref="Create"/>).</summary>
    public HWND Hwnd => _hwnd;

    /// <summary>The window is currently shown.</summary>
    public bool IsShown { get; private set; }

    /// <summary>The window rectangle last given to <see cref="Present"/> (screen, physical px).</summary>
    public ScreenRect Rect { get; private set; }

    // Events raised from the window procedure (wheel thread).
    public event Action<PointPx>? MouseMoved;      // screen coordinates
    public event Action<PointPx>? MouseDown;       // screen coordinates
    public event Action? MouseLeft;
    public event Action<int>? KeyDown;             // virtual key
    public event Action? Deactivated;
    public event Action? InvokeRequested;          // WM_APP_INVOKE
    public event Action? DisplayChanged;

    /// <summary>Answers <c>WM_GETOBJECT</c> (UI Automation); return null to let the default handle it.</summary>
    public Func<WPARAM, LPARAM, LRESULT?>? GetObjectHandler { get; set; }

    public void Create()
    {
        RegisterClass();
        var ex = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST;
        fixed (char* cls = ClassName)
        fixed (char* title = "MikeShift wheel")
        {
            _hwnd = PInvoke.CreateWindowEx(ex, new PCWSTR(cls), new PCWSTR(title), WINDOW_STYLE.WS_POPUP, 0, 0, 1, 1, HWND.Null, HMENU.Null, HINSTANCE.Null, null);
        }
        if (_hwnd.IsNull) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for the wheel window");
        _activatable = false;
    }

    private static void RegisterClass()
    {
        if (s_classRegistered) return;
        s_wndProc = StaticWndProc;
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = s_wndProc,
                hInstance = new HINSTANCE(System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, new PCWSTR((char*)32512)),
                lpszClassName = new PCWSTR(cls),
            };
            if (PInvoke.RegisterClassEx(in wc) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed for the wheel window");
        }
        s_classRegistered = true;
    }

    private static WheelWindow? s_instance; // exactly one wheel window exists per process

    private static LRESULT StaticWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        var self = s_instance;
        if (self is null || self._hwnd != hwnd) return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
        try
        {
            return self.WndProc(msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            WheelLog.Error($"wndproc 0x{msg:X}", ex);
            return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    /// <summary>Makes this the instance the static window procedure forwards to. Call right after <see cref="Create"/>.</summary>
    public void Attach() => s_instance = this;

    private LRESULT WndProc(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return new LRESULT(1);

            case WM_MOUSEACTIVATE:
                return new LRESULT((nint)(_activatable ? MA_ACTIVATE : MA_NOACTIVATE));

            case WM_ACTIVATE:
                if ((wParam.Value & 0xFFFF) == WA_INACTIVE && _activatable) Deactivated?.Invoke();
                return new LRESULT(0);

            case WM_SETCURSOR:
                return PInvoke.DefWindowProc(_hwnd, msg, wParam, lParam);

            case WM_MOUSEMOVE:
            {
                EnsureMouseTracking();
                MouseMoved?.Invoke(ClientToScreenPoint(lParam));
                return new LRESULT(0);
            }

            case WM_MOUSELEAVE:
                _trackingMouse = false;
                MouseLeft?.Invoke();
                return new LRESULT(0);

            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
                MouseDown?.Invoke(ClientToScreenPoint(lParam));
                return new LRESULT(0);

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
                KeyDown?.Invoke((int)wParam.Value);
                return new LRESULT(0);

            case WM_GETOBJECT:
                if (GetObjectHandler?.Invoke(wParam, lParam) is { } handled) return handled;
                break;

            case WM_APP_INVOKE:
                InvokeRequested?.Invoke();
                return new LRESULT(0);

            case WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                break;
        }
        return PInvoke.DefWindowProc(_hwnd, msg, wParam, lParam);
    }

    private PointPx ClientToScreenPoint(LPARAM lParam)
    {
        var p = new Point((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF));
        PInvoke.ClientToScreen(_hwnd, ref p);
        return new PointPx(p.X, p.Y);
    }

    private void EnsureMouseTracking()
    {
        if (_trackingMouse) return;
        var tme = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
            hwndTrack = _hwnd,
        };
        if (PInvoke.TrackMouseEvent(ref tme)) _trackingMouse = true;
    }

    /// <summary>Posts <c>WM_APP_INVOKE</c> (callable from any thread) so the wheel thread drains its work queue.</summary>
    public void PostInvoke() => PInvoke.PostMessage(_hwnd, WM_APP_INVOKE, default, default);

    // ------------------------------------------------------------------ monitors

    /// <summary>Dev switch (<see cref="DevSwitches.DpiScale"/>, <c>MIKESHIFT_DPI_SCALE=1.5</c>): the wheel behaves as if every monitor ran at that scale (to exercise 125-200 % layouts on a 100 % machine).</summary>
    private static readonly double? DpiOverride = DevSwitches.DpiScale;

    /// <summary>The monitor containing <paramref name="point"/> (nearest if outside every monitor): bounds, work area and DPI scale (1.0 = 96 dpi).</summary>
    public static MonitorGeometry MonitorAt(PointPx point)
    {
        var hmon = PInvoke.MonitorFromPoint(new Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        ScreenRect bounds, work;
        if (PInvoke.GetMonitorInfo(hmon, ref info))
        {
            bounds = new ScreenRect(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
            work = new ScreenRect(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        }
        else
        {
            bounds = work = new ScreenRect(0, 0, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN));
        }

        double scale = 1.0;
        if (PInvoke.GetDpiForMonitor(hmon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded && dpiX > 0)
            scale = dpiX / 96.0;
        if (DpiOverride is { } forced) scale = forced; // dev/test only
        return new MonitorGeometry(bounds, work, scale);
    }

    // ------------------------------------------------------------------ presenting

    /// <summary>
    /// Replaces the window's contents and geometry in one <c>UpdateLayeredWindow(ULW_ALPHA)</c> call.
    /// <paramref name="bgra"/> is a top-down premultiplied BGRA bitmap of <paramref name="width"/>×<paramref name="height"/>.
    /// </summary>
    public void Present(ReadOnlySpan<byte> bgra, int width, int height, ScreenRect rect)
    {
        EnsureDib(width, height);
        bgra[..(width * height * 4)].CopyTo(new Span<byte>(_dibBits, width * height * 4));

        var dst = new Point(rect.Left, rect.Top);
        var size = new SIZE(width, height);
        var src = new Point(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        if (!PInvoke.UpdateLayeredWindow(_hwnd, HDC.Null, &dst, &size, _memDc, &src, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
            WheelLog.Error("UpdateLayeredWindow failed, win32=" + Marshal.GetLastWin32Error());
        Rect = rect;
    }

    private void EnsureDib(int width, int height)
    {
        if (_dib != HBITMAP.Null && _dibWidth == width && _dibHeight == height) return;
        ReleaseDib();

        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            if (_memDc.IsNull) _memDc = PInvoke.CreateCompatibleDC(screen);
            var bi = new BITMAPINFO();
            bi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = -height; // top-down
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = 0; // BI_RGB
            void* bits;
            _dib = PInvoke.CreateDIBSection(screen, &bi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (_dib.IsNull) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection failed");
            _dibBits = bits;
            _oldBitmap = PInvoke.SelectObject(_memDc, _dib);
            _dibWidth = width;
            _dibHeight = height;
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    private void ReleaseDib()
    {
        if (!_memDc.IsNull && !_oldBitmap.IsNull) PInvoke.SelectObject(_memDc, _oldBitmap);
        if (!_dib.IsNull) PInvoke.DeleteObject(_dib);
        _dib = default;
        _oldBitmap = default;
        _dibBits = null;
        _dibWidth = _dibHeight = 0;
    }

    // ------------------------------------------------------------------ visibility

    /// <summary>Switches between drag mode (never activates) and click mode (activatable, takes keyboard focus).</summary>
    public void SetActivatable(bool activatable)
    {
        if (_activatable == activatable) return;
        _activatable = activatable;
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style = activatable ? style & ~WINDOW_EX_STYLE.WS_EX_NOACTIVATE : style | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)style);
    }

    /// <summary>Shows the window topmost without stealing focus (<c>SW_SHOWNOACTIVATE</c> semantics).</summary>
    public void ShowNoActivate()
    {
        PInvoke.SetWindowPos(_hwnd, new HWND(-1), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        IsShown = true;
    }

    /// <summary>Shows the window and takes the foreground (click mode).</summary>
    public void ShowActivated()
    {
        PInvoke.SetWindowPos(_hwnd, new HWND(-1), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        IsShown = true;
        TakeForeground();
    }

    /// <summary>Best-effort foreground activation, with the attach-thread-input workaround when plain <c>SetForegroundWindow</c> is refused.</summary>
    public void TakeForeground()
    {
        if (PInvoke.SetForegroundWindow(_hwnd)) { PInvoke.SetFocus(_hwnd); return; }

        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground, null);
        var ourThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread != 0 && foregroundThread != ourThread && PInvoke.AttachThreadInput(ourThread, foregroundThread, true))
        {
            PInvoke.SetForegroundWindow(_hwnd);
            PInvoke.SetFocus(_hwnd);
            PInvoke.AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    /// <summary>True if the window is the foreground window.</summary>
    public bool IsForeground => PInvoke.GetForegroundWindow() == _hwnd;

    public void Hide()
    {
        if (!_hwnd.IsNull) PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        IsShown = false;
        _trackingMouse = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_hwnd.IsNull) PInvoke.DestroyWindow(_hwnd);
        _hwnd = default;
        ReleaseDib();
        if (!_memDc.IsNull) PInvoke.DeleteDC(_memDc);
        _memDc = default;
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }
}
