using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Shiftvert.App.Wheel;

/// <summary>
/// What the wheel looks like right now: light or dark (from <see cref="UISettings"/>' background colour), the system accent colour,
/// high contrast (system colours, opaque, no glass) and whether the OS wants animations. Read at every show so a theme change takes
/// effect on the next interaction. Dev/test knobs: <c>SHIFTVERT_THEME=light|dark|hc</c> overrides the theme (harness screenshots),
/// <c>SHIFTVERT_ANIMATIONS=0</c> behaves like "animation effects" off.
/// </summary>
internal sealed class WheelTheme
{
    public bool Dark { get; init; } = true;
    public bool HighContrast { get; init; }
    public bool AnimationsEnabled { get; init; } = true;
    public Color Accent { get; init; } = Color.FromArgb(255, 0, 120, 212);

    // High contrast system colours.
    public Color HcWindow { get; init; }
    public Color HcWindowText { get; init; }
    public Color HcHighlight { get; init; }
    public Color HcHighlightText { get; init; }

    private static UISettings? s_settings;

    /// <summary>Reads the current system state. Never throws; falls back to a dark default.</summary>
    public static WheelTheme Read()
    {
        var dark = true;
        var accent = Color.FromArgb(255, 0, 120, 212);
        try
        {
            s_settings ??= new UISettings();
            var bg = s_settings.GetColorValue(UIColorType.Background);
            dark = Luminance(bg) < 0.5;
            accent = s_settings.GetColorValue(UIColorType.Accent);
        }
        catch (Exception ex)
        {
            WheelLog.Error("UISettings unavailable", ex);
        }

        var themeOverride = Environment.GetEnvironmentVariable("SHIFTVERT_THEME")?.ToLowerInvariant();
        switch (themeOverride)
        {
            case "light": dark = false; break;
            case "dark": dark = true; break;
        }

        var highContrast = false;
        try { highContrast = new AccessibilitySettings().HighContrast; }
        catch (Exception) { /* keep false */ }
        if (themeOverride == "hc") highContrast = true; // dev/test: exercise the high-contrast drawing path with the current system colours

        return new WheelTheme
        {
            Dark = dark,
            Accent = accent,
            HighContrast = highContrast,
            AnimationsEnabled = Environment.GetEnvironmentVariable("SHIFTVERT_ANIMATIONS") == "0" ? false : ReadAnimationsEnabled(),
            HcWindow = highContrast ? SysColor(SYS_COLOR_INDEX.COLOR_WINDOW) : default,
            HcWindowText = highContrast ? SysColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT) : default,
            HcHighlight = highContrast ? SysColor(SYS_COLOR_INDEX.COLOR_HIGHLIGHT) : default,
            HcHighlightText = highContrast ? SysColor(SYS_COLOR_INDEX.COLOR_HIGHLIGHTTEXT) : default,
        };
    }

    /// <summary>The OS "Animation effects" switch (<c>SPI_GETCLIENTAREAANIMATION</c>; the same switch as <c>UISettings.AnimationsEnabled</c>).</summary>
    public static unsafe bool ReadAnimationsEnabled()
    {
        try
        {
            int enabled = 1;
            if (PInvoke.SystemParametersInfo((SYSTEM_PARAMETERS_INFO_ACTION)0x1042, 0, &enabled, 0)) return enabled != 0;
        }
        catch (Exception) { /* fall through */ }
        return true;
    }

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Color SysColor(SYS_COLOR_INDEX index)
    {
        var v = PInvoke.GetSysColor(index); // 0x00BBGGRR
        return Color.FromArgb(255, (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
    }

    /// <summary>Mixes <paramref name="color"/> toward <paramref name="target"/> by <paramref name="amount"/> (0..1), keeping alpha.</summary>
    public static Color Mix(Color color, Color target, double amount)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(color.A, M(color.R, target.R), M(color.G, target.G), M(color.B, target.B));
    }

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B);
}
