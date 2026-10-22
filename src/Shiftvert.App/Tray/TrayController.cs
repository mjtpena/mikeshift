using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Shiftvert.App.Settings;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Shiftvert.App.Tray;

/// <summary>
/// The notification-area icon and its menu (PLAN §1.1): header hint, Convert Files…, Open at Login, Settings…,
/// Check for Updates…, Version, Quit. No window, no taskbar button. The menu is a native popup menu built fresh each time
/// it opens so the Open at Login checkmark is always current.
/// </summary>
public sealed class TrayController : IDisposable
{
    /// <summary>Where "Check for Updates…" goes. The app itself never touches the network; the browser does.</summary>
    public const string ReleasesUrl = "https://github.com/roiegold/shiftvert/releases";

    private readonly AppServices _services;
    private TaskbarIcon? _icon;
    private bool _openAtLogin;
    private bool _menuOpen;

    public TrayController(AppServices services) => _services = services;

    /// <summary>
    /// Convert Files… picked these files. W3 attaches a handler that opens the wheel at the screen centre in click mode.
    /// While no handler is attached the temporary action picker is shown instead.
    /// </summary>
    public event Action<IReadOnlyList<string>>? ConvertFilesRequested;

    /// <summary>True while the tray icon exists in the notification area.</summary>
    public bool IsCreated => _icon?.IsCreated == true;

    /// <summary>Creates the tray icon. Call once on the UI thread.</summary>
    public void Create()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shiftvert.ico");
        var size = Math.Max(16, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));

        _icon = new TaskbarIcon
        {
            ToolTipText = "Shiftvert",
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ShowMenuAtCursor),
            RightClickCommand = new RelayCommand(ShowMenuAtCursor),
        };
        if (File.Exists(iconPath))
            _icon.Icon = new System.Drawing.Icon(iconPath, size, size);
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _ = RefreshOpenAtLoginAsync();
    }

    /// <summary>Pops the menu at the cursor (tray click, or a second launch of the app).</summary>
    public void ShowMenuAtCursor()
    {
        if (_icon?.TrayIcon is not { } tray || _menuOpen) return;

        _menuOpen = true;
        try
        {
            var menu = BuildMenu();
            PInvoke.GetCursorPos(out var p);
            PInvoke.SetForegroundWindow(new Windows.Win32.Foundation.HWND(tray.WindowHandle));
            menu.Show(tray.WindowHandle, p.X, p.Y);
        }
        finally
        {
            _menuOpen = false;
        }
        _ = RefreshOpenAtLoginAsync();
    }

    /// <summary>Convert Files…: file picker, then <see cref="ConvertFilesRequested"/> (or the temporary picker).</summary>
    public void ConvertFiles()
    {
        var paths = FilePicker.PickFiles();
        if (paths.Count > 0) RequestConvert(paths);
    }

    /// <summary>Raises <see cref="ConvertFilesRequested"/> for <paramref name="paths"/>; falls back to the temporary action picker.</summary>
    public void RequestConvert(IReadOnlyList<string> paths)
    {
        if (ConvertFilesRequested is { } handler) handler(paths);
        else ActionPickerWindow.Show(_services, paths);
    }

    /// <summary>Toggles Open at Login and reports a problem (e.g. disabled in Windows Settings) on the HUD.</summary>
    public async Task ToggleOpenAtLoginAsync()
    {
        var result = await _services.Startup.SetEnabledAsync(!_openAtLogin);
        _openAtLogin = result.Enabled;
        if (result.Message is { } message) _services.Hud.Show(message);
    }

    private PopupMenu BuildMenu()
    {
        void Post(Action action) => _services.Dispatcher.TryEnqueue(() => action());

        var menu = new PopupMenu();
        menu.Items.Add(new PopupMenuItem("Shift-drag files to convert", (_, _) => { }) { Enabled = false });
        menu.Items.Add(new PopupMenuSeparator());
        // Access key (C) instead of a "Ctrl+O" label: a global Ctrl+O hotkey would hijack Open in every other app.
        menu.Items.Add(new PopupMenuItem("&Convert Files…", (_, _) => Post(ConvertFiles)));
        menu.Items.Add(new PopupMenuItem("Open at Login", (_, _) => Post(() => _ = ToggleOpenAtLoginAsync())) { Checked = _openAtLogin });
        menu.Items.Add(new PopupMenuItem("Settings…", (_, _) => Post(() => SettingsWindow.ShowSingleton(_services))));
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(new PopupMenuItem("Check for Updates…", (_, _) => Post(OpenReleasesPage)));
        menu.Items.Add(new PopupMenuItem($"Version {PackageInfo.VersionText}", (_, _) => { }) { Enabled = false });
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(new PopupMenuItem("Quit", (_, _) => Post(() => ((App)Microsoft.UI.Xaml.Application.Current).Quit())));
        return menu;
    }

    private static void OpenReleasesPage() => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ReleasesUrl));

    private async Task RefreshOpenAtLoginAsync() => _openAtLogin = await _services.Startup.GetEnabledAsync();

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
