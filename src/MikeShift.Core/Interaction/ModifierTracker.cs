namespace MikeShift.Core.Interaction;

/// <summary>The modifier keys the drag interaction reacts to.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>OLE <c>grfKeyState</c> bits (<c>MK_*</c>) as passed to <c>IDropTarget.DragEnter/DragOver</c>.</summary>
public static class KeyStateFlags
{
    public const uint MK_LBUTTON = 0x1;
    public const uint MK_RBUTTON = 0x2;
    public const uint MK_SHIFT = 0x4;
    public const uint MK_CONTROL = 0x8;
    public const uint MK_MBUTTON = 0x10;
    public const uint MK_ALT = 0x20;

    /// <summary>The <see cref="Modifiers"/> encoded in <paramref name="grfKeyState"/>.</summary>
    public static Modifiers ToModifiers(uint grfKeyState)
    {
        var m = Modifiers.None;
        if ((grfKeyState & MK_SHIFT) != 0) m |= Modifiers.Shift;
        if ((grfKeyState & MK_CONTROL) != 0) m |= Modifiers.Ctrl;
        if ((grfKeyState & MK_ALT) != 0) m |= Modifiers.Alt;
        return m;
    }
}

/// <summary>
/// Tracks Shift/Ctrl/Alt from low-level key events and tells the caller about <em>fresh presses</em> (a key going from up
/// to down; auto-repeat of an already-down key is ignored). It can also be reconciled with an OLE <c>grfKeyState</c> mask,
/// which covers sources the keyboard hook cannot see (elevated windows) and keys that were already held before we started
/// listening.
/// <para>
/// Reconciliation rule: a modifier that the mask reports down while we last <em>knew</em> it to be up (from a key event
/// or an earlier reconcile) is a fresh press: it went up→down between observations. A modifier we had no knowledge of yet
/// (never seen) or already believed down is merely caught up on, and never reported as fresh.
/// </para>
/// Not thread-safe; use from one consumer thread.
/// </summary>
public sealed class ModifierTracker
{
    private const int SyntheticBit = 8; // "down" learnt from a reconcile, not from a concrete key

    // Per modifier (shift, ctrl, alt): which keys are down (bit0 generic VK, bit1 left, bit2 right, bit3 synthetic), and
    // whether we have ever observed the modifier's state.
    private readonly int[] _down = new int[3];
    private readonly bool[] _known = new bool[3];

    /// <summary>The modifiers currently believed down.</summary>
    public Modifiers Current
    {
        get
        {
            var m = Modifiers.None;
            if (_down[0] != 0) m |= Modifiers.Shift;
            if (_down[1] != 0) m |= Modifiers.Ctrl;
            if (_down[2] != 0) m |= Modifiers.Alt;
            return m;
        }
    }

    public bool Shift => _down[0] != 0;
    public bool Ctrl => _down[1] != 0;
    public bool Alt => _down[2] != 0;

    /// <summary>Forgets everything (state becomes "unknown, none down").</summary>
    public void Reset()
    {
        Array.Clear(_down);
        Array.Clear(_known);
    }

    /// <summary>
    /// Applies a key event. Returns the modifiers that were <em>freshly pressed</em> by it (empty for non-modifier keys,
    /// key-ups, auto-repeat, and a second physical key of an already-held modifier).
    /// </summary>
    public Modifiers OnKey(int vk, bool down)
    {
        if (!TryClassify(vk, out var index, out var bit)) return Modifiers.None;
        var before = _down[index] != 0;
        if (down) _down[index] = (_down[index] | bit) & ~SyntheticBit;
        else _down[index] &= ~(bit | SyntheticBit);
        _known[index] = true;
        return down && !before ? ToFlag(index) : Modifiers.None;
    }

    /// <summary>Applies an <see cref="InputEvent"/> if it is a key event; other kinds are ignored.</summary>
    public Modifiers OnEvent(in InputEvent e) => e.Kind switch
    {
        InputKind.KeyDown => OnKey(e.Vk, true),
        InputKind.KeyUp => OnKey(e.Vk, false),
        _ => Modifiers.None,
    };

    /// <summary>Reconciles with an OLE <c>grfKeyState</c> mask (<c>MK_SHIFT</c>, <c>MK_CONTROL</c>, <c>MK_ALT</c>). Returns fresh presses per the class rule.</summary>
    public Modifiers Reconcile(uint grfKeyState) => ReconcileModifiers(KeyStateFlags.ToModifiers(grfKeyState));

    /// <summary>Reconciles with an explicit modifier set. See the class remarks for what counts as fresh.</summary>
    public Modifiers ReconcileModifiers(Modifiers actual)
    {
        var fresh = Modifiers.None;
        for (var i = 0; i < 3; i++)
        {
            var flag = ToFlag(i);
            var isDown = (actual & flag) != 0;
            var wasDown = _down[i] != 0;
            if (isDown)
            {
                if (!wasDown)
                {
                    if (_known[i]) fresh |= flag; // observed up, now down: the hook missed the press
                    _down[i] = SyntheticBit;
                }
            }
            else
            {
                _down[i] = 0;
            }
            _known[i] = true;
        }
        return fresh;
    }

    private static Modifiers ToFlag(int index) => index switch
    {
        0 => Modifiers.Shift,
        1 => Modifiers.Ctrl,
        _ => Modifiers.Alt,
    };

    private static bool TryClassify(int vk, out int index, out int bit)
    {
        switch (vk)
        {
            case VirtualKeys.Shift: index = 0; bit = 1; return true;
            case VirtualKeys.LShift: index = 0; bit = 2; return true;
            case VirtualKeys.RShift: index = 0; bit = 4; return true;
            case VirtualKeys.Control: index = 1; bit = 1; return true;
            case VirtualKeys.LControl: index = 1; bit = 2; return true;
            case VirtualKeys.RControl: index = 1; bit = 4; return true;
            case VirtualKeys.Menu: index = 2; bit = 1; return true;
            case VirtualKeys.LMenu: index = 2; bit = 2; return true;
            case VirtualKeys.RMenu: index = 2; bit = 4; return true;
            default: index = 0; bit = 0; return false;
        }
    }
}
