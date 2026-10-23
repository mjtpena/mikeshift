using MikeShift.Core.Actions;

namespace MikeShift.Core.Interaction;

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

/// <summary>Which edges of the monitor the pointer is beyond the work area on (it is over a taskbar / app bar).</summary>
[Flags]
public enum TaskbarEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>Where the wheel window goes (PLAN §3.1 item 8): centred on the cursor, kept inside the monitor's work area; anchored beside a taskbar.</summary>
public static class WheelPlacement
{
    /// <summary>How far (logical px) the window extends past the pointer toward a taskbar, so the pointer stays inside the window.</summary>
    public const int TaskbarAnchorGap = 4;

    /// <summary>Which taskbar edges the cursor is over: the cursor is inside <paramref name="monitorBounds"/> but beyond <paramref name="workArea"/> on that side.</summary>
    public static TaskbarEdges EdgesOver(PointPx cursor, ScreenRect workArea, ScreenRect monitorBounds)
    {
        if (!monitorBounds.Contains(cursor) || workArea.Contains(cursor)) return TaskbarEdges.None;
        var edges = TaskbarEdges.None;
        if (cursor.X < workArea.Left) edges |= TaskbarEdges.Left;
        if (cursor.X >= workArea.Right) edges |= TaskbarEdges.Right;
        if (cursor.Y < workArea.Top) edges |= TaskbarEdges.Top;
        if (cursor.Y >= workArea.Bottom) edges |= TaskbarEdges.Bottom;
        return edges;
    }

    /// <summary>
    /// A square window of <paramref name="size"/> physical px centred on <paramref name="cursor"/>, shifted just enough to lie inside
    /// <paramref name="workArea"/>. If the work area is smaller than the window the window is aligned to its top-left corner.
    /// Works with negative coordinates (monitors left of / above the primary).
    /// <para>
    /// If the cursor is over a taskbar (inside <paramref name="monitorBounds"/> but beyond the work area on some edge), centring would put half
    /// the opaque disc under the taskbar, which the shell keeps on top. Instead, on that axis the window is anchored so its far edge is
    /// <paramref name="anchorGapPx"/> past the pointer: the pointer is still inside the window (a window that does not contain the cursor never
    /// receives the OLE <c>DragEnter</c>, so there would be no wheel), the 40 px transparent margin is the only part that overlaps the taskbar
    /// and the disc sits beside it, above a bottom taskbar, below a top one, right of a left one and left of a right one. The other axis is
    /// centred and clamped to the work area as usual.
    /// </para>
    /// </summary>
    public static ScreenRect ForCursor(PointPx cursor, int size, ScreenRect workArea, ScreenRect? monitorBounds = null, int anchorGapPx = TaskbarAnchorGap)
    {
        var bounds = monitorBounds ?? workArea;
        var edges = EdgesOver(cursor, workArea, bounds);
        var gap = Math.Max(1, anchorGapPx);

        int left, top;
        if ((edges & TaskbarEdges.Left) != 0) left = Clamp(cursor.X - gap, bounds.Left, bounds.Right - size);
        else if ((edges & TaskbarEdges.Right) != 0) left = Clamp(cursor.X + gap - size, bounds.Left, bounds.Right - size);
        else left = Clamp(cursor.X - size / 2, workArea.Left, workArea.Right - size);

        if ((edges & TaskbarEdges.Top) != 0) top = Clamp(cursor.Y - gap, bounds.Top, bounds.Bottom - size);
        else if ((edges & TaskbarEdges.Bottom) != 0) top = Clamp(cursor.Y + gap - size, bounds.Top, bounds.Bottom - size);
        else top = Clamp(cursor.Y - size / 2, workArea.Top, workArea.Bottom - size);

        return new ScreenRect(left, top, left + size, top + size);
    }

    /// <summary>Shifts <paramref name="value"/> into <c>[min, max]</c>; if the range is empty (area smaller than the window) the minimum wins (top-left alignment).</summary>
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

    /// <summary>A square window of <paramref name="size"/> physical px centred in <paramref name="area"/> (click mode: the monitor's centre).</summary>
    public static ScreenRect CentredIn(ScreenRect area, int size)
    {
        var c = area.Centre;
        var left = c.X - size / 2;
        var top = c.Y - size / 2;
        return new ScreenRect(left, top, left + size, top + size);
    }

    /// <summary>The same centre, a different square size.</summary>
    public static ScreenRect Resized(ScreenRect window, int newSize)
    {
        var c = window.Centre;
        var left = c.X - newSize / 2;
        var top = c.Y - newSize / 2;
        return new ScreenRect(left, top, left + newSize, top + newSize);
    }
}

/// <summary>The two strings of a shell drop description (<c>DROPDESCRIPTION</c>): <c>szMessage</c> (with an optional <c>%1</c>) and <c>szInsert</c>.</summary>
public readonly record struct DropDescriptionText(string Message, string Insert)
{
    /// <summary>"Convert to %1" + "PNG" for a format tile; the bare title for a tool tile.</summary>
    public static DropDescriptionText For(ActionDef action) =>
        action.Page == WheelPage.Format
            ? new DropDescriptionText("Convert to %1", action.Title)
            : new DropDescriptionText(action.Title, "");

    /// <summary>The "can't drop here" description for a drag that carries virtual files (<c>DROPIMAGE_NONE</c>).</summary>
    public static DropDescriptionText SaveFirst { get; } = new(WheelText.SaveFirst, "");

    /// <summary>The text with the insert substituted (what the user reads), equal to <see cref="ActionDef.DropDescription"/>.</summary>
    public string Rendered => Message.Replace("%1", Insert, StringComparison.Ordinal);
}
