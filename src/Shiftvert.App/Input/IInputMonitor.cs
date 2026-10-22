using System.Threading.Channels;
using Shiftvert.Core.Interaction;

namespace Shiftvert.App.Input;

/// <summary>
/// A source of global (system-wide) mouse and modifier-key events for the drag detector. Implementations must deliver events in
/// order through <see cref="Events"/> without doing any slow work on the thread that observes them. The low-level-hook implementation is
/// <see cref="LowLevelInputMonitor"/>; a Raw Input (<c>RIDEV_INPUTSINK</c>) implementation can replace it without touching consumers.
/// <para>
/// Only mouse movement/left-button/vertical-wheel events and the modifier keys (Shift, Ctrl, Alt) plus Esc are reported; no other key is ever copied.
/// A monitor is single-use: <see cref="Start"/> once, <see cref="Stop"/> (or dispose) once; the channel completes on stop.
/// </para>
/// </summary>
public interface IInputMonitor : IDisposable
{
    /// <summary>The ordered event stream. Completes when the monitor stops.</summary>
    ChannelReader<InputEvent> Events { get; }

    /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>Installs the hooks and begins delivering events. Throws <see cref="InvalidOperationException"/> if installation fails.</summary>
    void Start();

    /// <summary>Removes the hooks, ends the monitor thread and completes <see cref="Events"/>. Idempotent.</summary>
    void Stop();
}
