using System.Diagnostics;
using System.Text;

namespace MikeShift.Cli.Tests;

/// <summary>Result of one <c>MikeShift.Cli.exe</c> invocation (the exe behind the <c>mikeshift</c> command).</summary>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Non-empty stdout lines (the printed output paths for <c>--run</c>).</summary>
    public string[] StdoutLines => Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// Launches the real CLI executable as a child process. <c>MikeShift.Cli.exe</c> is copied next to this test assembly
/// by the project reference to MikeShift.Cli. Reusable by the converter packages for their end-to-end tests:
/// <code>var r = CliProcess.Run("--run", "PNG", input); Assert.Equal(0, r.ExitCode);</code>
/// </summary>
public static class CliProcess
{
    /// <summary>
    /// The CLI under test. Normally the build output next to this assembly; <c>MIKESHIFT_CLI_EXE</c> points the whole suite at another
    /// <c>MikeShift.Cli.exe</c> instead, for example the one in an unpacked release package (the trimmed, self-contained one that users run).
    /// </summary>
    public static string ExePath { get; } = ResolveExePath();

    private static string ResolveExePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("MIKESHIFT_CLI_EXE");
        if (string.IsNullOrWhiteSpace(overridePath)) return Path.Combine(AppContext.BaseDirectory, "MikeShift.Cli.exe");
        var full = Path.GetFullPath(overridePath);
        if (!File.Exists(full)) throw new FileNotFoundException("MIKESHIFT_CLI_EXE does not point to an existing file.", full);
        return full;
    }

    public static CliResult Run(params string[] args) => Run(TimeSpan.FromMinutes(2), args);

    /// <summary>
    /// Runs the CLI with the dev switch <c>MIKESHIFT_CAPS</c> set to <paramref name="caps"/> (for example <c>ai-upscale</c> or <c>no-ai-upscale</c>) and
    /// <c>--dev</c> in front, which a trimmed Release CLI needs before it honours the switch (a Debug one does not, and ignores the extra flag).
    /// </summary>
    public static CliResult RunWithCaps(string caps, params string[] args) =>
        Run(TimeSpan.FromMinutes(2), new Dictionary<string, string> { ["MIKESHIFT_CAPS"] = caps }, ["--dev", .. args]);

    public static CliResult Run(TimeSpan timeout, params string[] args) => Run(timeout, null, args);

    private static CliResult Run(TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string[] args)
    {
        if (!File.Exists(ExePath)) throw new FileNotFoundException("MikeShift.Cli.exe was not copied to the test output.", ExePath);

        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (environment is not null)
            foreach (var (name, value) in environment) psi.Environment[name] = value;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start MikeShift.Cli.exe");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"mikeshift {string.Join(' ', args)} did not finish within {timeout}.");
        }
        return new CliResult(p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
