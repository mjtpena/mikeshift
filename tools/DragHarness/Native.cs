using System.Runtime.InteropServices;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DragHarness;

[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropSource
{
    [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, uint grfKeyState);
    [PreserveSig] int GiveFeedback(uint dwEffect);
}

/// <summary>A plain drop source like Explorer's: continue while the left button is down, cancel on Esc, drop on release.</summary>
[ComVisible(true)]
internal sealed class DropSource : IDropSource
{
    public int QueryContinueDrag(bool fEscapePressed, uint grfKeyState)
    {
        if (fEscapePressed) return Native.DRAGDROP_S_CANCEL;
        if ((grfKeyState & 1) == 0) return Native.DRAGDROP_S_DROP;
        return 0;
    }

    public int GiveFeedback(uint dwEffect) => Native.DRAGDROP_S_USEDEFAULTCURSORS;
}

internal static class Native
{
    public const int DRAGDROP_S_DROP = 0x40100, DRAGDROP_S_CANCEL = 0x40101, DRAGDROP_S_USEDEFAULTCURSORS = 0x40102;
    public const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;

    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_ESCAPE = 0x1B, VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2;

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    public static extern int DoDragDrop(ComIDataObject dataObject, IDropSource dropSource, uint okEffects, out uint effect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHCreateDataObject(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, IntPtr pdtInner, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    public static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? className, string? title);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 1, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_WHEEL = 0x800,
        MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint KEYEVENTF_KEYUP = 2;

    [DllImport("shell32.dll")]
    public static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    public static extern IntPtr ILClone(IntPtr pidl);

    [DllImport("shell32.dll")]
    public static extern bool ILRemoveLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    /// <summary>
    /// A shell data object for <paramref name="paths"/> (all in one folder), the kind Explorer hands to DoDragDrop: it advertises
    /// CFSTR_SHELLIDLIST and produces CF_HDROP on request.
    /// </summary>
    public static ComIDataObject CreateShellDataObject(IReadOnlyList<string> paths)
    {
        var folder = IntPtr.Zero;
        var absolute = new IntPtr[paths.Count];
        var children = new IntPtr[paths.Count];
        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var hr = SHParseDisplayName(paths[i], IntPtr.Zero, out absolute[i], 0, out _);
                if (hr != 0) throw new InvalidOperationException($"SHParseDisplayName({paths[i]}) failed: 0x{hr:X8}");
                children[i] = ILFindLastID(absolute[i]);
                if (folder == IntPtr.Zero)
                {
                    folder = ILClone(absolute[i]);
                    ILRemoveLastID(folder);
                }
            }
            var iid = IID_IDataObject;
            var hr2 = SHCreateDataObject(folder, (uint)children.Length, children, IntPtr.Zero, ref iid, out var ppv);
            if (hr2 != 0) throw new InvalidOperationException($"SHCreateDataObject failed: 0x{hr2:X8}");
            try { return (ComIDataObject)Marshal.GetObjectForIUnknown(ppv); }
            finally { Marshal.Release(ppv); }
        }
        finally
        {
            foreach (var p in absolute) if (p != IntPtr.Zero) CoTaskMemFree(p);
            if (folder != IntPtr.Zero) ILFree(folder);
        }
    }
}

/// <summary>Synthetic mouse and keyboard input (the same API the wheel's nudge uses; the app's hooks see it as injected, see SHIFTVERT_ACCEPT_INJECTED).</summary>
internal static class Pointer
{
    public static void Send(params Native.INPUT[] inputs) =>
        Native.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());

    private static Native.INPUT Mouse(uint flags, int x = 0, int y = 0) => new()
    {
        type = Native.INPUT_MOUSE,
        u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dx = x, dy = y, dwFlags = flags } },
    };

    private static Native.INPUT Key(int vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 } },
    };

    public static (int X, int Y) Position
    {
        get
        {
            Native.GetCursorPos(out var p);
            return (p.X, p.Y);
        }
    }

    /// <summary>Moves the pointer to a virtual-screen pixel (absolute SendInput move: no pointer ballistics).</summary>
    public static void MoveTo(int x, int y)
    {
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        int vx = Native.GetSystemMetrics(SM_XVIRTUALSCREEN), vy = Native.GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Native.GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = Native.GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var nx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        var ny = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
        Send(Mouse(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, nx, ny));
    }

    /// <summary>Glides to (x, y) in small steps so OLE and the hooks see a realistic stream of moves.</summary>
    public static void Glide(int x, int y, int stepPx = 6, int stepMs = 8)
    {
        var (sx, sy) = Position;
        var dist = Math.Sqrt((x - sx) * (double)(x - sx) + (y - sy) * (double)(y - sy));
        var steps = Math.Max(1, (int)(dist / stepPx));
        for (var i = 1; i <= steps; i++)
        {
            MoveTo((int)Math.Round(sx + (x - sx) * (double)i / steps), (int)Math.Round(sy + (y - sy) * (double)i / steps));
            Thread.Sleep(stepMs);
        }
        MoveTo(x, y);
    }

    public static void LeftDown() => Send(Mouse(Native.MOUSEEVENTF_LEFTDOWN));
    public static void LeftUp() => Send(Mouse(Native.MOUSEEVENTF_LEFTUP));
    public static void Wheel(int delta) => Send(new Native.INPUT
    {
        type = Native.INPUT_MOUSE,
        u = new Native.InputUnion { mi = new Native.MOUSEINPUT { mouseData = (uint)delta, dwFlags = Native.MOUSEEVENTF_WHEEL } },
    });

    public static void KeyDown(int vk) => Send(Key(vk, up: false));
    public static void KeyUp(int vk) => Send(Key(vk, up: true));
}
