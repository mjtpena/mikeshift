using System.Globalization;
using MikeShift.App.Settings;
using MikeShift.Core.Hosting;

namespace MikeShift.App;

/// <summary>
/// Every developer and test switch of the tray app, in one place. Environment variables (<c>MIKESHIFT_*</c>) and dev command-line flags are read
/// only through this class, and only when <see cref="Enabled"/>: a Debug build, or an unpackaged Release build started with <c>--dev</c>
/// (<see cref="DevSwitchPolicy"/>). A packaged Release build ignores all of them, so nothing outside the user's own gesture can change
/// how the wheel treats input or where it keeps its files. <c>--smoke-test</c> is not a dev switch (CI uses it) and is read directly.
/// The switches are listed in docs/ARCHITECTURE.md ("Dev switches").
/// </summary>
internal static class DevSwitches
{
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    /// <summary>True if the dev switches are honoured by this process.</summary>
    public static bool Enabled { get; } = DevSwitchPolicy.IsEnabled(DebugBuild, PackageInfo.IsPackaged, Environment.GetCommandLineArgs());

    /// <summary>The command line (executable first) when dev switches are on; otherwise only the executable, so no dev flag can be found in it.</summary>
    public static string[] Args { get; } = Enabled ? Environment.GetCommandLineArgs() : Environment.GetCommandLineArgs().Take(1).ToArray();

    /// <summary>The value after a dev flag such as <c>--scene</c>, or null (also null when dev switches are off).</summary>
    public static string? ValueAfter(string flag) => DevSwitchPolicy.ValueAfter(Args, flag);

    /// <summary>True if the dev flag is on the command line (false when dev switches are off).</summary>
    public static bool HasFlag(string flag) => Args.Contains(flag, StringComparer.Ordinal);

    /// <summary>The environment variable's value, or null when dev switches are off or the variable is unset.</summary>
    public static string? Env(string name) => Enabled ? Environment.GetEnvironmentVariable(name) : null;

    private static bool EnvIs(string name, string value) => Env(name) == value;

    // ---- environment switches ----

    /// <summary><c>MIKESHIFT_ACCEPT_INJECTED=1</c>: input synthesised by <c>SendInput</c> counts as real (the drag harness drives the wheel this way).</summary>
    public static bool AcceptInjected => EnvIs("MIKESHIFT_ACCEPT_INJECTED", "1");

    /// <summary><c>MIKESHIFT_NO_HOOKS=1</c>: do not install the global input hooks.</summary>
    public static bool NoHooks => EnvIs("MIKESHIFT_NO_HOOKS", "1");

    /// <summary><c>MIKESHIFT_NO_SINGLE_INSTANCE=1</c>: skip the single-instance redirect (dev scenes next to a real instance).</summary>
    public static bool NoSingleInstance => EnvIs("MIKESHIFT_NO_SINGLE_INSTANCE", "1");

    /// <summary><c>MIKESHIFT_SETTINGS_PATH</c>: another settings file.</summary>
    public static string? SettingsPath => Env("MIKESHIFT_SETTINGS_PATH");

    /// <summary><c>MIKESHIFT_LOG</c>: another log file.</summary>
    public static string? LogPath => Env("MIKESHIFT_LOG") is { } p && !string.IsNullOrWhiteSpace(p) ? p : null;

    /// <summary><c>MIKESHIFT_TRACE=1|true|0</c>: interaction stage lines in the log (null = unset, so Debug builds default to on).</summary>
    public static bool? Trace => Env("MIKESHIFT_TRACE") is { } v ? v is "1" or "true" : null;

    /// <summary><c>MIKESHIFT_PICKER_FOLDER</c>: the Convert files… dialog starts here.</summary>
    public static string? PickerFolder => Env("MIKESHIFT_PICKER_FOLDER");

    /// <summary><c>MIKESHIFT_HINTS_FILE</c>: another file for the wheel-hint counter.</summary>
    public static string? HintsFile => Env("MIKESHIFT_HINTS_FILE");

    /// <summary><c>MIKESHIFT_THEME=light|dark|hc</c> (lower case): forces the theme; <c>hc</c> only affects the wheel.</summary>
    public static string? Theme => Env("MIKESHIFT_THEME")?.ToLowerInvariant();

    /// <summary><c>MIKESHIFT_ANIMATIONS=0</c>: behave as if the system's animation effects were off.</summary>
    public static bool AnimationsOff => EnvIs("MIKESHIFT_ANIMATIONS", "0");

    /// <summary><c>MIKESHIFT_DPI_SCALE=1.5</c> (0.75 to 4): every monitor behaves as if it ran at that scale.</summary>
    public static double? DpiScale =>
        TryParseDouble(Env("MIKESHIFT_DPI_SCALE")) is { } v && v is >= 0.75 and <= 4 ? v : null;

    /// <summary><c>MIKESHIFT_TEXT_SCALE=1.4</c>: pretend the OS text size is this.</summary>
    public static double? TextScale => TryParseDouble(Env("MIKESHIFT_TEXT_SCALE"));

    /// <summary><c>MIKESHIFT_CAPS=no-heif,no-hevc,no-av1,no-webp</c>: pretend those Windows extensions are missing (HEIC tile, welcome codec card).</summary>
    public static string? Caps => Env("MIKESHIFT_CAPS");

    /// <summary><c>MIKESHIFT_TRIM_MS</c>: how long the wheel idles before releasing its graphics device (default 15 s).</summary>
    public static long? TrimMs => long.TryParse(Env("MIKESHIFT_TRIM_MS"), out var v) && v >= 0 ? v : null;

    private static double? TryParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
