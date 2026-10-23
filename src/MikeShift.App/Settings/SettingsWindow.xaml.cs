using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MikeShift.App.Chrome;
using MikeShift.App.Tray;
using MikeShift.Core;
using MikeShift.Core.Feedback;
using Windows.System;
using WinUIEx;

namespace MikeShift.App.Settings;

/// <summary>
/// Settings as Windows 11 setting cards (UX review S-01 to S-04): General (open at login, sound, message, show in folder), Wheel (tools
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
        foreach (RadioButton rb in ToolsKeyChoice.Items)
            if (string.Equals((string)rb.Tag, prefs.ToolsKey.ToString(), StringComparison.Ordinal))
                ToolsKeyChoice.SelectedItem = rb;
        ShowToolsKey(prefs.ToolsKey);

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

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
            if (result.ToString() != "RestartPending") throw new InvalidOperationException(result.ToString());
        }
        catch (Exception)
        {
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

    private void OnToolsKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ToolsKeyChoice.SelectedItem is not RadioButton rb) return;
        if (Enum.TryParse<ToolsKey>((string)rb.Tag, out var key))
        {
            _services.Preferences.ToolsKey = key;
            ShowToolsKey(key);
        }
    }

    private void ShowToolsKey(ToolsKey key)
    {
        ToolsKeySummary.Text = key switch
        {
            ToolsKey.MouseWheelOnly => "Mouse wheel only",
            ToolsKey.Alt => "Alt",
            _ => "Ctrl (recommended)",
        };
        AltNote.IsOpen = key == ToolsKey.Alt;
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
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikeShift");
            Directory.CreateDirectory(folder);
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Explorer missing: nothing to do
        }
    }
}
