namespace MikeShift.Core.Hosting;

/// <summary>
/// What the tray app does with an exception nobody caught on the UI thread. A resident tray app should survive a failed menu action or a
/// window that would not open (the exception is logged and the app carries on), but not a corrupted process (out of memory, a damaged runtime)
/// and not a storm of the same failure many times a second, which would only turn into a frozen app instead of a crash.
/// </summary>
public sealed class UnhandledPolicy
{
    /// <summary>More than this many unhandled exceptions within <see cref="StormWindow"/> are treated as fatal.</summary>
    public const int StormLimit = 50;

    /// <summary>The window over which <see cref="StormLimit"/> is counted.</summary>
    public static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTime> _recent = new();

    /// <summary>True for exceptions that mean the process itself can no longer be trusted.</summary>
    public static bool IsFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OutOfMemoryException or StackOverflowException or InsufficientExecutionStackException
            or AccessViolationException;
    }

    /// <summary>True if the exception should be logged and swallowed (<c>e.Handled = true</c>); false to let it end the process.</summary>
    public bool ShouldHandle(Exception exception, DateTime utcNow)
    {
        if (IsFatal(exception)) return false;
        lock (_recent)
        {
            _recent.Enqueue(utcNow);
            while (_recent.Count > 0 && utcNow - _recent.Peek() > StormWindow) _recent.Dequeue();
            return _recent.Count <= StormLimit;
        }
    }
}
