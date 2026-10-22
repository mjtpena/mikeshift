using Shiftvert.Core.Interaction;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Shiftvert.App.Input;

/// <summary>
/// Polled input state straight from the system (<c>GetAsyncKeyState</c>): the safety net for a missed button-up (feed
/// <see cref="DragSession.PollButtonState"/>) and the real modifier state at mouse-down (<see cref="DragSessionOptions.QueryKeyState"/>).
/// </summary>
public static class AsyncInput
{
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

    /// <summary>
    /// True while the user's primary (logical left) mouse button is physically down. <c>GetAsyncKeyState</c> reports the
    /// <em>physical</em> buttons, so with swapped buttons (<c>SM_SWAPBUTTON</c>) the logical left button is <c>VK_RBUTTON</c>.
    /// </summary>
    public static bool IsLeftButtonDown()
    {
        var swapped = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        return IsDown(swapped ? VK_RBUTTON : VK_LBUTTON);
    }

    /// <summary>True if the given virtual key is physically down right now.</summary>
    public static bool IsDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The system's real Shift/Ctrl/Alt state as an OLE <c>grfKeyState</c>-style mask (<see cref="KeyStateFlags"/>).</summary>
    public static uint ReadKeyState()
    {
        uint mask = 0;
        if (IsDown(VirtualKeys.Shift)) mask |= KeyStateFlags.MK_SHIFT;
        if (IsDown(VirtualKeys.Control)) mask |= KeyStateFlags.MK_CONTROL;
        if (IsDown(VirtualKeys.Menu)) mask |= KeyStateFlags.MK_ALT;
        return mask;
    }

    /// <summary>The system drag thresholds (<c>SM_CXDRAG</c>, <c>SM_CYDRAG</c>) in physical pixels, for <see cref="DragSessionOptions"/>.</summary>
    public static (int X, int Y) DragThreshold() =>
        (Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG)),
         Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG)));

    /// <summary>
    /// Convenience: <see cref="DragSessionOptions"/> populated with the system drag thresholds and the live key-state probe.
    /// </summary>
    public static DragSessionOptions CreateSessionOptions(ToolsKey toolsKey = ToolsKey.Ctrl)
    {
        var (x, y) = DragThreshold();
        return new DragSessionOptions
        {
            DragThresholdX = x,
            DragThresholdY = y,
            ToolsKey = toolsKey,
            QueryKeyState = ReadKeyState,
        };
    }
}
