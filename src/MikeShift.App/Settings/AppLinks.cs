namespace MikeShift.App.Settings;

/// <summary>Where the About card and the tray menu send the browser. The app itself never goes online.</summary>
public static class AppLinks
{
    /// <summary>The project page.</summary>
    public const string Project = "https://github.com/mjtpena/mikeshift";

    /// <summary>Releases, for "Check for updates".</summary>
    public const string Releases = Project + "/releases";

    /// <summary>The NOTICE file: every third-party licence and the credit to the original Shiftvert.</summary>
    public const string Notice = Project + "/blob/main/NOTICE";

    /// <summary>The upstream macOS app MikeShift is a port of.</summary>
    public const string Shiftvert = "https://github.com/roiegold/shiftvert";
}
