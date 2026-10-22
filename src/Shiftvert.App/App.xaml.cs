using Microsoft.UI.Xaml;

namespace Shiftvert.App;

/// <summary>
/// Placeholder application shell (W1). Starts with no window. W2f replaces this with the tray app (single instance,
/// tray icon, job runner); W3 adds the wheel wiring.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Smoke test hook: `Shiftvert.exe --smoke-test` starts and exits cleanly.
        if (Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal))
            Exit();
    }
}
