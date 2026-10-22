using System.Diagnostics;
using System.Text;

namespace Shiftvert.App.Wheel;

/// <summary>
/// A tiny file logger for the drag/wheel layer: <c>%LOCALAPPDATA%\Shiftvert\log.txt</c> (override with the <c>SHIFTVERT_LOG</c>
/// environment variable, which the dev harness uses). <see cref="Error"/> and <see cref="Info"/> always write; <see cref="Stage"/>
/// lines (one per interaction stage, for asserting from tests) are written in Debug builds or when <c>SHIFTVERT_TRACE=1</c>.
/// Never throws: logging must not be able to kill the hook or wheel thread. No file names or user content beyond what the caller passes.
/// </summary>
internal static class WheelLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();
    private static readonly string FilePath = ResolvePath();
    private static readonly bool TraceOn = ResolveTrace();
    private static bool _trimChecked;

    /// <summary>The log file's path.</summary>
    public static string Path => FilePath;

    /// <summary>True if <see cref="Stage"/> lines are written.</summary>
    public static bool StagesEnabled => TraceOn;

    public static void Info(string message) => Write("info ", message);

    public static void Stage(string message)
    {
        if (TraceOn) Write("stage", message);
    }

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!_trimChecked)
                {
                    _trimChecked = true;
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                }

                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception)
        {
            // Logging is best effort.
        }
    }

    private static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("SHIFTVERT_LOG");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiftvert", "log.txt");
    }

    private static bool ResolveTrace()
    {
        if (Environment.GetEnvironmentVariable("SHIFTVERT_TRACE") is { } v) return v is "1" or "true";
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Milliseconds since process start, for relative timing in stage lines.</summary>
    public static long Ms => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
}
