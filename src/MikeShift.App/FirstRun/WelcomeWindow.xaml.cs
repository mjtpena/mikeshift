using Microsoft.UI.Xaml;
using MikeShift.App.Chrome;
using MikeShift.App.Settings;
using MikeShift.App.Tray;
using MikeShift.Core.Hosting;
using MikeShift.Core.Feedback;
using Windows.System;
using WinUIEx;

namespace MikeShift.App.FirstRun;

/// <summary>
/// The welcome window (UX review F-01, F-02): shown on first launch and from the tray's "How it works". A picture of the wheel, the three
/// steps with keycaps, how to find the tray icon, and "Try it with Convert files…".
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private static WelcomeWindow? s_instance;

    private readonly AppServices _services;

    private WelcomeWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        WindowChrome.Apply(this, "Welcome to MikeShift");
        WindowChrome.MakeFixedSize(this);
        WindowChrome.PlaceCentered(this, 580, 840);
    }

    /// <summary>
    /// Shows the welcome window if it has never been shown, and records that it was. A start by the Open at login task does not show it when
    /// that record cannot be saved (<see cref="WelcomePolicy"/>): it would otherwise open again at every sign-in.
    /// </summary>
    public static void ShowIfFirstRun(AppServices services)
    {
        var prefs = services.Preferences;
        if (prefs.Launched) return;
        var remembered = prefs.TryMarkLaunched();
        if (WelcomePolicy.ShouldShow(alreadyLaunched: false, remembered, StartupService.WasLaunchedAtLogin())) Show(services);
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

    private void OnTaskbarSettingsClicked(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("ms-settings:taskbar"));

    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        TryButton.IsEnabled = false;
        if (OpenAtLoginBox.IsChecked == true)
        {
            var result = await _services.Startup.SetEnabledAsync(true);
            if (result.Message is { } message) _services.Hud.Show(new ToastContent(message, null, ToastKind.Warning));
        }
        Close();
    }

    private void OnTryClicked(object sender, RoutedEventArgs e)
    {
        OnStartClicked(sender, e);
        ((App)Application.Current).Tray.ConvertFiles();
    }
}
