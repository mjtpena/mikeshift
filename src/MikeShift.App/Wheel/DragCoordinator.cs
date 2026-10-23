using MikeShift.App.Input;
using MikeShift.App.Tray;
using MikeShift.Core.Interaction;

namespace MikeShift.App.Wheel;

/// <summary>
/// The wiring of the drag feature (PLAN §3.1): owns the low-level input monitor, the wheel thread (window, renderer, model, session, OLE
/// drop target) and the little reader that carries hook events from the monitor's channel to the session. Created once at start-up; the
/// "Convert Files…" click mode goes through <see cref="ShowClickMode"/>. Everything is best effort: a failure is logged to
/// <c>%LOCALAPPDATA%\MikeShift\log.txt</c> and the app keeps running (the hook thread and the wheel thread must never die).
/// </summary>
internal sealed class DragCoordinator : IDisposable
{
    private readonly WheelThread _thread;
    private readonly LowLevelInputMonitor? _monitor;
    private readonly Task? _reader;
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;

    private DragCoordinator(WheelThread thread, LowLevelInputMonitor? monitor)
    {
        _thread = thread;
        _monitor = monitor;
        if (monitor is not null) _reader = Task.Run(() => PumpEventsAsync(monitor, thread.Controller, _stop.Token));
    }

    /// <summary>True while the global hooks are installed (Shift-drag works); false if only click mode is available.</summary>
    public bool HooksActive => _monitor?.IsRunning == true;

    /// <summary>The wheel controller (tests, diagnostics).</summary>
    internal WheelController Controller => _thread.Controller;

    /// <summary>
    /// Starts the wheel thread and (unless <paramref name="installHooks"/> is false) the input hooks. Returns null if the wheel itself could not
    /// start; a failure to install the hooks only disables Shift-drag.
    /// </summary>
    public static DragCoordinator? Start(AppServices services, bool installHooks = true)
    {
        WheelThread? thread = null;
        try
        {
            thread = new WheelThread(t => new WheelController(services, t));
            thread.Start();
        }
        catch (Exception ex)
        {
            WheelLog.Error("the wheel could not be created; Shift-drag and the click-mode wheel are unavailable", ex);
            thread?.Dispose();
            return null;
        }

        LowLevelInputMonitor? monitor = null;
        if (installHooks)
        {
            try
            {
                monitor = new LowLevelInputMonitor();
                monitor.Start();
                monitor.HooksReinstalled += () => WheelLog.Info("input hooks re-installed by the watchdog");
                WheelLog.Info("input hooks installed");
                WheelLog.Stage("hooks installed");
            }
            catch (Exception ex)
            {
                WheelLog.Error("the low-level hooks could not be installed; Shift-drag is unavailable", ex);
                monitor?.Dispose();
                monitor = null;
            }
        }

        // A finished conversion may have left large buffers behind: give them back a moment later (the app is idle again by then).
        services.Jobs.JobFinished += (_, _) => MemoryTrim.CompactSoon();

        return new DragCoordinator(thread, monitor);
    }

    /// <summary>Opens the wheel in click mode for files picked with "Convert Files…". Safe to call from any thread.</summary>
    public void ShowClickMode(IReadOnlyList<string> paths)
    {
        try
        {
            _thread.Controller.ShowClickMode(paths);
        }
        catch (Exception ex)
        {
            WheelLog.Error("opening the click-mode wheel failed", ex);
        }
    }

    /// <summary>
    /// Dev/test switch: with <c>MIKESHIFT_ACCEPT_INJECTED=1</c> events synthesised by <c>SendInput</c> count as real input, so the dev harness can
    /// drive a drag. Never enabled in normal use (injected input is ignored by design: it is our own nudge, or another program's).
    /// </summary>
    private static readonly bool AcceptInjected = Environment.GetEnvironmentVariable("MIKESHIFT_ACCEPT_INJECTED") == "1";

    private static async Task PumpEventsAsync(LowLevelInputMonitor monitor, WheelController controller, CancellationToken ct)
    {
        var leftDown = false;
        var reader = monitor.Events;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var e))
                {
                    try
                    {
                        if (AcceptInjected && e.Injected) e = e with { Injected = false };
                        switch (e.Kind)
                        {
                            case InputKind.LButtonDown when !e.Injected:
                                leftDown = true;
                                break;
                            case InputKind.LButtonUp when !e.Injected:
                                leftDown = false;
                                break;
                            case InputKind.MouseMove or InputKind.MouseWheel when !leftDown:
                                continue; // the session only cares about moves and wheel ticks during a press
                        }
                        controller.Enqueue(e);
                    }
                    catch (Exception ex)
                    {
                        WheelLog.Error("forwarding an input event failed", ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            WheelLog.Error("the input reader stopped", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _monitor?.Dispose(); }
        catch (Exception ex) { WheelLog.Error("stopping the input monitor failed", ex); }
        try { _reader?.Wait(TimeSpan.FromSeconds(1)); }
        catch (Exception) { /* cancelled */ }
        _thread.Dispose();
        _stop.Dispose();
    }
}
