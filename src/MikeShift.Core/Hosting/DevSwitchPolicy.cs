namespace MikeShift.Core.Hosting;

/// <summary>
/// When the tray app honours its developer and test switches (environment variables such as <c>MIKESHIFT_ACCEPT_INJECTED</c> and flags such
/// as <c>--scene</c>; the list is in docs/ARCHITECTURE.md). Some of them weaken the product (a program's <c>SendInput</c> could drive the wheel),
/// so a Release build ignores them unless it is an unpackaged run that was started with an explicit <see cref="DevFlag"/>.
/// A Debug build honours them always, so ordinary development needs no extra flag. A packaged Release build never honours them.
/// </summary>
public static class DevSwitchPolicy
{
    /// <summary>The explicit opt-in flag for an unpackaged Release build.</summary>
    public const string DevFlag = "--dev";

    /// <summary>True if the dev switches are honoured for this build, packaging state and command line.</summary>
    public static bool IsEnabled(bool debugBuild, bool packaged, IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (debugBuild) return true;
        return !packaged && args.Any(a => string.Equals(a, DevFlag, StringComparison.Ordinal));
    }

    /// <summary>The argument after <paramref name="flag"/>, or null if the flag is absent or last.</summary>
    public static string? ValueAfter(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.Ordinal)) return args[i + 1];
        return null;
    }
}
