using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MikeShift.App;

/// <summary>
/// Custom entry point (<c>DISABLE_XAML_GENERATED_MAIN</c>) so a second launch can be redirected to the first
/// instance before any XAML starts. The running instance pops its tray menu at the cursor.
/// </summary>
public static class Program
{
    /// <summary>The single-instance key shared by every launch of MikeShift.</summary>
    public const string InstanceKey = "mikeshift";

    /// <summary>Raised (on a background thread) in the first instance when another launch was redirected to it.</summary>
    public static event Action? SecondInstanceActivated;

    [STAThread]
    private static int Main(string[] args)
    {
        // Explorer right-click: COM starts `MikeShift.exe -Embedding` as the out-of-process context-menu server (no WinUI, no tray, no single-instance check).
        if (Shell.ShellExtensionServer.IsEmbedding(args)) return Shell.ShellExtensionServer.Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Dev-only (see DevSwitches): `MikeShift.exe --dev --render-wheel <scenario|all|list> <out.png|dir>` renders the wheel offscreen over a synthetic
        // desktop and exits. No tray, no hooks, no windows, no single-instance check: it never touches the real screen or the running instance.
        if (DevSwitches.Enabled && Array.IndexOf(args, "--render-wheel") is var render and >= 0 && render + 1 < args.Length)
            return Wheel.WheelPreview.Run(args[(render + 1)..]);

        // The smoke test (and dev scenes / builds under test, MIKESHIFT_NO_SINGLE_INSTANCE=1) must work even while a
        // real instance is running, so they skip the single-instance check.
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal) || DevSwitches.NoSingleInstance;
        if (!smoke && !RegisterOrRedirect())
            return 0;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    /// <summary>True if this process is the first instance and should run; false if it handed over to the first one.</summary>
    private static bool RegisterOrRedirect()
    {
        AppInstance main;
        try
        {
            main = AppInstance.FindOrRegisterForKey(InstanceKey);
        }
        catch (Exception)
        {
            return true; // lifecycle APIs unavailable: run anyway rather than refuse to start
        }

        if (main.IsCurrent)
        {
            main.Activated += (_, _) => SecondInstanceActivated?.Invoke();
            return true;
        }

        // Redirect from a worker thread (blocking an STA thread on the async call would deadlock), then exit.
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var done = new ManualResetEventSlim();
        Task.Run(async () =>
        {
            try { await main.RedirectActivationToAsync(activation); }
            catch (Exception) { /* the first instance may be shutting down; nothing useful to do */ }
            finally { done.Set(); }
        });
        done.Wait();
        return false;
    }
}
