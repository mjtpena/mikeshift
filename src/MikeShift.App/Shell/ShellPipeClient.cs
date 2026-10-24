using System.IO.Pipes;
using System.Runtime.InteropServices.Marshalling;
using MikeShift.Core.Shell;

namespace MikeShift.App.Shell;

/// <summary>
/// The extension process's side of the hand-over: connect to the tray app's per-user local pipe, send the request, wait for the one-byte
/// answer. If the tray app is not running (the user exited it) it is started through Windows' app activation, which is what clicking the
/// Start menu entry does, and the send is retried while it comes up. Local named pipe only; nothing here touches the network.
/// </summary>
internal static class ShellPipeClient
{
    private static readonly TimeSpan FirstTry = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(25);

    /// <summary>Delivers <paramref name="request"/> (blocking; the caller runs this off Explorer's thread).</summary>
    public static void Send(ShellRequest request)
    {
        // Two tries before starting anything: between two requests the tray app has a moment with no free pipe instance, and activating the app
        // while it runs would pop its tray menu.
        if (TrySend(request, FirstTry)) return;
        Thread.Sleep(300);
        if (TrySend(request, FirstTry)) return;

        ShellLog.Info("tray app not reachable; starting it");
        StartTrayApp();
        var deadline = DateTime.UtcNow + StartupWait;
        while (DateTime.UtcNow < deadline)
        {
            if (TrySend(request, TimeSpan.FromMilliseconds(500))) return;
            Thread.Sleep(250);
        }
        ShellLog.Error("the tray app did not come up in time; the request was dropped");
    }

    private static bool TrySend(ShellRequest request, TimeSpan connectTimeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ShellNames.PipeName(), PipeDirection.InOut, PipeOptions.None);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            request.WriteTo(pipe);
            pipe.Flush();
            var reply = pipe.ReadByte();
            return reply == (int)ShellReply.Ok;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static unsafe void StartTrayApp()
    {
        try
        {
            var family = ShellNames.PackageFamilyName();
            if (family is null)
            {
                ShellLog.Info("not packaged: the tray app cannot be started by activation");
                return;
            }
            var clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"); // CLSID_ApplicationActivationManager
            var iid = typeof(IApplicationActivationManager).GUID;
            var hr = Native.CoCreateInstance(in clsid, 0, Native.ClsctxLocalServer | Native.ClsctxInprocServer, in iid, out var raw);
            if (hr != Hr.Ok || raw == 0)
            {
                ShellLog.Error($"ApplicationActivationManager unavailable (0x{hr:X8})");
                return;
            }
            IApplicationActivationManager manager;
            try { manager = ComInterfaceMarshaller<IApplicationActivationManager>.ConvertToManaged((void*)raw)!; }
            finally { ComInterfaceMarshaller<IApplicationActivationManager>.Free((void*)raw); }
            hr = manager.ActivateApplication(family + "!App", null, 0, out var pid);
            ShellLog.Info($"ActivateApplication -> 0x{hr:X8}, pid {pid}");
        }
        catch (Exception ex)
        {
            ShellLog.Error("starting the tray app failed", ex);
        }
    }
}
