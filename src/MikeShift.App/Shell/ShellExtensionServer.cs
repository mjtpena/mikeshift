using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MikeShift.App.Shell;

/// <summary>
/// The out-of-process COM server Explorer's context menu talks to: <c>MikeShift.exe -Embedding</c>. Windows starts it (the package manifest declares
/// it as a <c>com:ExeServer</c> for the command's CLSID) the first time the user right-clicks, and it exits 10 minutes after the last call. It is a separate,
/// light process on purpose: no MikeShift (or .NET) code is ever loaded into explorer.exe, so a bug here cannot crash or stall Explorer, and the
/// WinUI tray app is not involved in building the menu. It does not start WinUI, the wheel or the input hooks; it reads the capability cache
/// the tray app wrote and hands the chosen action to the tray app over a local pipe.
/// </summary>
internal static class ShellExtensionServer
{
    private static readonly List<object> KeepAlive = [];

    /// <summary>True for the command line COM uses to start a local server.</summary>
    public static bool IsEmbedding(string[] args) =>
        args.Any(a => a.Equals(ShellNames.EmbeddingSwitch, StringComparison.OrdinalIgnoreCase) || a.Equals("/Embedding", StringComparison.OrdinalIgnoreCase));

    /// <summary>Serves until idle, then returns the process exit code. The caller is the process's entry point.</summary>
    public static int Run()
    {
        var exit = 0;
        // Free-threaded: Explorer's calls arrive on RPC worker threads, so a slow call can never block another one.
        var thread = new Thread(() => exit = Serve()) { Name = "MikeShift shell server" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return exit;
    }

    private static int Serve()
    {
        var cookies = new List<uint>();
        try
        {
            Native.CoInitializeEx(0, Native.CoinitMultithreaded);
            ShellLog.Info("server started");
            Register(ShellNames.ModernCommandClsid, () => new ConvertCommand(), cookies);

            while (!ServerLifetime.IsIdle) Thread.Sleep(500);
            ShellLog.Info("server idle: exiting");
        }
        catch (Exception ex)
        {
            ShellLog.Error("server failed", ex);
            return 1;
        }
        finally
        {
            foreach (var cookie in cookies) Native.CoRevokeClassObject(cookie);
            Native.CoUninitialize();
        }
        return 0;
    }

    private static unsafe void Register(Guid clsid, Func<object> create, List<uint> cookies)
    {
        var factory = new ClassFactory(create);
        KeepAlive.Add(factory);
        var unknown = ComBridge.Wrappers.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
        try
        {
            var iid = typeof(IClassFactory).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var classFactory));
            try
            {
                var hr = Native.CoRegisterClassObject(in clsid, classFactory, Native.ClsctxLocalServer, Native.RegclsMultipleUse, out var cookie);
                Marshal.ThrowExceptionForHR(hr);
                cookies.Add(cookie);
            }
            finally
            {
                Marshal.Release(classFactory);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

/// <summary>Creates one command object per request, as COM asks.</summary>
[GeneratedComClass]
internal sealed partial class ClassFactory(Func<object> create) : IClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0;
        try
        {
            if (outer != 0) return Hr.NoAggregation;
            using var _ = ServerLifetime.Begin();
            var unknown = ComBridge.Wrappers.GetOrCreateComInterfaceForObject(create(), CreateComInterfaceFlags.None);
            try { return Marshal.QueryInterface(unknown, in iid, out instance); }
            finally { Marshal.Release(unknown); }
        }
        catch (Exception ex)
        {
            ShellLog.Error("CreateInstance failed", ex);
            return Hr.Fail;
        }
    }

    public int LockServer(int @lock)
    {
        ServerLifetime.Touch();
        return Hr.Ok;
    }
}
