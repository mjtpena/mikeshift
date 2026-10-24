using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MikeShift.App.Chrome;
using MikeShift.App.Input;
using MikeShift.App.Selection;
using MikeShift.App.Tray;
using MikeShift.Core;
using MikeShift.Core.Feedback;
using MikeShift.Core.Hotkeys;
using MikeShift.Core.Interaction;
using Windows.System;
using WinUIEx;

namespace MikeShift.App.Settings;

/// <summary>
/// Settings as Windows 11 setting cards (UX review S-01 to S-04): General (open at login, sound, message, show in folder, use Word), Wheel (tools
/// key), Format support (what this PC can convert, with text state, Store links and a restart hint) and About.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? s_instance;

    private readonly AppServices _services;
    private readonly Capabilities _startupCapabilities;
    private bool _loading = true;
    private DateTimeOffset _lastProbe = DateTimeOffset.MinValue;
    private bool _probing;

    private SettingsWindow(AppServices services)
    {
        _services = services;
        _startupCapabilities = services.Capabilities;
        InitializeComponent();
        WindowChrome.Apply(this, "Settings");
        Closed += (_, _) => s_instance = null;

        var prefs = services.Preferences;
        ChimeSwitch.IsOn = prefs.Chime;
        ToastSwitch.IsOn = prefs.ShowsToast;
        RevealSwitch.IsOn = prefs.ShowInFolderAfter;
        WordSwitch.IsOn = prefs.UseWord;
        if (!_startupCapabilities.WordInstalled)
        {
            // Nothing to turn on: say so instead of offering a switch that changes nothing.
            WordSwitch.IsEnabled = false;
            WordCard.Description = "Microsoft Word is not installed on this PC.";
        }
        foreach (RadioButton rb in ArmKeyChoice.Items)
            if (string.Equals((string)rb.Tag, prefs.ArmKey.ToString(), StringComparison.Ordinal))
                ArmKeyChoice.SelectedItem = rb;
        ShowArmKey(prefs.ArmKey);
        ShowHotkey(prefs.Hotkey);
        if (ExplorerHotkey.Current is { } hotkey) hotkey.StateChanged += OnHotkeyStateChanged;
        Closed += (_, _) => { if (ExplorerHotkey.Current is { } hk) hk.StateChanged -= OnHotkeyStateChanged; };

        VersionText.Text = $"Version {PackageInfo.VersionText}";
        AboutIcon.Source = new BitmapImage(new Uri(WindowChrome.AssetPath("Square150x150Logo.scale-200.png")));
        BuildCodecList(_startupCapabilities);
        _ = LoadOpenAtLoginAsync();

        _loading = false;
        WindowChrome.PlaceCentered(this, 720, 860, minWidthDip: 480, minHeightDip: 480);
        Activated += OnActivated;
    }

    /// <summary>Shows the settings window, or brings the existing one to the front.</summary>
    public static void ShowSingleton(AppServices services)
    {
        s_instance ??= new SettingsWindow(services);
        s_instance.Activate();
        s_instance.SetForegroundWindow();
    }

    private async Task LoadOpenAtLoginAsync()
    {
        var enabled = await _services.Startup.GetEnabledAsync();
        _loading = true;
        OpenAtLoginSwitch.IsOn = enabled;
        _loading = false;
    }

    // ---- format support (S-02) ----

    private void BuildCodecList(Capabilities capabilities)
    {
        var rows = CodecSummary.From(capabilities);
        CodecSummaryText.Text = CodecSummary.Headline(rows);
        CodecHint.Text = rows.Any(r => !r.Available) ? CodecSummary.StoreHint : CodecSummary.AllAvailable;
        CodecList.Children.Clear();
        foreach (var row in CodecSummary.MissingFirst(rows)) CodecList.Children.Add(BuildCodecRow(row));
        AutomationProperties.SetName(CodecExpander, "Format support, " + CodecSummary.Headline(rows));
    }

    private static UIElement BuildCodecRow(CodecStatus row)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 36, Padding = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AutomationProperties.SetName(grid, row.AccessibleName);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = row.Name, TextWrapping = TextWrapping.Wrap });
        if (!row.Available)
            name.Children.Add(new TextBlock
            {
                Text = row.Hint,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        grid.Children.Add(name);

        // The state is written out (never only a colour or a glyph).
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        state.Children.Add(new FontIcon
        {
            Glyph = row.Available ? "" : "",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources[row.Available ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"],
        });
        state.Children.Add(new TextBlock { Text = row.StateText });
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);

        if (!row.Available && row.StoreUri is { } uri)
        {
            var link = new HyperlinkButton { Content = "Get from Microsoft Store", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(link, $"Get {row.Name} from the Microsoft Store");
            link.Click += (_, _) => _ = Launcher.LaunchUriAsync(new Uri(uri));
            Grid.SetColumn(link, 2);
            grid.Children.Add(link);
        }
        return grid;
    }

    /// <summary>
    /// After the user installs an extension in the Store and comes back, probe again: if the set of supported formats changed, offer a
    /// restart (the app reads the probe once at start-up).
    /// </summary>
    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated || _probing) return;
        if (DateTimeOffset.UtcNow - _lastProbe < TimeSpan.FromSeconds(10)) return;
        _probing = true;
        try
        {
            var now = await Task.Run(Capabilities.Probe);
            _lastProbe = DateTimeOffset.UtcNow;
            RestartNote.IsOpen = CodecSummary.Differs(_startupCapabilities, now);
            if (RestartNote.IsOpen) BuildCodecList(now);
        }
        catch (Exception)
        {
            // best effort
        }
        finally
        {
            _probing = false;
        }
    }

    // Same shutdown as Exit in the tray menu (input hooks, tray icon, running jobs), then Windows starts the app again.
    private async void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ((App)Application.Current).RestartAsync();
        }
        catch (Exception ex)
        {
            Wheel.WheelLog.Error("restart failed", ex);
            RestartNote.Message = "MikeShift could not restart itself. Exit it from the tray menu and open it again.";
        }
    }

    // ---- general ----

    private async void OnOpenAtLoginToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var result = await _services.Startup.SetEnabledAsync(OpenAtLoginSwitch.IsOn);
        _loading = true;
        OpenAtLoginSwitch.IsOn = result.Enabled;
        _loading = false;
        OpenAtLoginNote.Message = result.Message ?? string.Empty;
        OpenAtLoginNote.IsOpen = result.Message is not null;
    }

    private void OnChimeToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.Chime = ChimeSwitch.IsOn;
    }

    private void OnToastToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.ShowsToast = ToastSwitch.IsOn;
    }

    private void OnRevealToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.ShowInFolderAfter = RevealSwitch.IsOn;
    }

    private void OnWordToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.UseWord = WordSwitch.IsOn;
    }

    // ---- the key that opens the wheel while dragging (positioning P1-2) ----

    private void OnArmKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ArmKeyChoice.SelectedItem is not RadioButton rb) return;
        if (!Enum.TryParse<ArmKey>((string)rb.Tag, out var key)) return;
        _services.Preferences.ArmKey = key; // also moves the tools key off the new arming key
        ShowArmKey(key);
    }

    /// <summary>Everything that names the arming key follows it: the summary, the tools-key choices, the hint under them and the About line.</summary>
    private void ShowArmKey(ArmKey arm)
    {
        var prefs = _services.Preferences;
        var label = ToolsKeys.Label(arm);
        ArmKeySummary.Text = $"Hold {label}";
        ArmKeyNote.Text = OnboardingCopy.ArmKeyTradeoff + " " + FeedbackCopy.OriginalsSafe;
        AboutHint.Text = $"{label}-drag files in File Explorer to convert them. Your original files are never changed, and MikeShift itself never goes online.";
        AutomationProperties.SetName(ArmKeyExpander, $"Open the wheel while dragging with, {label}");

        // The first tools-key choice is "the other of Shift and Ctrl".
        var other = arm == ArmKey.Ctrl ? ToolsKey.Shift : ToolsKey.Ctrl;
        var wasLoading = _loading;
        _loading = true;
        ToolsOtherKeyChoice.Tag = other.ToString();
        ToolsOtherKeyChoice.Content = $"{ToolsKeys.Label(arm, other)} (recommended)";
        foreach (RadioButton rb in ToolsKeyChoice.Items)
            if (string.Equals((string)rb.Tag, prefs.ToolsKey.ToString(), StringComparison.Ordinal))
                ToolsKeyChoice.SelectedItem = rb;
        _loading = wasLoading;
        ShowToolsKey(prefs.ToolsKey);
    }

    private void OnToolsKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ToolsKeyChoice.SelectedItem is not RadioButton rb) return;
        if (Enum.TryParse<ToolsKey>((string)rb.Tag, out var key))
        {
            _services.Preferences.ToolsKey = key;
            ShowToolsKey(_services.Preferences.ToolsKey);
        }
    }

    private void ShowToolsKey(ToolsKey key)
    {
        var arm = _services.Preferences.ArmKey;
        ToolsKeySummary.Text = key switch
        {
            ToolsKey.MouseWheelOnly => "Mouse wheel only",
            ToolsKey.Alt => "Alt",
            _ => $"{ToolsKeys.Label(arm, key)} (recommended)",
        };
        AltNote.Message = $"Alt+Shift also changes the keyboard layout on many PCs. {ToolsKeys.Label(arm, arm == ArmKey.Ctrl ? ToolsKey.Shift : ToolsKey.Ctrl)} is recommended.";
        AltNote.IsOpen = key == ToolsKey.Alt;
        ToolsKeyNote.Text = $"A mouse-wheel tick switches the wheel in every mode. {ToolsKeys.Label(arm)} always goes back to formats.";
    }

    // ---- the hotkey for the current Explorer selection (positioning P1-1) ----

    private bool _capturingHotkey;

    private void ShowHotkey(HotkeySpec? hotkey)
    {
        _capturingHotkey = false;
        var text = hotkey?.ToString().Replace("+", " + ") ?? "Not set";
        HotkeyButton.Content = text;
        AutomationProperties.SetName(HotkeyButton, $"Keyboard shortcut for the files selected in File Explorer: {text}. Select to change.");
        HotkeyCard.Description = OnboardingCopy.HotkeyDescription;
        HotkeyOffButton.Content = hotkey is null ? $"Use {HotkeySpec.Default.ToString().Replace("+", " + ")}" : "Turn off";
        UpdateHotkeyNote(hotkey, ExplorerHotkey.Current?.State ?? HotkeyState.Off);
    }

    private void UpdateHotkeyNote(HotkeySpec? hotkey, HotkeyState state)
    {
        string? message = null;
        var severity = InfoBarSeverity.Warning;
        if (hotkey is { } spec)
        {
            var check = HotkeyConflicts.Check(spec);
            if (check.Verdict != HotkeyVerdict.Ok) message = check.Message;
            if (state == HotkeyState.Taken)
                message = $"{spec.ToString().Replace("+", " + ")} is already used by another program, so MikeShift could not take it. Choose a different shortcut.";
        }
        HotkeyNote.Severity = severity;
        HotkeyNote.Message = message ?? string.Empty;
        HotkeyNote.IsOpen = message is not null;
    }

    private void OnHotkeyStateChanged(HotkeyState state) =>
        DispatcherQueue.TryEnqueue(() => UpdateHotkeyNote(_services.Preferences.Hotkey, state));

    private void OnHotkeyButtonClicked(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyButton.Content = "Press the new shortcut…  (Esc to cancel)";
        AutomationProperties.SetName(HotkeyButton, "Press the new keyboard shortcut now. Escape cancels.");
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey) ShowHotkey(_services.Preferences.Hotkey);
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturingHotkey) return;
        var vk = (int)e.Key;
        if (HotkeySpec.IsModifierKey(vk)) { e.Handled = true; return; } // wait for the key that completes the shortcut
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            ShowHotkey(_services.Preferences.Hotkey);
            return;
        }

        static bool Down(VirtualKey key) =>
            (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var mods = HotkeyModifiers.None;
        if (Down(VirtualKey.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Down(VirtualKey.Menu)) mods |= HotkeyModifiers.Alt;
        if (Down(VirtualKey.Shift)) mods |= HotkeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods |= HotkeyModifiers.Win;

        var spec = new HotkeySpec(mods, vk);
        var check = HotkeyConflicts.Check(spec);
        if (check.Verdict == HotkeyVerdict.Rejected || spec.KeyName is null)
        {
            ShowHotkey(_services.Preferences.Hotkey);
            HotkeyNote.Severity = InfoBarSeverity.Warning;
            HotkeyNote.Message = check.Message ?? "That key can't be used.";
            HotkeyNote.IsOpen = true;
            return;
        }
        _services.Preferences.Hotkey = spec;
        ShowHotkey(spec);
    }

    private void OnHotkeyOffClicked(object sender, RoutedEventArgs e)
    {
        var next = _services.Preferences.Hotkey is null ? HotkeySpec.Default : (HotkeySpec?)null;
        _services.Preferences.Hotkey = next;
        ShowHotkey(next);
    }

    // ---- about ----

    private static void Open(string url) => _ = Launcher.LaunchUriAsync(new Uri(url));

    private void OnCheckUpdatesClicked(object sender, RoutedEventArgs e) => Open(AppLinks.Releases);

    private void OnGitHubClicked(object sender, RoutedEventArgs e) => Open(AppLinks.Project);

    private void OnNoticeClicked(object sender, RoutedEventArgs e) => Open(AppLinks.Notice);

    private void OnShiftvertClicked(object sender, RoutedEventArgs e) => Open(AppLinks.Shiftvert);

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            // The real folder: packaged, writes under %LOCALAPPDATA%\MikeShift are redirected into the package's LocalCache, and Explorer would
            // show the unredirected (empty or missing) one.
            var folder = AppData.Folder;
            Directory.CreateDirectory(folder);
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Explorer missing: nothing to do
        }
    }
}
