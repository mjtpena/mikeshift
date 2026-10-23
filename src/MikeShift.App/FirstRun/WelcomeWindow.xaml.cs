using Microsoft.UI.Xaml;
using MikeShift.App.Tray;
using WinUIEx;

namespace MikeShift.App.FirstRun;

/// <summary>Shown once, on first launch (the <c>launched</c> preference): explains Shift-drag, Ctrl and Convert Files….</summary>
public sealed partial class WelcomeWindow : Window
{
    private readonly AppServices _services;

    private WelcomeWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        this.CenterOnScreen(620, 640);
    }

    private static WelcomeWindow? s_instance;

    /// <summary>Shows the welcome window if it has never been shown, and records that it was.</summary>
    public static void ShowIfFirstRun(AppServices services)
    {
        if (services.Preferences.Launched) return;
        services.Preferences.Launched = true;
        Show(services);
    }

    /// <summary>Shows the welcome window ("How it works" in the tray menu), or brings the open one to the front.</summary>
    public static void Show(AppServices services)
    {
        if (s_instance is null)
        {
            s_instance = new WelcomeWindow(services);
            s_instance.Closed += (_, _) => s_instance = null;
        }
        s_instance.Activate();
        s_instance.SetForegroundWindow();
    }

    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        if (OpenAtLoginBox.IsChecked == true)
        {
            var result = await _services.Startup.SetEnabledAsync(true);
            if (result.Message is { } message) _services.Hud.Show(message);
        }
        Close();
    }
}
