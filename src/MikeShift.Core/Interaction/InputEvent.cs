namespace MikeShift.Core.Interaction;

/// <summary>A point in physical (device) pixels, virtual-screen coordinates, as delivered by the low-level hooks and OLE.</summary>
public readonly record struct PointPx(int X, int Y)
{
    /// <summary>Euclidean distance to <paramref name="other"/>.</summary>
    public double DistanceTo(PointPx other)
    {
        double dx = X - other.X, dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>What happened.</summary>
public enum InputKind : byte
{
    MouseMove,
    LButtonDown,
    LButtonUp,
    KeyDown,
    KeyUp,
    /// <summary>A vertical wheel notch (or fraction of one on hi-res wheels / precision touchpads); see <see cref="InputEvent.Delta"/>.</summary>
    MouseWheel,
}

/// <summary>
/// One input event as copied out of a low-level hook callback. A small immutable value so the hook can enqueue it
/// without allocating.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Point">Pointer position in physical pixels (mouse events only; 0,0 for key events).</param>
/// <param name="Vk">Virtual-key code for key events (left/right variants such as <see cref="VirtualKeys.LShift"/> are preserved); 0 otherwise.</param>
/// <param name="Delta">Wheel delta for <see cref="InputKind.MouseWheel"/> (120 per notch, positive = away from the user); 0 otherwise.</param>
/// <param name="Injected">True when synthesised by software (<c>LLMHF_INJECTED</c>/<c>LLKHF_INJECTED</c>), e.g. our own nudge.</param>
/// <param name="TimestampMs">Monotonic milliseconds (<see cref="Environment.TickCount64"/> base) at which the monitor saw the event.</param>
public readonly record struct InputEvent(
    InputKind Kind,
    PointPx Point = default,
    int Vk = 0,
    int Delta = 0,
    bool Injected = false,
    long TimestampMs = 0)
{
    public static InputEvent MouseMove(int x, int y, long t = 0, bool injected = false) =>
        new(InputKind.MouseMove, new PointPx(x, y), Injected: injected, TimestampMs: t);

    public static InputEvent ButtonDown(int x, int y, long t = 0, bool injected = false) =>
        new(InputKind.LButtonDown, new PointPx(x, y), Injected: injected, TimestampMs: t);

    public static InputEvent ButtonUp(int x, int y, long t = 0, bool injected = false) =>
        new(InputKind.LButtonUp, new PointPx(x, y), Injected: injected, TimestampMs: t);

    public static InputEvent KeyDown(int vk, long t = 0, bool injected = false) =>
        new(InputKind.KeyDown, Vk: vk, Injected: injected, TimestampMs: t);

    public static InputEvent KeyUp(int vk, long t = 0, bool injected = false) =>
        new(InputKind.KeyUp, Vk: vk, Injected: injected, TimestampMs: t);

    public static InputEvent Wheel(int delta, int x = 0, int y = 0, long t = 0, bool injected = false) =>
        new(InputKind.MouseWheel, new PointPx(x, y), Delta: delta, Injected: injected, TimestampMs: t);
}

/// <summary>The virtual-key codes the interaction layer cares about (<c>winuser.h</c>).</summary>
public static class VirtualKeys
{
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Escape = 0x1B;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
}
