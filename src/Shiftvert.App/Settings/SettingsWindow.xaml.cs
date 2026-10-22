using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shiftvert.App.Tray;
using Shiftvert.Core.Feedback;
using WinUIEx;

namespace Shiftvert.App.Settings;

/// <summary>Chime, toast, tools key, Open at Login and the optional-codec overview (PLAN §3.3).</summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? s_instance;

    private readonly AppServices _services;
    private bool _loading = true;

    private SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        Closed += (_, _) => s_instance = null;

        var prefs = services.Preferences;
        ChimeSwitch.IsOn = prefs.Chime;
        ToastSwitch.IsOn = prefs.ShowsToast;
        foreach (RadioButton rb in ToolsKeyChoice.Items)
            if (string.Equals((string)rb.Tag, prefs.ToolsKey.ToString(), StringComparison.Ordinal))
                ToolsKeyChoice.SelectedItem = rb;
        AltNote.Visibility = prefs.ToolsKey == ToolsKey.Alt ? Visibility.Visible : Visibility.Collapsed;

        VersionText.Text = $"Shiftvert {PackageInfo.VersionText}";
        BuildCodecList();
        _ = LoadOpenAtLoginAsync();

        _loading = false;
        this.CenterOnScreen(640, 820);
    }

    /// <summary>Shows the settings window, or brings the existing one to the front.</summary>
    public static void ShowSingleton(AppServices services)
    {
        if (s_instance is null) s_instance = new SettingsWindow(services);
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

    private void BuildCodecList()
    {
        var rows = CodecSummary.From(_services.Capabilities);
        foreach (var row in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line.Children.Add(new FontIcon
            {
                // Segoe Fluent Icons: CheckMark / Cancel.
                Glyph = row.Available ? "" : "",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources[row.Available ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush"],
            });
            line.Children.Add(new TextBlock { Text = row.Name });
            if (!row.Available)
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(line, row.Hint);
            CodecList.Children.Add(line);
        }

        var missing = rows.Where(r => !r.Available).ToList();
        CodecHint.Text = missing.Count == 0
            ? "Everything Shiftvert can use is available on this PC."
            : CodecSummary.StoreHint;
    }

    private async void OnOpenAtLoginToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var result = await _services.Startup.SetEnabledAsync(OpenAtLoginSwitch.IsOn);
        _loading = true;
        OpenAtLoginSwitch.IsOn = result.Enabled;
        _loading = false;
        OpenAtLoginNote.Text = result.Message ?? string.Empty;
        OpenAtLoginNote.Visibility = result.Message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnChimeToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.Chime = ChimeSwitch.IsOn;
    }

    private void OnToastToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _services.Preferences.ShowsToast = ToastSwitch.IsOn;
    }

    private void OnToolsKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ToolsKeyChoice.SelectedItem is not RadioButton rb) return;
        if (Enum.TryParse<ToolsKey>((string)rb.Tag, out var key))
        {
            _services.Preferences.ToolsKey = key;
            AltNote.Visibility = key == ToolsKey.Alt ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
