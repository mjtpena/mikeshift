using System.Text.Json;

namespace MikeShift.App.Settings;

/// <summary>Which input switches the wheel to the tools page (PLAN §1.1). Default <see cref="Ctrl"/>.</summary>
public enum ToolsKey
{
    /// <summary>A fresh Ctrl press (default; also a mouse-wheel tick).</summary>
    Ctrl,
    /// <summary>Only a mouse-wheel tick; no modifier key switches pages.</summary>
    MouseWheelOnly,
    /// <summary>Alt (the Windows input-language hotkey can interfere: Left Alt+Shift).</summary>
    Alt,
}

/// <summary>Where preference values live. Values are plain strings so both back ends agree.</summary>
public interface IPreferenceStore
{
    /// <summary>The stored value, or null if absent.</summary>
    string? Get(string key);

    /// <summary>Stores a value.</summary>
    void Set(string key, string value);
}

/// <summary>Packaged: <c>ApplicationData.Current.LocalSettings</c>.</summary>
internal sealed class LocalSettingsStore : IPreferenceStore
{
    public string? Get(string key) =>
        Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var v) ? v as string : null;

    public void Set(string key, string value) =>
        Windows.Storage.ApplicationData.Current.LocalSettings.Values[key] = value;
}

/// <summary>Unpackaged: <c>%LOCALAPPDATA%\MikeShift\settings.json</c> (flat string map); <c>MIKESHIFT_SETTINGS_PATH</c> points dev/test runs at another file.</summary>
internal sealed class JsonFileStore : IPreferenceStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string>? _cache;

    public JsonFileStore(string? path = null) =>
        _path = path ?? Environment.GetEnvironmentVariable("MIKESHIFT_SETTINGS_PATH") ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikeShift", "settings.json");

    public string? Get(string key)
    {
        lock (_gate) return Load().TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            var map = Load();
            map[key] = value;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(map));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (IOException) { /* a preference that cannot be saved is not worth crashing for */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(_path))
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return _cache ??= [];
    }
}

/// <summary>
/// The user's preferences (PLAN §3.3): <c>chime</c>, <c>showsToast</c>, <c>toolsKey</c>, <c>showInFolderAfter</c>, <c>launched</c>.
/// Packaged apps use <c>ApplicationData.LocalSettings</c>; unpackaged runs use a JSON file under
/// <c>%LOCALAPPDATA%\MikeShift</c>. Open at Login is NOT stored here: it lives in the OS (<see cref="StartupService"/>).
/// Thread-safe for reads and writes; <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class Preferences
{
    /// <summary>Key: play a sound when a job succeeds (bool, default false: Windows apps stay quiet unless asked).</summary>
    public const string ChimeKey = "chime";

    /// <summary>Key: show the HUD toast (bool, default true).</summary>
    public const string ShowsToastKey = "showsToast";

    /// <summary>Key: tools toggle (<see cref="ToolsKey"/> name, default "Ctrl").</summary>
    public const string ToolsKeyKey = "toolsKey";

    /// <summary>Key: open Explorer on the result after a conversion (bool, default false).</summary>
    public const string ShowInFolderAfterKey = "showInFolderAfter";

    /// <summary>Key: the welcome window has been shown once (bool, default false).</summary>
    public const string LaunchedKey = "launched";

    private readonly IPreferenceStore _store;

    /// <summary>Uses the packaged or unpackaged store automatically.</summary>
    public Preferences() : this(PackageInfo.IsPackaged ? new LocalSettingsStore() : new JsonFileStore()) { }

    /// <summary>Uses <paramref name="store"/> (tests, tools).</summary>
    public Preferences(IPreferenceStore store) => _store = store;

    /// <summary>Raised after any value changed; the argument is the key.</summary>
    public event Action<string>? Changed;

    /// <summary>Play a short sound when a job succeeds. Default false (UX review X-02).</summary>
    public bool Chime
    {
        get => GetBool(ChimeKey, false);
        set => SetBool(ChimeKey, value);
    }

    /// <summary>Show the HUD toast. Default true.</summary>
    public bool ShowsToast
    {
        get => GetBool(ShowsToastKey, true);
        set => SetBool(ShowsToastKey, value);
    }

    /// <summary>Open Explorer with the result selected after every successful conversion. Default false.</summary>
    public bool ShowInFolderAfter
    {
        get => GetBool(ShowInFolderAfterKey, false);
        set => SetBool(ShowInFolderAfterKey, value);
    }

    /// <summary>The key that switches the wheel to tools. Default <see cref="ToolsKey.Ctrl"/>.</summary>
    public ToolsKey ToolsKey
    {
        get => Enum.TryParse<ToolsKey>(_store.Get(ToolsKeyKey), ignoreCase: true, out var k) && Enum.IsDefined(k) ? k : ToolsKey.Ctrl;
        set
        {
            _store.Set(ToolsKeyKey, value.ToString());
            Changed?.Invoke(ToolsKeyKey);
        }
    }

    /// <summary>True once the welcome window was shown. Default false.</summary>
    public bool Launched
    {
        get => GetBool(LaunchedKey, false);
        set => SetBool(LaunchedKey, value);
    }

    /// <summary>A remembered editor window size (client DIPs), or null.</summary>
    public (int Width, int Height)? GetWindowSize(string name)
    {
        var parts = _store.Get("window." + name)?.Split('x');
        return parts is { Length: 2 } && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w is > 100 and < 10000 && h is > 100 and < 10000
            ? (w, h)
            : null;
    }

    /// <summary>Remembers an editor window size (client DIPs).</summary>
    public void SetWindowSize(string name, int width, int height) => _store.Set("window." + name, $"{width}x{height}");

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(_store.Get(key), out var b) ? b : fallback;

    private void SetBool(string key, bool value)
    {
        _store.Set(key, value ? "true" : "false");
        Changed?.Invoke(key);
    }
}
