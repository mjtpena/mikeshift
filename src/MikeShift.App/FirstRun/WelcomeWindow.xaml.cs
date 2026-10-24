using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MikeShift.App.Chrome;
using MikeShift.App.Settings;
using MikeShift.App.Tray;
using MikeShift.Core;
using MikeShift.Core.Feedback;
using MikeShift.Core.Hosting;
using MikeShift.Core.Interaction;
using Windows.System;
using WinUIEx;

namespace MikeShift.App.FirstRun;

/// <summary>
/// The welcome window: shown on first launch and from the tray's "How it works". A picture of the wheel, the choice of
/// the key that opens it while dragging (positioning P1-2), the three steps with keycaps, how to find the tray icon, the keyboard shortcut,
/// "Try it with Convert files…", and, when this PC cannot open iPhone photos yet, an optional card that links to the free Store extensions
/// (positioning P0-3).
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private static WelcomeWindow? s_instance;

    private readonly AppServices _services;
    private bool _loading = true;

    private WelcomeWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        WindowChrome.Apply(this, "Welcome to MikeShift");
        WindowChrome.MakeFixedSize(this);
        WindowChrome.PlaceCentered(this, 580, 1000);

        foreach (RadioButton rb in ArmKeyChoice.Items)
            if (string.Equals((string)rb.Tag, services.Preferences.ArmKey.ToString(), StringComparison.Ordinal))
                ArmKeyChoice.SelectedItem = rb;
        _loading = false;
        ShowArmKey();
        _ = LoadCodecCardAsync();
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

    // ---- the activation choice ----

    private void OnArmKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ArmKeyChoice.SelectedItem is not RadioButton rb) return;
        if (Enum.TryParse<ArmKey>((string)rb.Tag, out var key)) _services.Preferences.ArmKey = key;
        ShowArmKey();
    }

    /// <summary>Makes every key name on the page follow the chosen arming key (and the tools key that goes with it).</summary>
    private void ShowArmKey()
    {
        var prefs = _services.Preferences;
        var arm = prefs.ArmKey;
        var tools = prefs.ToolsKey;
        ArmKeycap.Text = ToolsKeys.Label(arm);
        var (verb, key) = OnboardingCopy.ToolsHeading(arm, tools);
        ToolsVerb.Text = verb;
        ToolsKeycap.Text = key;
        ToolsDescription.Text = OnboardingCopy.ToolsDescription(arm, tools);
        ArmKeyReassurance.Text = OnboardingCopy.ArmKeyTradeoff + " " + FeedbackCopy.OriginalsSafe;
        if (OnboardingCopy.HotkeyHint(prefs.Hotkey) is { } hint)
        {
            HotkeyText.Text = hint;
            HotkeyText.Visibility = Visibility.Visible;
        }
        else
        {
            HotkeyText.Visibility = Visibility.Collapsed;
        }
    }

    // ---- the optional codec card ----

    private async Task LoadCodecCardAsync()
    {
        try
        {
            // The startup probe may still be running on a cold PC: wait for it off the UI thread, then fill the card in.
            var caps = await _services.CapabilitiesAsync;
            var missing = CodecHelp.Missing(caps);
            if (OnboardingCopy.CodecCard(missing) is not { } card) return;

            CodecTitle.Text = card.Title;
            CodecBody.Text = card.Body;
            CodecButtons.Children.Clear();
            var first = true;
            foreach (var prompt in missing)
            {
                var button = new Button { Content = OnboardingCopy.StoreButtonLabel(prompt) };
                if (first) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                first = false;
                AutomationProperties.SetName(button, $"{OnboardingCopy.StoreButtonLabel(prompt)} from the Microsoft Store");
                var uri = prompt.StoreUri;
                button.Click += (_, _) => StoreLauncher.Open(uri);
                CodecButtons.Children.Add(button);
            }
            CodecCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Wheel.WheelLog.Error("the welcome window's codec check failed", ex);
        }
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
