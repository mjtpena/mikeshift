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

    /// <summary>Shows the welcome window if it has never been shown, and records that it was.</summary>
    public static void ShowIfFirstRun(AppServices services)
    {
        if (services.Preferences.Launched) return;
        services.Preferences.Launched = true;

        var window = new WelcomeWindow(services);
        window.Activate();
        window.SetForegroundWindow();
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
