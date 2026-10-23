namespace MikeShift.Core.Hosting;

/// <summary>
/// The pause a loop takes after an iteration threw, so a persistent failure cannot spin a core: 50 ms, doubling on each consecutive failure up to
/// 1 s, back to the start after <see cref="Reset"/> (a good iteration).
/// </summary>
public sealed class FailureBackoff
{
    /// <summary>First delay.</summary>
    public static readonly TimeSpan Initial = TimeSpan.FromMilliseconds(50);

    /// <summary>Longest delay.</summary>
    public static readonly TimeSpan Max = TimeSpan.FromSeconds(1);

    private TimeSpan _next = Initial;

    /// <summary>How long to wait after this failure; the following one waits twice as long (capped).</summary>
    public TimeSpan NextDelay()
    {
        var delay = _next;
        _next = TimeSpan.FromTicks(Math.Min(Max.Ticks, _next.Ticks * 2));
        return delay;
    }

    /// <summary>An iteration succeeded: the next failure starts again at <see cref="Initial"/>.</summary>
    public void Reset() => _next = Initial;
}
