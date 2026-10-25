using MikeShift.App;
using MikeShift.App.Settings;

namespace MikeShift.Core.Tests.Hosting;

/// <summary>
/// UI-free files of the tray app (<c>Preferences.cs</c>, <c>DevSwitches.cs</c>, <c>AppData.cs</c>, <c>PackageInfo.cs</c>) are compiled into this
/// test assembly (see the csproj), so the settings store and the dev-switch gate run for real.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Dev_environment_switches_are_read_only_when_dev_switches_are_enabled()
    {
        // Debug builds honour them; a Release test run is neither packaged nor started with --dev, so they are ignored.
        const string name = "MIKESHIFT_TEST_GATE_PROBE";
        Environment.SetEnvironmentVariable(name, "1");
        try
        {
            Assert.Equal(DevSwitches.Enabled ? "1" : null, DevSwitches.Env(name));
            if (!DevSwitches.Enabled) Assert.Empty(DevSwitches.Args.Skip(1)); // no dev flag can be found
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void The_json_store_round_trips_and_reports_that_it_saved()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("settings.json");
        var store = new JsonFileStore(path);
        Assert.Null(store.Get("chime"));
        Assert.True(store.Set("chime", "true"));
        Assert.Equal("true", new JsonFileStore(path).Get("chime"));
    }

    [Fact]
    public void Saving_a_key_keeps_keys_another_instance_saved_in_the_meantime()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("settings.json");
        var first = new JsonFileStore(path);
        var second = new JsonFileStore(path);
        Assert.Null(first.Get("chime"));          // first has read (an empty file) at start-up
        Assert.True(second.Set("toolsKey", "Alt")); // a second process saves a key afterwards
        Assert.True(first.Set("chime", "true"));    // first saves a different key: it must not drop "toolsKey"

        var fresh = new JsonFileStore(path);
        Assert.Equal("Alt", fresh.Get("toolsKey"));
        Assert.Equal("true", fresh.Get("chime"));
        Assert.Equal("Alt", first.Get("toolsKey")); // and the first instance now sees it too
    }

    [Fact]
    public void A_damaged_settings_file_is_replaced_not_fatal()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("settings.json", "{ not json");
        var store = new JsonFileStore(path);
        Assert.Null(store.Get("chime"));
        Assert.True(store.Set("chime", "true"));
        Assert.Equal("true", new JsonFileStore(path).Get("chime"));
    }

    [Fact]
    public void A_value_that_cannot_be_saved_is_kept_for_this_run_and_reported()
    {
        using var tmp = new TempDir();
        // The settings "file" is a directory, so writing it fails.
        var path = tmp.CreateDir("settings.json");
        var store = new JsonFileStore(path);
        Assert.False(store.Set("launched", "true"));
        Assert.Equal("true", store.Get("launched"));
    }

    [Fact]
    public void Marking_the_welcome_window_as_shown_says_whether_it_was_saved()
    {
        using var tmp = new TempDir();
        var good = new Preferences(new JsonFileStore(tmp.Combine("ok.json")));
        Assert.False(good.Launched);
        Assert.True(good.TryMarkLaunched());
        Assert.True(good.Launched);

        var bad = new Preferences(new JsonFileStore(tmp.CreateDir("broken.json")));
        Assert.False(bad.TryMarkLaunched());
        Assert.True(bad.Launched); // still true for this run
    }

    [Fact]
    public void The_data_folder_is_the_plain_local_app_data_folder_when_unpackaged()
    {
        Assert.False(PackageInfo.IsPackaged);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikeShift"), AppData.Folder);
        Assert.Equal(Path.Combine(AppData.Folder, "log.txt"), AppData.FilePath("log.txt"));
    }
}
