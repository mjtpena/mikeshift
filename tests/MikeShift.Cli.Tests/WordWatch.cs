using System.Diagnostics;

namespace MikeShift.Cli.Tests;

/// <summary>
/// Runs the CLI while taking snapshots of the machine's WINWORD.EXE processes, so a test can say "this run never started Word". Word is started
/// through COM (by the system, not as a child of the CLI), so the only honest evidence is the process list: every Word that appears while the
/// CLI is running is recorded (under <see cref="MikeShift.Core.Tests.WordLock"/>, so a Word test of the other assembly cannot be mistaken for it). Word is also quit again by the converter, so looking afterwards would prove nothing.
/// </summary>
internal static class WordWatch
{
    public sealed record Observation(CliResult Result, IReadOnlyCollection<int> NewWordProcesses);

    public static HashSet<int> Pids() =>
        Process.GetProcessesByName("WINWORD").Select(p => { using (p) return p.Id; }).ToHashSet();

    public static async Task<Observation> RunAsync(TimeSpan timeout, params string[] args)
    {
        using var wordLock = new MikeShift.Core.Tests.WordLock(); // no other test (in either assembly) may start Word meanwhile
        var before = Pids();
        var seen = new HashSet<int>();
        var run = Task.Run(() => CliProcess.Run(timeout, args));
        while (!run.IsCompleted)
        {
            seen.UnionWith(Pids().Except(before));
            await Task.WhenAny(run, Task.Delay(25));
        }
        var result = await run;
        seen.UnionWith(Pids().Except(before));
        return new Observation(result, seen);
    }
}
