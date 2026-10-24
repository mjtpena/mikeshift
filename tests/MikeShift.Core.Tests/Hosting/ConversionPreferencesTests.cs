using MikeShift.App.Settings;
using MikeShift.Core;

namespace MikeShift.Core.Tests.Hosting;

/// <summary>The conversion settings in <see cref="Preferences"/>: defaults, round trip through the settings file, damaged values, change notifications.</summary>
public class ConversionPreferencesTests
{
    private static Preferences Open(string path) => new(new JsonFileStore(path));

    [Fact]
    public void A_fresh_profile_has_exactly_the_default_conversion_settings()
    {
        using var tmp = new TempDir();
        var prefs = Open(tmp.Combine("settings.json"));

        Assert.Equal(90, prefs.Quality);
        Assert.Equal(CompressStrength.Medium, prefs.Compress);
        Assert.Equal(50, prefs.ResizePercent);
        Assert.Equal(15, prefs.GifSeconds);
        Assert.Null(prefs.OutputFolder);
        Assert.Equal(ConversionOptions.Default, prefs.Conversions);
        Assert.False(File.Exists(tmp.Combine("settings.json"))); // reading the defaults writes nothing
    }

    [Fact]
    public void Every_conversion_setting_survives_a_restart()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("settings.json");
        var folder = tmp.CreateDir("converted");

        var prefs = Open(path);
        prefs.Quality = 65;
        prefs.Compress = CompressStrength.Strong;
        prefs.ResizePercent = 75;
        prefs.GifSeconds = 30;
        prefs.OutputFolder = folder;

        var again = Open(path); // a new process reading the same file
        Assert.Equal(65, again.Quality);
        Assert.Equal(CompressStrength.Strong, again.Compress);
        Assert.Equal(75, again.ResizePercent);
        Assert.Equal(30, again.GifSeconds);
        Assert.Equal(folder, again.OutputFolder);
        Assert.Equal(
            new ConversionOptions { Quality = 65, Compress = CompressStrength.Strong, ResizePercent = 75, GifSeconds = 30, OutputFolder = folder },
            again.Conversions);

        again.OutputFolder = null; // back to "next to the original"
        Assert.Null(Open(path).OutputFolder);
    }

    [Fact]
    public void The_stored_keys_are_the_documented_plain_strings_and_old_files_without_them_still_load()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("settings.json");
        // A settings file written before the conversion settings existed: other keys only.
        File.WriteAllText(path, """{"chime":"true","armKey":"Ctrl"}""");

        var prefs = Open(path);
        Assert.True(prefs.Chime);
        Assert.Equal(ConversionOptions.Default, prefs.Conversions);

        prefs.Quality = 80;
        prefs.Compress = CompressStrength.Light;
        var text = File.ReadAllText(path);
        Assert.Contains("\"convQuality\": \"80\"", text.Replace("\":\"", "\": \""));
        Assert.Contains("\"convCompress\"", text);
        Assert.Contains("\"chime\"", text); // the other keys were kept
        Assert.Contains("\"armKey\"", text);
        Assert.True(Open(path).Chime);
    }

    [Fact]
    public void Damaged_stored_values_give_the_default_for_that_setting_only()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("settings.json");
        File.WriteAllText(path, """
            {"convQuality":"12","convCompress":"ultra","convResize":"33","convGifSeconds":"20","convOutputFolder":"relative\\path"}
            """);
        var prefs = Open(path);
        Assert.Equal(ConversionOptions.Default, prefs.Conversions);
        Assert.Equal(90, prefs.Quality);
        Assert.Equal(CompressStrength.Medium, prefs.Compress);
        Assert.Equal(50, prefs.ResizePercent);
        Assert.Equal(15, prefs.GifSeconds);
        Assert.Null(prefs.OutputFolder);

        File.WriteAllText(path, """{"convQuality":"70","convCompress":"strong","convResize":"banana"}""");
        var mixed = Open(path);
        Assert.Equal(70, mixed.Quality);
        Assert.Equal(CompressStrength.Strong, mixed.Compress);
        Assert.Equal(50, mixed.ResizePercent);
    }

    [Fact]
    public void Setting_a_value_outside_its_range_is_refused_and_changes_nothing()
    {
        using var tmp = new TempDir();
        var prefs = Open(tmp.Combine("settings.json"));
        var raised = new List<string>();
        prefs.Changed += raised.Add;

        Assert.Throws<ArgumentOutOfRangeException>(() => prefs.Quality = 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => prefs.ResizePercent = 60);
        Assert.Throws<ArgumentOutOfRangeException>(() => prefs.GifSeconds = 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => prefs.Compress = (CompressStrength)9);
        Assert.Throws<ArgumentException>(() => prefs.OutputFolder = @"not\absolute");

        Assert.Empty(raised);
        Assert.Equal(ConversionOptions.Default, prefs.Conversions);
    }

    [Fact]
    public void Changing_a_conversion_setting_raises_changed_with_its_key()
    {
        using var tmp = new TempDir();
        var prefs = Open(tmp.Combine("settings.json"));
        var raised = new List<string>();
        prefs.Changed += raised.Add;

        prefs.Quality = 70;
        prefs.Compress = CompressStrength.Light;
        prefs.ResizePercent = 25;
        prefs.GifSeconds = 5;
        prefs.OutputFolder = tmp.Path;

        Assert.Equal([Preferences.QualityKey, Preferences.CompressKey, Preferences.ResizeKey, Preferences.GifSecondsKey, Preferences.OutputFolderKey], raised);
        Assert.All(raised, key => Assert.True(Preferences.IsConversionKey(key)));
        Assert.False(Preferences.IsConversionKey(Preferences.UseWordKey));
        Assert.False(Preferences.IsConversionKey(Preferences.ChimeKey));
    }

    [Fact]
    public void The_settings_reach_a_job_through_conversions_as_one_consistent_snapshot()
    {
        using var tmp = new TempDir();
        var prefs = Open(tmp.Combine("settings.json"));
        prefs.ResizePercent = 25;
        var snapshot = prefs.Conversions;
        prefs.ResizePercent = 75; // a later change does not touch a snapshot a running job holds

        Assert.Equal(25, snapshot.ResizePercent);
        Assert.Equal(75, prefs.Conversions.ResizePercent);
    }
}
