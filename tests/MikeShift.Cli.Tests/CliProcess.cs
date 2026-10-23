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
    public static string ExePath { get; } = Path.Combine(AppContext.BaseDirectory, "MikeShift.Cli.exe");

    public static CliResult Run(params string[] args) => Run(TimeSpan.FromMinutes(2), args);

    public static CliResult Run(TimeSpan timeout, params string[] args)
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
