using MikeShift.Core.Interaction;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Wheel;

/// <summary>
/// What the wheel looks like right now: light or dark (from <see cref="UISettings"/>' background colour), the system accent colour, the
/// OS text size, high contrast (system colours: ButtonFace/ButtonText for tiles, Highlight/HighlightText for the selected tile, opaque, no glass)
/// and whether the OS wants animations. Read at every show so a change takes effect on the next interaction. Dev/test knobs:
/// <c>MIKESHIFT_THEME=light|dark|hc</c> overrides the theme (harness screenshots), <c>MIKESHIFT_ANIMATIONS=0</c> behaves like "animation
/// effects" off, <c>MIKESHIFT_TEXT_SCALE=1.4</c> pretends the OS text size is 140 %.
/// </summary>
internal sealed class WheelTheme
{
    public bool Dark { get; init; } = true;
    public bool HighContrast { get; init; }
    public bool AnimationsEnabled { get; init; } = true;

    /// <summary>The system accent colour as the user set it.</summary>
    public Color Accent { get; init; } = Color.FromArgb(255, 0, 120, 212);

    /// <summary>The accent as Windows 11 fills accent controls: <c>AccentLight2</c> in the dark theme, <c>AccentDark1</c> in the light one.</summary>
    public Color AccentFill { get; init; } = Color.FromArgb(255, 0, 120, 212);

    /// <summary><c>UISettings.TextScaleFactor</c> clamped to 1 .. 1.4 (<see cref="WheelTypography.ClampScale"/>).</summary>
    public double TextScale { get; init; } = 1;

    /// <summary>The Fluent colours of the solid parts, for the current theme.</summary>
    public WheelPalette Palette => WheelPalette.For(Dark);

    // High contrast system colours.
    public Color HcWindow { get; init; }
    public Color HcWindowText { get; init; }
    public Color HcButtonFace { get; init; }
    public Color HcButtonText { get; init; }
    public Color HcHighlight { get; init; }
    public Color HcHighlightText { get; init; }
    public Color HcGrayText { get; init; }

    private static UISettings? s_settings;

    /// <summary>Reads the current system state. Never throws; falls back to a dark default.</summary>
    public static WheelTheme Read()
    {
        var dark = true;
        var accent = Color.FromArgb(255, 0, 120, 212);
        var accentFill = accent;
        var textScale = 1.0;
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

        var themeOverride = Environment.GetEnvironmentVariable("MIKESHIFT_THEME")?.ToLowerInvariant();
        switch (themeOverride)
        {
            case "light": dark = false; break;
            case "dark": dark = true; break;
        }

        try
        {
            s_settings ??= new UISettings();
            accentFill = s_settings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
            textScale = s_settings.TextScaleFactor;
        }
        catch (Exception) { accentFill = accent; }
        if (double.TryParse(Environment.GetEnvironmentVariable("MIKESHIFT_TEXT_SCALE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var forcedScale))
            textScale = forcedScale;

        var highContrast = false;
        try { highContrast = new AccessibilitySettings().HighContrast; }
        catch (Exception) { /* keep false */ }
        if (themeOverride == "hc") highContrast = true; // dev/test: exercise the high-contrast drawing path with the current system colours

        var theme = new WheelTheme
        {
            Dark = dark,
            Accent = accent,
            AccentFill = accentFill,
            TextScale = WheelTypography.ClampScale(textScale),
            HighContrast = highContrast,
            AnimationsEnabled = Environment.GetEnvironmentVariable("MIKESHIFT_ANIMATIONS") == "0" ? false : ReadAnimationsEnabled(),
        };
        return highContrast ? theme.WithSystemColours() : theme;
    }

    /// <summary>This theme with the high-contrast colours taken from the current Windows colour scheme.</summary>
    public WheelTheme WithSystemColours() => new()
    {
        Dark = Dark,
        Accent = Accent,
        AccentFill = AccentFill,
        TextScale = TextScale,
        HighContrast = true,
        AnimationsEnabled = AnimationsEnabled,
        HcWindow = SysColor(SYS_COLOR_INDEX.COLOR_WINDOW),
        HcWindowText = SysColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT),
        HcButtonFace = SysColor(SYS_COLOR_INDEX.COLOR_BTNFACE),
        HcButtonText = SysColor(SYS_COLOR_INDEX.COLOR_BTNTEXT),
        HcHighlight = SysColor(SYS_COLOR_INDEX.COLOR_HIGHLIGHT),
        HcHighlightText = SysColor(SYS_COLOR_INDEX.COLOR_HIGHLIGHTTEXT),
        HcGrayText = SysColor(SYS_COLOR_INDEX.COLOR_GRAYTEXT),
    };

    /// <summary>A high-contrast theme with explicit colours (offscreen previews of schemes other than the one the PC is running).</summary>
    public static WheelTheme HighContrastScheme(Color window, Color windowText, Color buttonFace, Color buttonText, Color highlight, Color highlightText, Color grayText, bool dark) => new()
    {
        Dark = dark,
        HighContrast = true,
        HcWindow = window,
        HcWindowText = windowText,
        HcButtonFace = buttonFace,
        HcButtonText = buttonText,
        HcHighlight = highlight,
        HcHighlightText = highlightText,
        HcGrayText = grayText,
    };

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

    public static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    public static Color FromRgb(Rgb c, double alpha = 1) => Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), c.R, c.G, c.B);

    /// <summary>Mixes <paramref name="color"/> toward <paramref name="target"/> by <paramref name="amount"/> (0..1), keeping alpha.</summary>
    public static Color Mix(Color color, Color target, double amount)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(color.A, M(color.R, target.R), M(color.G, target.G), M(color.B, target.B));
    }

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B);
}
