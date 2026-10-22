using Microsoft.Win32;
using Shiftvert.Core;
using SkiaSharp;
using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Tests;

public class CapabilitiesTests
{
    [Fact]
    public void All_is_every_flag_true_and_None_is_every_flag_false()
    {
        var props = typeof(Capabilities).GetProperties().Where(p => p.PropertyType == typeof(bool)).ToList();
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
    public void WordCom_matches_the_registry()
    {
        using var key = Registry.ClassesRoot.OpenSubKey("Word.Application");
        Assert.Equal(key is not null, Capabilities.Probe().WordCom);
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
