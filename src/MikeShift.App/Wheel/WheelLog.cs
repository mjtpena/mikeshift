using System.Diagnostics;
using System.Text;
using MikeShift.App.Settings;
using MikeShift.Core.Hosting;

namespace MikeShift.App.Wheel;

/// <summary>
/// A tiny file logger for the drag/wheel layer: <c>log.txt</c> in <see cref="AppData.Folder"/> (the real folder when packaged; a dev switch,
/// <c>MIKESHIFT_LOG</c>, points the dev harness elsewhere). <see cref="Error"/> and <see cref="Info"/> always write; <see cref="Stage"/>
/// lines (one per interaction stage, for asserting from tests) are written in Debug builds or when the dev switch <c>MIKESHIFT_TRACE=1</c> is on.
/// The file is cut to its newest half when it passes 1 MB (checked at the first write and then every <see cref="LogTrimmer.CheckEveryWrites"/> writes).
/// Never throws: logging must not be able to kill the hook or wheel thread. No file names or user content beyond what the caller passes.
/// </summary>
internal static class WheelLog
{
    private static readonly object Gate = new();
    private static readonly string FilePath = ResolvePath();
    private static readonly bool TraceOn = ResolveTrace();
    private static int _writesSinceTrimCheck = LogTrimmer.CheckEveryWrites; // the first write checks

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
                if (++_writesSinceTrimCheck >= LogTrimmer.CheckEveryWrites)
                {
                    _writesSinceTrimCheck = 0;
                    LogTrimmer.TrimIfLarge(FilePath);
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
        return DevSwitches.LogPath ?? AppData.FilePath("log.txt");
    }

    private static bool ResolveTrace()
    {
        if (DevSwitches.Trace is { } trace) return trace;
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Milliseconds since process start, for relative timing in stage lines.</summary>
    public static long Ms => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
}
