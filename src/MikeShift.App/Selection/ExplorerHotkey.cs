using MikeShift.App.Input;
using MikeShift.App.Tray;
using MikeShift.App.Wheel;
using MikeShift.Core.Feedback;
using MikeShift.Core.Hotkeys;
using MikeShift.Core.Interaction;
using Windows.Win32;

namespace MikeShift.App.Selection;

/// <summary>
/// "Convert what I have selected in File Explorer" from the keyboard: a global hotkey that opens the click-mode wheel on the
/// files selected in the foreground Explorer window or on the desktop. It gives keyboard-only, touch and pen users a way in (the drag gesture needs
/// a mouse and a held key). Nothing selected, or the foreground window is not Explorer: a toast says so and nothing else happens. The shortcut
/// comes from the <c>hotkey</c> preference (default Ctrl+Alt+Shift+Space, "None" turns it off) and follows changes at once.
/// </summary>
internal sealed class ExplorerHotkey : IDisposable
{
    private readonly AppServices _services;
    private readonly HotkeyService _hotkey;
    private readonly Action<IReadOnlyList<string>, PointPx?> _show;
    private bool _disposed;

    private ExplorerHotkey(AppServices services, Action<IReadOnlyList<string>, PointPx?> show)
    {
        _services = services;
        _show = show;
        _hotkey = new HotkeyService();
        _hotkey.Pressed += OnPressed;
        _hotkey.StateChanged += state => StateChanged?.Invoke(state);
    }

    /// <summary>The running instance (Settings reads its state to say "another program already uses this shortcut").</summary>
    public static ExplorerHotkey? Current { get; private set; }

    /// <summary>Raised on the hotkey thread whenever the registration changed.</summary>
    public event Action<HotkeyState>? StateChanged;

    /// <summary>Whether the shortcut is registered.</summary>
    public HotkeyState State => _hotkey.State;

    /// <summary>Starts listening for the preferred shortcut. <paramref name="show"/> opens the click-mode wheel for the paths near a screen point.</summary>
    public static ExplorerHotkey? Start(AppServices services, Action<IReadOnlyList<string>, PointPx?> show)
    {
        try
        {
            var hotkey = new ExplorerHotkey(services, show);
            hotkey._hotkey.Start();
            hotkey._hotkey.Apply(services.Preferences.Hotkey);
            services.Preferences.Changed += hotkey.OnPreferenceChanged;
            Current = hotkey;
            return hotkey;
        }
        catch (Exception ex)
        {
            WheelLog.Error("the hotkey could not start", ex);
            return null;
        }
    }

    private void OnPreferenceChanged(string key)
    {
        if (key == Settings.Preferences.HotkeyKey && !_disposed) _hotkey.Apply(_services.Preferences.Hotkey);
    }

    // Runs on the hotkey thread (an STA of its own): the shell calls may take as long as Explorer takes to answer.
    private void OnPressed()
    {
        var foreground = PInvoke.GetForegroundWindow();
        var result = ExplorerSelection.ReadForeground(foreground);
        WheelLog.Stage($"hotkey: {result.Status}, {result.Paths.Count} item(s)");
        if (result.Status != ExplorerSelectionStatus.Selected)
        {
            _services.Hud.Show(FeedbackCopy.SelectFilesFirst);
            return;
        }
        _show(result.Paths, AnchorFor(result.Window));
    }

    /// <summary>Where the wheel goes: under the pointer if it is over the Explorer window, else in the middle of that window (clamped on screen by the wheel).</summary>
    internal static PointPx? AnchorFor(ScreenRect window)
    {
        if (window.Width <= 0 || window.Height <= 0) return null;
        if (PInvoke.GetCursorPos(out var cursor) && window.Contains(new PointPx(cursor.X, cursor.Y))) return new PointPx(cursor.X, cursor.Y);
        return window.Centre;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _services.Preferences.Changed -= OnPreferenceChanged; } catch (Exception) { }
        if (ReferenceEquals(Current, this)) Current = null;
        _hotkey.Dispose();
    }
}
