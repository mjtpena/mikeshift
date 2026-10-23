using MikeShift.Core.Actions;

namespace MikeShift.Core.Feedback;

/// <summary>
/// Bookkeeping for the App's job runner: ids, how many jobs are running, and what the last finished job produced
/// (<see cref="LastOutputs"/>/<see cref="LastFinishedAt"/> feed the Shift-click "exclude previously produced files" rule,
/// PLAN §5.3 item 3). Thread-safe; knows nothing about UI.
/// </summary>
public sealed class JobLedger
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<int> _running = [];
    private int _nextId;
    private IReadOnlyList<string> _lastOutputs = [];
    private DateTimeOffset? _lastFinishedAt;

    /// <summary>Creates a ledger; <paramref name="clock"/> is injectable for tests.</summary>
    public JobLedger(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Registers a new running job and returns its id (1, 2, 3, ...).</summary>
    public int Begin()
    {
        lock (_gate)
        {
            var id = ++_nextId;
            _running.Add(id);
            return id;
        }
    }

    /// <summary>Number of jobs currently running.</summary>
    public int RunningCount
    {
        get { lock (_gate) return _running.Count; }
    }

    /// <summary>Number of running jobs other than <paramref name="id"/>.</summary>
    public int OthersRunning(int id)
    {
        lock (_gate) return _running.Count - (_running.Contains(id) ? 1 : 0);
    }

    /// <summary>
    /// Marks a job finished. If it wrote anything, <see cref="LastOutputs"/> becomes its output paths and
    /// <see cref="LastFinishedAt"/> the current time; a job that wrote nothing leaves both unchanged.
    /// </summary>
    public void Complete(int id, ActionResult? result)
    {
        lock (_gate)
        {
            _running.Remove(id);
            if (result is { Outputs.Count: > 0 })
            {
                _lastOutputs = [.. result.Outputs.Select(o => o.Path)];
                _lastFinishedAt = _clock();
            }
        }
    }

    /// <summary>Output paths of the most recent job that wrote anything (files, or folders for pages/split).</summary>
    public IReadOnlyList<string> LastOutputs
    {
        get { lock (_gate) return _lastOutputs; }
    }

    /// <summary>When the most recent output-producing job finished; null if none has.</summary>
    public DateTimeOffset? LastFinishedAt
    {
        get { lock (_gate) return _lastFinishedAt; }
    }
}
