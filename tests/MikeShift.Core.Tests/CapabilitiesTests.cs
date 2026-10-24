using Microsoft.Win32;
using MikeShift.Core;
using SkiaSharp;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests;

public class CapabilitiesTests
{
    [Fact]
    public void All_is_every_flag_true_and_None_is_every_flag_false()
    {
        var props = typeof(Capabilities).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Where(p => p.PropertyType == typeof(bool)).ToList();
        Assert.True(props.Count >= 12);
        Assert.All(props, p => Assert.True((bool)p.GetValue(Capabilities.All)!, p.Name));
        Assert.All(props, p => Assert.False((bool)p.GetValue(Capabilities.None)!, p.Name));
    }

    [Fact]
    public void Fakes_are_built_with_with_expressions_and_compare_by_value()
    {
        var fake = Capabilities.All with { HeifEncode = false };
        Assert.False(fake.HeifEncode);
        Assert.True(fake.WebpEncode);
        Assert.NotEqual(Capabilities.All, fake);
        Assert.Equal(fake, Capabilities.All with { HeifEncode = false });
    }

    [Fact]
    public void Probe_never_throws_and_Current_is_cached()
    {
        var probed = Capabilities.Probe();
        Assert.NotNull(probed);
        Assert.Same(Capabilities.Current, Capabilities.Current);
    }

    [Fact]
    public async Task TryGetCurrent_hands_out_a_quick_snapshot_without_waiting_for_a_running_probe()
    {
        using var probeMayFinish = new ManualResetEventSlim();
        using var probeStarted = new ManualResetEventSlim();
        var real = Capabilities.All with { HeifEncode = false };
        var cache = new ProbeCache(() =>
        {
            probeStarted.Set();
            Assert.True(probeMayFinish.Wait(TimeSpan.FromSeconds(30)));
            return real;
        }, Capabilities.Quick);

        Assert.False(cache.IsReady);
        var pending = cache.ValueAsync;                      // starts the slow probe on the pool
        Assert.True(probeStarted.Wait(TimeSpan.FromSeconds(10)));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var probed = cache.TryGet(out var early);            // the probe is stuck mid-way: must not wait for it
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), clock.Elapsed.ToString());
        Assert.False(probed);
        Assert.False(cache.IsReady);
        Assert.Equal(Capabilities.Quick(), early);
        Assert.False(pending.IsCompleted);

        probeMayFinish.Set();
        Assert.Same(real, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(cache.IsReady);
        Assert.True(cache.TryGet(out var late));
        Assert.Same(real, late);
        Assert.Same(real, cache.Value);
        Assert.Same(pending, cache.ValueAsync);              // one probe, one task
    }

    [Fact]
    public async Task A_cache_that_is_asked_synchronously_probes_once_and_then_reports_ready()
    {
        var runs = 0;
        var cache = new ProbeCache(() => { Interlocked.Increment(ref runs); return Capabilities.None; }, Capabilities.Quick);

        Assert.Same(cache.Value, cache.Value);
        Assert.True(cache.TryGet(out _));
        await cache.ValueAsync.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Quick_assumes_no_optional_codec_and_only_detects_Word()
    {
        var quick = Capabilities.Quick();
        using var key = Registry.ClassesRoot.OpenSubKey("Word.Application");
        Assert.Equal(key is not null, quick.WordInstalled);
        Assert.Equal(Capabilities.None with { WordInstalled = quick.WordInstalled, WordCom = quick.WordCom }, quick);
    }

    [Fact]
    public async Task The_process_wide_probe_becomes_available_through_every_accessor()
    {
        var probed = await Capabilities.CurrentAsync.WaitAsync(TimeSpan.FromMinutes(2));

        Assert.True(Capabilities.IsProbed);
        Assert.True(Capabilities.TryGetCurrent(out var snapshot));
        Assert.Same(probed, snapshot);
        Assert.Same(probed, Capabilities.Current);
    }

    [Fact]
    public void WordInstalled_matches_the_registry()
    {
        using var key = Registry.ClassesRoot.OpenSubKey("Word.Application");
        Assert.Equal(key is not null, Capabilities.Probe().WordInstalled);
    }

    // ------------------------------------------------------------------ Word is opt-in

    [Fact]
    public void Word_is_off_by_default_even_where_it_is_installed()
    {
        Assert.False(Capabilities.WordAllowedByDefault);
        Assert.False(Capabilities.WordAllowed);
        Assert.False(Capabilities.Current.WordCom);
        Assert.False(Capabilities.Quick().WordCom);
        Assert.False(Capabilities.Probe().WordCom);
        Assert.False(Capabilities.None.WordCom);
    }

    [Fact]
    public void WithWord_grants_Word_only_where_it_is_installed()
    {
        var installed = Capabilities.None with { WordInstalled = true };
        Assert.True(installed.WithWord(true).WordCom);
        Assert.False(installed.WithWord(false).WordCom);
        Assert.False((Capabilities.None.WithWord(true)).WordCom); // not installed: nothing to allow
        Assert.False((Capabilities.All.WithWord(false)).WordCom);
        Assert.True(Capabilities.All.WithWord(false).WordInstalled); // still known to be there
        Assert.Same(Capabilities.All, Capabilities.All.WithWord(true));
    }


    [Fact]
    public void Tool_glyphs_exist_in_the_installed_icon_font()
    {
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "SegoeIcons.ttf");
        if (!File.Exists(font)) font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segmdl2.ttf");
        if (!File.Exists(font)) return; // no icon font on this machine

        using var typeface = SKTypeface.FromFile(font);
        using var skFont = new SKFont(typeface);
        Assert.NotNull(typeface);
        foreach (var glyph in Glyphs.All)
            Assert.True(skFont.GetGlyphs(glyph)[0] != 0, $"U+{(int)glyph[0]:X4} missing from {Path.GetFileName(font)}");
    }

    [Fact]
    public void Every_tool_action_has_a_glyph_and_no_format_action_does()
    {
        foreach (var c in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
            foreach (var a in ActionCatalog.ListFor(c))
                Assert.Equal(a.Page == WheelPage.Tool, !string.IsNullOrEmpty(a.Glyph));
        Assert.False(string.IsNullOrEmpty(ZipAction.Def.Glyph));
    }
}
