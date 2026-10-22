using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Shiftvert.App.FirstRun;
using Shiftvert.App.Settings;
using Shiftvert.App.Tray;
using Shiftvert.App.Wheel;
using Shiftvert.Core;

namespace Shiftvert.App;

/// <summary>
/// The resident tray app: no main window, single instance (see <see cref="Program"/>), tray icon, job runner, HUD toast.
/// W3 hooks the wheel and the input monitor in <see cref="OnLaunched"/> (look for "W3 wiring").
/// </summary>
public partial class App : Application
{
    private readonly bool _smokeTest;
    private DragCoordinator? _drag;

    public App()
    {
        InitializeComponent();
        _smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal);

        // There is no main window: closing Settings or Welcome must never end the process.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    /// <summary>The running app.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>Preferences, job runner, HUD, interaction... Valid after <see cref="OnLaunched"/> started.</summary>
    public AppServices Services { get; private set; } = null!;

    /// <summary>The tray icon and menu. <c>Tray.ConvertFilesRequested</c> is the hook for the wheel's click mode.</summary>
    public TrayController Tray { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Services = AppServices.Create(dispatcher);
        Tray = new TrayController(Services);
        Tray.Create();

        // Smoke test hook: `Shiftvert.exe --smoke-test` starts, builds the tray and exits cleanly.
        if (_smokeTest)
        {
            // W3: also bring up the wheel (window, Win2D, drop target) without the global hooks, so the smoke test covers it.
            _drag = DragCoordinator.Start(Services, installHooks: false);
            Quit();
            return;
        }

        // A second launch is redirected here: pop the tray menu at the cursor.
        Program.SecondInstanceActivated += () => dispatcher.TryEnqueue(Tray.ShowMenuAtCursor);

        // Probe optional codecs now (MF/WIC probes can take a moment) instead of on the first drag.
        _ = Task.Run(() => Capabilities.Current);

        // W3 wiring: the input monitor + wheel thread, and the click-mode wheel for Convert Files….
        _drag = DragCoordinator.Start(Services);
        if (_drag is not null) Tray.ConvertFilesRequested += _drag.ShowClickMode;
        // W4a wiring: Services.Interaction.CropEditor / TrimEditor.

        var args2 = Environment.GetCommandLineArgs();
        dispatcher.TryEnqueue(() =>
        {
            if (IsElevated())
                Services.Dialogs.ShowError("Shiftvert is running as administrator",
                    "Windows does not let a normal Explorer window drag files onto an administrator program, and the other way round, " +
                    "so Shift-drag will not work as expected. Quit Shiftvert and start it again without \"Run as administrator\".");

            if (args2.Contains("--show-welcome", StringComparer.Ordinal)) Services.Preferences.Launched = false;
            WelcomeWindow.ShowIfFirstRun(Services);
            if (args2.Contains("--show-settings", StringComparer.Ordinal)) SettingsWindow.ShowSingleton(Services);

            // Dev/test hook: `Shiftvert.exe --convert <path>...` behaves like Convert Files… with those paths.
            var convertAt = Array.IndexOf(args2, "--convert");
            if (convertAt >= 0 && convertAt + 1 < args2.Length)
                Tray.RequestConvert(args2[(convertAt + 1)..]);
        });
    }

    /// <summary>Stops running jobs, removes the tray icon and exits the process.</summary>
    public void Quit()
    {
        _drag?.Dispose();
        _drag = null;
        Services.Jobs.Shutdown();
        Services.Hud.Dispose();
        Tray.Dispose();
        Exit();
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
