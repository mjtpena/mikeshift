using MikeShift.Core;
using MikeShift.Core.Hosting;

namespace MikeShift.Cli;

/// <summary>
/// The command line's one developer switch: <c>MIKESHIFT_CAPS</c> (see <see cref="CapabilityOverrides"/>), so the capability-gated tiles can be
/// listed and run on a PC that lacks the feature, for example Upscale 2x on a PC that is not a Copilot+ PC. Honoured under the same rule as
/// the tray app's switches (<see cref="DevSwitchPolicy"/>): always in a Debug build, in an unpackaged Release build only with <c>--dev</c>, never from the
/// installed package, where <c>--dev</c> is an unknown flag like any other. Nothing else the app reads from the environment is read here.
/// </summary>
internal static class DevSwitches
{
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    /// <summary>Applies <c>MIKESHIFT_CAPS</c> when dev switches are honoured (and clears any override otherwise); returns the arguments without <c>--dev</c> when it was honoured.</summary>
    public static string[] Apply(string[] args)
    {
        var enabled = DevSwitchPolicy.IsEnabled(DebugBuild, IsPackaged(), args);
        CapabilityOverrides.Apply(enabled ? Environment.GetEnvironmentVariable("MIKESHIFT_CAPS") : null);
        return enabled ? args.Where(a => !string.Equals(a, DevSwitchPolicy.DevFlag, StringComparison.Ordinal)).ToArray() : args;
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
