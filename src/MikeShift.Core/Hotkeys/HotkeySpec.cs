using System.Globalization;

namespace MikeShift.Core.Hotkeys;

/// <summary>The modifier keys of a global hotkey, with the values <c>RegisterHotKey</c> takes (<c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c>, <c>MOD_WIN</c>).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global keyboard shortcut: modifiers plus one key. Stored as text ("Ctrl+Alt+Space") in the settings file, parsed and shown with the same
/// words. "None" (or nothing) means no hotkey. Pure: the app turns it into a <c>RegisterHotKey</c> call.
/// </summary>
public readonly record struct HotkeySpec(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>The default: Ctrl+Alt+Shift+Space. See <see cref="HotkeyConflicts"/> for why not the shorter Ctrl+Shift+Space or Ctrl+Alt+Space.</summary>
    public static HotkeySpec Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x20);

    /// <summary>The text that means "no hotkey".</summary>
    public const string NoneText = "None";

    /// <summary>The key's name ("Space", "A", "F9") or null when <see cref="VirtualKey"/> is not one the settings can express.</summary>
    public string? KeyName => KeyNameOf(VirtualKey);

    /// <summary>"Ctrl+Alt+Space": modifiers in the order Ctrl, Alt, Shift, Win, then the key. Round-trips through <see cref="TryParse"/>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & HotkeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(KeyName ?? "0x" + VirtualKey.ToString("X2", CultureInfo.InvariantCulture));
        return string.Join('+', parts);
    }

    /// <summary>
    /// Parses "Ctrl+Alt+Space" (case-insensitive; "Control", "Windows" and "Esc"/"Escape" spellings; spaces around the plus signs are fine; modifier
    /// order does not matter). False for "", "None", a missing key, an unknown word, two keys, or a repeated modifier.
    /// </summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var word = raw.Trim();
            if (word.Length == 0) return false;
            var mod = ModifierOf(word);
            if (mod != HotkeyModifiers.None)
            {
                if ((mods & mod) != 0) return false;
                mods |= mod;
                continue;
            }
            if (key is not null || VirtualKeyOf(word) is not { } vk) return false;
            key = vk;
        }
        if (key is null) return false;
        spec = new HotkeySpec(mods, key.Value);
        return true;
    }

    /// <summary>The spec for a setting value: <see cref="Default"/> when the value is absent or unreadable, null for "None".</summary>
    public static HotkeySpec? FromSetting(string? value)
    {
        if (value is null) return Default;
        if (value.Trim().Equals(NoneText, StringComparison.OrdinalIgnoreCase) || value.Trim().Length == 0) return null;
        return TryParse(value, out var spec) ? spec : Default;
    }

    /// <summary>The setting text for <paramref name="spec"/>: its <see cref="ToString"/>, or "None".</summary>
    public static string ToSetting(HotkeySpec? spec) => spec?.ToString() ?? NoneText;

    /// <summary>True if <paramref name="vk"/> is a modifier key (Shift, Ctrl, Alt, Win, left/right variants): never a hotkey's main key.</summary>
    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private static HotkeyModifiers ModifierOf(string word) => word.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Ctrl,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static readonly (string Name, int Vk)[] s_namedKeys =
    [
        ("Space", 0x20), ("Tab", 0x09), ("Enter", 0x0D), ("Esc", 0x1B), ("Backspace", 0x08), ("Insert", 0x2D), ("Delete", 0x2E),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22), ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
        ("Pause", 0x13), ("PrintScreen", 0x2C), ("`", 0xC0), ("-", 0xBD), ("=", 0xBB), ("[", 0xDB), ("]", 0xDD), (";", 0xBA), ("'", 0xDE),
        (",", 0xBC), (".", 0xBE), ("/", 0xBF), ("\\", 0xDC),
    ];

    private static int? VirtualKeyOf(string word)
    {
        if (word.Length == 1)
        {
            var c = char.ToUpperInvariant(word[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (word.Length is 2 or 3 && (word[0] is 'F' or 'f') && int.TryParse(word.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        foreach (var (name, vk) in s_namedKeys)
            if (name.Equals(word, StringComparison.OrdinalIgnoreCase)) return vk;
        return word.ToLowerInvariant() switch
        {
            "escape" => 0x1B,
            "return" => 0x0D,
            "del" => 0x2E,
            "ins" => 0x2D,
            "pgup" => 0x21,
            "pgdn" => 0x22,
            _ => null,
        };
    }

    private static string? KeyNameOf(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var (name, key) in s_namedKeys)
            if (key == vk) return name;
        return null;
    }
}

/// <summary>How usable a hotkey is.</summary>
public enum HotkeyVerdict
{
    /// <summary>Fine.</summary>
    Ok,
    /// <summary>Allowed, but known to be used by Windows or common apps: the settings page warns.</summary>
    Warning,
    /// <summary>Not allowed: the settings page refuses it.</summary>
    Rejected,
}

/// <summary>The verdict for a hotkey and the sentence explaining it.</summary>
public readonly record struct HotkeyCheck(HotkeyVerdict Verdict, string? Message);

/// <summary>
/// Which shortcuts a global hotkey must not take over. A hotkey registered with <c>RegisterHotKey</c> is delivered to MikeShift <em>instead of</em>
/// the focused app, so a clash silently breaks that app's shortcut. The default is Ctrl+Alt+Shift+Space because the nearby candidates are all taken:
/// Ctrl+Alt+Space is the Claude desktop app's quick-entry shortcut (it was already registered on the development PC, Windows error 1409) and
/// OrbitDrop's; Ctrl+Shift+Space is Word's non-breaking space, Excel's "select the whole sheet" and VS Code's parameter hints; Ctrl+Alt plus a
/// letter is AltGr plus that letter on many European keyboards (it types a character); Alt+Space is the window menu and PowerToys Run; Ctrl+Space
/// switches Chinese, Japanese and Korean input methods; shortcuts with the Windows key are Windows' own. Three modifiers plus Space is free on
/// every PC checked and is hard to hit by accident.
/// </summary>
public static class HotkeyConflicts
{
    private static readonly Dictionary<HotkeySpec, string> s_known = BuildKnown();

    /// <summary>Every shortcut with a known meaning that the check warns about, for tests and the settings page.</summary>
    public static IReadOnlyDictionary<HotkeySpec, string> Known => s_known;

    /// <summary>Judges <paramref name="spec"/>.</summary>
    public static HotkeyCheck Check(HotkeySpec spec)
    {
        if (HotkeySpec.IsModifierKey(spec.VirtualKey) || spec.KeyName is null)
            return new HotkeyCheck(HotkeyVerdict.Rejected, "Choose a key other than Shift, Ctrl, Alt or Windows.");
        if ((spec.Modifiers & HotkeyModifiers.Win) != 0)
            return new HotkeyCheck(HotkeyVerdict.Rejected, "Windows keeps shortcuts with the Windows key for itself. Use Ctrl, Alt or Shift.");

        var isFunctionKey = spec.VirtualKey is >= 0x70 and <= 0x87;
        var hasCtrlOrAlt = (spec.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) != 0;
        if (!hasCtrlOrAlt && !isFunctionKey)
            return new HotkeyCheck(HotkeyVerdict.Rejected, "Add Ctrl or Alt, or the shortcut would stop you typing that key everywhere.");
        if (spec.Modifiers == HotkeyModifiers.None && spec.VirtualKey == 0x1B)
            return new HotkeyCheck(HotkeyVerdict.Rejected, "Esc is needed to cancel things in every program.");

        if (s_known.TryGetValue(spec, out var meaning))
            return new HotkeyCheck(HotkeyVerdict.Warning, $"{spec} is already used for {meaning}. MikeShift would take it from that program.");

        // Ctrl+Alt+letter or digit is AltGr+letter on many keyboard layouts (it types a character).
        if (spec.Modifiers == (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt) && spec.VirtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
            return new HotkeyCheck(HotkeyVerdict.Warning, $"{spec} types a character on keyboards where AltGr is Ctrl+Alt (many European layouts).");
        if (spec.Modifiers == HotkeyModifiers.Ctrl && spec.VirtualKey is >= 'A' and <= 'Z')
            return new HotkeyCheck(HotkeyVerdict.Warning, $"{spec} is a standard editing shortcut in most programs. Add Alt or Shift to be safe.");
        if (!hasCtrlOrAlt && isFunctionKey)
            return new HotkeyCheck(HotkeyVerdict.Warning, $"{spec} is used by many programs. Add Ctrl or Alt to be safe.");
        return new HotkeyCheck(HotkeyVerdict.Ok, null);
    }

    private static Dictionary<HotkeySpec, string> BuildKnown()
    {
        var d = new Dictionary<HotkeySpec, string>();
        void Add(HotkeyModifiers mods, int vk, string meaning) => d[new HotkeySpec(mods, vk)] = meaning;
        const HotkeyModifiers C = HotkeyModifiers.Ctrl, A = HotkeyModifiers.Alt, S = HotkeyModifiers.Shift;

        foreach (var (letter, meaning) in new[]
        {
            ('C', "Copy"), ('V', "Paste"), ('X', "Cut"), ('Z', "Undo"), ('Y', "Redo"), ('A', "Select all"), ('S', "Save"), ('P', "Print"),
            ('F', "Find"), ('N', "New"), ('O', "Open"), ('W', "Close tab or window"), ('T', "New tab"), ('R', "Refresh"), ('B', "Bold"),
            ('I', "Italic"), ('U', "Underline"), ('K', "Insert link"), ('L', "Focus the address bar"), ('D', "Bookmark or duplicate"),
            ('E', "Search"), ('H', "Replace"), ('G', "Go to"), ('J', "Downloads"), ('Q', "Quit"), ('M', "Minimise or mute"),
        })
            Add(C, letter, meaning);

        Add(A, 0x73, "closing the window (Alt+F4)");
        Add(A, 0x09, "switching windows (Alt+Tab)");
        Add(A, 0x1B, "cycling windows (Alt+Esc)");
        Add(A, 0x20, "the window menu and PowerToys Run (Alt+Space)");
        Add(C, 0x1B, "the Start menu (Ctrl+Esc)");
        Add(C | S, 0x1B, "Task Manager (Ctrl+Shift+Esc)");
        Add(C | A, 0x2E, "the security screen (Ctrl+Alt+Delete)");
        Add(C, 0x20, "switching input methods (Chinese, Japanese, Korean) and Word's 'clear formatting'");
        Add(S, 0x20, "switching between full-width and half-width characters in input methods");
        Add(C | A, 0x20, "the Claude desktop app's quick entry and OrbitDrop (Ctrl+Alt+Space)");
        Add(C | S, 0x20, "Word's non-breaking space, Excel's 'select the whole sheet' and VS Code's parameter hints");
        Add(C | S, 0x4E, "a new incognito or private window in browsers (Ctrl+Shift+N)");
        Add(C | S, 0x54, "reopening a closed tab (Ctrl+Shift+T)");
        Add(C | S, 0x53, "Save as (Ctrl+Shift+S)");
        Add(C | S, 0x50, "the command palette in many editors (Ctrl+Shift+P)");
        Add(A, 0x25, "going back (Alt+Left)");
        Add(A, 0x27, "going forward (Alt+Right)");
        Add(A, 0x0D, "properties (Alt+Enter)");
        Add(C | A, 0x09, "keeping the Alt+Tab view open (Ctrl+Alt+Tab)");
        return d;
    }
}

/// <summary>Turns what a File Explorer window reports as "selected" into the file paths MikeShift converts.</summary>
public static class SelectionPaths
{
    /// <summary>
    /// Keeps real file-system paths: fully qualified, not a shell namespace path ("::{GUID}", "shell:"), not blank; removes duplicates (the shell
    /// can report an item twice, e.g. in search results) while keeping the order, and drops paths <paramref name="exists"/> denies (a file deleted
    /// since it was selected). <paramref name="exists"/> null means "assume they exist" (tests).
    /// </summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string?> reported, Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in reported)
        {
            var path = raw?.Trim();
            if (string.IsNullOrEmpty(path)) continue;
            if (path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.IndexOf('\0') >= 0) continue;
            try
            {
                if (!Path.IsPathFullyQualified(path)) continue;
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (exists is not null && !exists(path)) continue;
            if (seen.Add(path)) result.Add(path);
        }
        return result;
    }

    /// <summary>The Explorer-window class names whose selection the hotkey reads: a folder window and the desktop (Progman, or WorkerW when a wallpaper slideshow runs).</summary>
    public static bool IsExplorerWindowClass(string? className) =>
        className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
}
