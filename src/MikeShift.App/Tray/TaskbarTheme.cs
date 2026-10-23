using Microsoft.Win32;
using MikeShift.App.Wheel;
using Windows.UI.ViewManagement;

namespace MikeShift.App.Tray;

/// <summary>
/// Whether the taskbar (and so the notification area) is light or dark right now. Windows 11 draws tray icons as-is on that surface, so a
/// monochrome icon has to be white on a dark taskbar and dark on a light one. The taskbar follows "Choose your default Windows mode"
/// (<c>SystemUsesLightTheme</c>), which can differ from the app mode that <see cref="UISettings"/> reports. Under high contrast the
/// system window colour decides. the dev switch <c>MIKESHIFT_THEME=light|dark</c> overrides it, like it does for the wheel.
/// </summary>
internal static class TaskbarTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when the taskbar is light. Never throws; defaults to dark (the Windows 11 default).</summary>
    public static bool IsLight()
    {
        switch (DevSwitches.Theme)
        {
            case "light": return true;
            case "dark": return false;
        }

        try
        {
            if (new AccessibilitySettings().HighContrast)
                return WheelTheme.Luminance(new UISettings().GetColorValue(UIColorType.Background)) >= 0.5;
        }
        catch (Exception) { /* fall through to the registry */ }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("SystemUsesLightTheme") is int value) return value != 0;
        }
        catch (Exception) { /* fall through */ }

        try
        {
            return WheelTheme.Luminance(new UISettings().GetColorValue(UIColorType.Background)) >= 0.5;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
