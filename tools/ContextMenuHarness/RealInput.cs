using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ContextMenuHarness;

/// <summary>Real mouse and keyboard input (SendInput) and a few window helpers. Only used by the `menu` scenarios, in short supervised runs.</summary>
internal static class RealInput
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput mi; [FieldOffset(0)] public KeyInput ki; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public InputUnion u; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Pt p);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X, Y; }

    public const ushort VkShift = 0x10, VkEscape = 0x1B, VkReturn = 0x0D, VkF10 = 0x79, VkLeft = 0x25, VkRight = 0x27, VkDown = 0x28, VkUp = 0x26, VkControl = 0x11, VkApps = 0x5D;

    private static void Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());

    private static Input Mouse(uint flags, int x = 0, int y = 0) => new() { type = 0, u = new InputUnion { mi = new MouseInput { dx = x, dy = y, flags = flags } } };

    private static Input Key(ushort vk, bool up) => new() { type = 1, u = new InputUnion { ki = new KeyInput { vk = vk, flags = up ? 2u : 0u } } };

    public static void MoveTo(int x, int y)
    {
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77); // SM_X/YVIRTUALSCREEN
        var w = GetSystemMetrics(78); var h = GetSystemMetrics(79);      // SM_CX/CYVIRTUALSCREEN
        var nx = (int)((x - left) * 65535.0 / (w - 1));
        var ny = (int)((y - top) * 65535.0 / (h - 1));
        Send(Mouse(0x8000 | 0x4000 | 0x1, nx, ny)); // ABSOLUTE | VIRTUALDESK | MOVE
        Thread.Sleep(40);
    }

    /// <summary>Right button down and up. Returns the Stopwatch timestamp of the button-up (the moment Explorer starts building the menu).</summary>
    public static long RightClick(int x, int y)
    {
        MoveTo(x, y);
        Send(Mouse(0x8));
        Thread.Sleep(30);
        Send(Mouse(0x10));
        return Stopwatch.GetTimestamp();
    }

    public static void LeftClick(int x, int y)
    {
        MoveTo(x, y);
        Send(Mouse(0x2));
        Thread.Sleep(30);
        Send(Mouse(0x4));
    }

    public static void Press(ushort vk, params ushort[] modifiers)
    {
        foreach (var m in modifiers) Send(Key(m, false));
        Send(Key(vk, false));
        Thread.Sleep(20);
        Send(Key(vk, true));
        foreach (var m in modifiers.Reverse()) Send(Key(m, true));
    }

    /// <summary>Right-click with Shift held, which opens the classic (full) menu directly.</summary>
    public static long ShiftRightClick(int x, int y)
    {
        MoveTo(x, y);
        Send(Key(VkShift, false));
        Thread.Sleep(30);
        Send(Mouse(0x8));
        Thread.Sleep(30);
        Send(Mouse(0x10));
        var stamp = Stopwatch.GetTimestamp();
        Thread.Sleep(30);
        Send(Key(VkShift, true));
        return stamp;
    }

    public static double MsSince(long stamp) => (Stopwatch.GetTimestamp() - stamp) * 1000.0 / Stopwatch.Frequency;
}
