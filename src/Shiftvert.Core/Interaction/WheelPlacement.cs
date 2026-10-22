using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Interaction;

/// <summary>An axis-aligned rectangle in physical (device) pixels, virtual-screen coordinates; <see cref="Right"/>/<see cref="Bottom"/> are exclusive.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    /// <summary>The integer centre (rounded toward the top-left).</summary>
    public PointPx Centre => new(Left + Width / 2, Top + Height / 2);

    /// <summary>True if <paramref name="p"/> lies inside (right/bottom exclusive).</summary>
    public bool Contains(PointPx p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
}

/// <summary>Where the wheel window goes (PLAN §3.1 item 8): centred on the cursor, kept inside the monitor's work area.</summary>
public static class WheelPlacement
{
    /// <summary>
    /// A square window of <paramref name="size"/> physical px centred on <paramref name="cursor"/>, shifted just enough to lie inside
    /// <paramref name="workArea"/>. If the work area is smaller than the window the window is aligned to its top-left corner.
    /// Works with negative coordinates (monitors left of / above the primary).
    /// </summary>
    public static ScreenRect ForCursor(PointPx cursor, int size, ScreenRect workArea)
    {
        var left = cursor.X - size / 2;
        var top = cursor.Y - size / 2;
        left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - size));
        top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - size));
        return new ScreenRect(left, top, left + size, top + size);
    }

    /// <summary>A square window of <paramref name="size"/> physical px centred in <paramref name="area"/> (click mode: the monitor's centre).</summary>
    public static ScreenRect CentredIn(ScreenRect area, int size)
    {
        var c = area.Centre;
        var left = c.X - size / 2;
        var top = c.Y - size / 2;
        return new ScreenRect(left, top, left + size, top + size);
    }

    /// <summary>The same centre, a different square size (grow the window around the wheel for the collapse frame).</summary>
    public static ScreenRect Resized(ScreenRect window, int newSize)
    {
        var c = window.Centre;
        var left = c.X - newSize / 2;
        var top = c.Y - newSize / 2;
        return new ScreenRect(left, top, left + newSize, top + newSize);
    }

    /// <summary>
    /// Window edge in physical px for the collapse animation: the disc grows to <see cref="WheelTimings.CollapseScaleTo"/>, so the
    /// window has to grow with it (PLAN §3.2).
    /// </summary>
    public static int CollapseWindowSizePx(double dpiScale) =>
        (int)Math.Ceiling(WheelGeometry.WindowSize * WheelTimings.CollapseScaleTo * dpiScale);
}

/// <summary>The two strings of a shell drop description (<c>DROPDESCRIPTION</c>): <c>szMessage</c> (with an optional <c>%1</c>) and <c>szInsert</c>.</summary>
public readonly record struct DropDescriptionText(string Message, string Insert)
{
    /// <summary>"Convert to %1" + "PNG" for a format tile; the bare title for a tool tile.</summary>
    public static DropDescriptionText For(ActionDef action) =>
        action.Page == WheelPage.Format
            ? new DropDescriptionText("Convert to %1", action.Title)
            : new DropDescriptionText(action.Title, "");

    /// <summary>The text with the insert substituted (what the user reads), equal to <see cref="ActionDef.DropDescription"/>.</summary>
    public string Rendered => Message.Replace("%1", Insert, StringComparison.Ordinal);
}
