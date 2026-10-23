using System.Runtime.InteropServices;

namespace MikeShift.Cli;

/// <summary>
/// Ends the process without running native DLL teardown. By the time the CLI has printed its result, the process may hold
/// Media Foundation, WIC and D3D/DXGI state (the capability probe encodes a HEIF picture, which loads the GPU encoder and driver).
/// Letting the CRT and loader tear all that down at exit is not safe: on machines with an Intel/NVIDIA hybrid GPU,
/// dxgi.dll fail-fasts (exception 0x87A) inside the driver's exit-time clean-up (seen at exit of the test host), and the caller would see a crashed child instead of
/// the exit code. Everything the CLI promises (output files, stdout, stderr) is already written and flushed at that point.
/// </summary>
internal static partial class FastExit
{
    [LibraryImport("kernel32.dll", EntryPoint = "TerminateProcess", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    /// <summary>The pseudo handle for "this process" (what GetCurrentProcess returns).</summary>
    private const nint CurrentProcess = -1;

    /// <summary>Flushes <paramref name="writers"/>, then ends the process with <paramref name="exitCode"/>. Returns only if that failed.</summary>
    public static void Now(int exitCode, params TextWriter[] writers)
    {
        foreach (var w in writers)
        {
            try { w.Flush(); }
            catch (Exception) { /* a closed pipe must not turn into a crash */ }
        }
        TerminateProcess(CurrentProcess, unchecked((uint)exitCode));
    }
}
