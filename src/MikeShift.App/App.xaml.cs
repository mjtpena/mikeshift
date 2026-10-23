using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MikeShift.App.FirstRun;
using MikeShift.App.Settings;
using MikeShift.App.Tray;
using MikeShift.App.Wheel;
using MikeShift.Core;
using MikeShift.Core.Feedback;

namespace MikeShift.App;

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
        Chrome.WindowChrome.ApplyForcedTheme(this);
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

        // Smoke test hook: `MikeShift.exe --smoke-test` starts, builds the tray and exits cleanly.
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
        _drag = DragCoordinator.Start(Services, installHooks: Environment.GetEnvironmentVariable("MIKESHIFT_NO_HOOKS") != "1");
        if (_drag is not null) Tray.ConvertFilesRequested += _drag.ShowClickMode;
        // Crop/Trim editors for interactive actions; dev flags `--crop <img>` / `--trim <media>` open them directly.
        Editors.EditorRegistration.Register(Services.Interaction);
        Editors.EditorRegistration.TryHandleDevArgs(Environment.GetCommandLineArgs());

        var args2 = Environment.GetCommandLineArgs();
        dispatcher.TryEnqueue(() =>
        {
            if (IsElevated())
                Services.Dialogs.Show(new DialogContent(FeedbackCopy.ElevatedTitle, FeedbackCopy.ElevatedBody, [], null, DialogSeverity.Warning,
                    [new DialogButton("Close", DialogCommand.Close), new DialogButton("Quit MikeShift", DialogCommand.QuitApp)]));

            if (args2.Contains("--show-welcome", StringComparer.Ordinal)) Services.Preferences.Launched = false;
            WelcomeWindow.ShowIfFirstRun(Services);
            if (args2.Contains("--show-settings", StringComparer.Ordinal)) SettingsWindow.ShowSingleton(Services);

            // Dev/test hook: `MikeShift.exe --scene <name>` shows one canned UI state (see DevScenes).
            DevScenes.TryRun(args2, Services, Tray);

            // Dev/test hook: `MikeShift.exe --convert <path>...` behaves like Convert Files… with those paths.
            var convertAt = Array.IndexOf(args2, "--convert");
            if (convertAt >= 0 && convertAt + 1 < args2.Length)
                Tray.RequestConvert(args2[(convertAt + 1)..]);
        });
    }

    private bool _quitting;

    /// <summary>
    /// Stops running jobs, removes the tray icon and exits the process. Running jobs get up to 3 s (awaited, so the UI thread keeps
    /// pumping messages) to unwind and delete their temp files before the process ends.
    /// </summary>
    public async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _drag?.Dispose();
        _drag = null;
        Tray.Dispose();
        try { await Services.Jobs.ShutdownAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception) { /* exit regardless */ }
        Services.Hud.Dispose();
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
