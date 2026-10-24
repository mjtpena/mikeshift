using System.IO.Pipes;
using MikeShift.App.Tray;
using MikeShift.App.Wheel;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Shell;

namespace MikeShift.App.Shell;

/// <summary>
/// The tray app's side of the Explorer right-click entry. It does two things and nothing else:
/// <list type="bullet">
/// <item>listens on a per-user local named pipe (current user only) for requests from the extension process (<see cref="ShellExtensionServer"/>) and
/// runs them exactly as the wheel would: "Run" resolves the action by title against the exact catalog for the paths and enqueues it on the
/// <c>JobRunner</c> (same toast, naming, originals untouched); "Wheel" opens the click-mode wheel;</item>
/// <item>keeps the capability cache file up to date, so the extension process can offer the right formats without probing codecs itself.</item>
/// </list>
/// Every field of a request is validated, and a failure is logged and ignored: a bad message never reaches a converter.
/// </summary>
internal sealed class ShellBridge : IDisposable
{
    private readonly AppServices _services;
    private readonly Action<IReadOnlyList<string>> _showWheel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private ShellBridge(AppServices services, Action<IReadOnlyList<string>> showWheel)
    {
        _services = services;
        _showWheel = showWheel;
        _loop = Task.Run(() => ListenAsync(_stop.Token));
        _ = Task.Run(WriteCapabilityCacheAsync);
        services.Preferences.Changed += key =>
        {
            if (key == Settings.Preferences.UseWordKey) _ = Task.Run(WriteCapabilityCacheAsync);
        };
    }

    /// <summary>Starts listening. <paramref name="showWheel"/> is called on the UI thread with the paths of "More options…".</summary>
    public static ShellBridge? Start(AppServices services, Action<IReadOnlyList<string>> showWheel)
    {
        try
        {
            return new ShellBridge(services, showWheel);
        }
        catch (Exception ex)
        {
            WheelLog.Error("the Explorer right-click bridge could not start", ex);
            return null;
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var name = ShellNames.PipeName();
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => HandleAsync(connected, ct), ct);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                WheelLog.Error("Explorer bridge: accepting a connection failed", ex);
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                // A peer that connects and then says nothing must not hold the pipe forever.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var abort = timeout.Token.Register(() => pipe.Dispose());

                ShellRequest request;
                try { request = ShellRequest.ReadFrom(pipe); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ObjectDisposedException)
                {
                    WheelLog.Info("Explorer bridge: rejected a malformed request (" + ex.Message + ")");
                    return;
                }

                var ok = Validate(request);
                pipe.WriteByte((byte)(ok ? ShellReply.Ok : ShellReply.Rejected));
                await pipe.FlushAsync(ct).ConfigureAwait(false);
                if (ok) Dispatch(request);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WheelLog.Error("Explorer bridge: handling a request failed", ex);
            }
        }
    }

    private static bool Validate(ShellRequest request)
    {
        if (request.Verb == ShellVerb.Ping) return true;
        if (request.Paths.Count == 0) return false;
        foreach (var path in request.Paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || !Path.IsPathFullyQualified(path)) return false;
        }
        return request.Verb != ShellVerb.Run || !string.IsNullOrWhiteSpace(request.ActionTitle);
    }

    private void Dispatch(ShellRequest request)
    {
        switch (request.Verb)
        {
            case ShellVerb.Wheel:
                WheelLog.Info($"Explorer bridge: More options… for {request.Paths.Count} item(s)");
                _services.Dispatcher.TryEnqueue(() => Guarded("opening the wheel", () => _showWheel(request.Paths)));
                break;

            case ShellVerb.Run:
                WheelLog.Info($"Explorer bridge: {request.ActionTitle} on {request.Paths.Count} item(s)");
                _ = Task.Run(() => RunAction(request.ActionTitle, request.Paths));
                break;
        }
    }

    private void RunAction(string title, IReadOnlyList<string> paths)
    {
        try
        {
            // The same exact catalog the wheel builds (waits for the codec probe, which is why this is not on the UI thread).
            var actions = ActionCatalog.For(paths, _services.Capabilities);
            var action = ActionCatalog.Find(actions, title);
            _services.Dispatcher.TryEnqueue(() => Guarded("starting the job", () =>
            {
                if (action is not null)
                {
                    _services.Jobs.Enqueue(action, paths, _services.Interaction);
                    return;
                }
                // The menu was built from what Explorer shows an extension for a big selection (its first 16 items), so the whole
                // selection can turn out to be mixed or the codec missing. Say what IS possible instead of failing: the wheel offers it.
                WheelLog.Info($"Explorer bridge: {title} is not offered for these {paths.Count} item(s); opening the wheel instead");
                _showWheel(paths);
            }));
        }
        catch (Exception ex)
        {
            WheelLog.Error("Explorer bridge: resolving the action failed", ex);
        }
    }

    private static void Guarded(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { WheelLog.Error("Explorer bridge: " + what + " failed", ex); }
    }

    private async Task WriteCapabilityCacheAsync()
    {
        try
        {
            var caps = await Capabilities.CurrentAsync.ConfigureAwait(false);
            var folder = ShellNames.DataFolder();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, CapabilityCache.FileName);
            var temp = path + "." + Environment.ProcessId + ".tmp";
            await File.WriteAllTextAsync(temp, CapabilityCache.Serialize(caps)).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            WheelLog.Error("Explorer bridge: writing the capability cache failed", ex);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(1)); }
        catch (Exception) { /* cancelled */ }
        _stop.Dispose();
    }
}
