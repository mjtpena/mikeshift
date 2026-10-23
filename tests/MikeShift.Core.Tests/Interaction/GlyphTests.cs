using MikeShift.Core.Actions;
using SkiaSharp;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>UX review W-06 (category glyphs on format tiles) and W-07 (glyph semantics): code points exist in Segoe Fluent Icons and mean what they should.</summary>
public class GlyphTests
{
    private static readonly string[] AllFormatTitles =
    [
        "PNG", "JPG", "TIFF", "GIF", "BMP", "HEIC", "WebP", "PDF", "TXT", "DOCX", "RTF", "HTML", "ODT",
        "M4A", "WAV", "AIFF", "FLAC", "MP3", "MP4", "M4V", "WMV",
    ];

    [Fact]
    public void EveryGlyphTheWheelDrawsExistsInTheInstalledIconFont()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var font = Path.Combine(fonts, "SegoeIcons.ttf");
        if (!File.Exists(font)) font = Path.Combine(fonts, "segmdl2.ttf");
        if (!File.Exists(font)) return; // no icon font on this machine

        using var typeface = SKTypeface.FromFile(font);
        using var skFont = new SKFont(typeface);
        Assert.NotNull(typeface);
        foreach (var glyph in Glyphs.Everything)
            Assert.True(skFont.GetGlyphs(glyph)[0] != 0, $"U+{(int)glyph[0]:X4} missing from {Path.GetFileName(font)}");
        foreach (var title in AllFormatTitles)
            Assert.True(skFont.GetGlyphs(Glyphs.ForFormat(title))[0] != 0, $"format {title}: U+{(int)Glyphs.ForFormat(title)[0]:X4} missing");
    }

    [Fact]
    public void EveryGlyphIsOneCharacterInThePrivateUseArea()
    {
        foreach (var glyph in Glyphs.Everything)
        {
            Assert.Single(glyph);
            Assert.InRange(glyph[0], '', '');
        }
    }

    [Fact]
    public void ToolGlyphsAreAllDifferent() => Assert.Equal(Glyphs.All.Count - 0, Glyphs.All.Distinct().Count());

    [Fact]
    public void ResizeIsInwardArrowsAndCompressIsADifferentMetaphor()
    {
        Assert.Equal("", Glyphs.Resize);        // BackToWindow: arrows pointing inward = shrink
        Assert.NotEqual(Glyphs.Resize, Glyphs.Compress);
        Assert.NotEqual("", Glyphs.Resize);     // FullScreen (outward arrows) means enlarge
        Assert.NotEqual("", Glyphs.Compress);
        Assert.NotEqual("", Glyphs.Compress);   // inward arrows are Resize's
        Assert.Equal("", Glyphs.Compress);      // Archive
    }

    [Fact]
    public void StripExifIsNeverAnX()
    {
        string[] closeLikeGlyphs = ["" /* Cancel */, "" /* Clear (the old X) */, "" /* ChromeClose */, "" /* ChromeCloseContrast */];
        Assert.DoesNotContain(Glyphs.StripExif, closeLikeGlyphs);
        Assert.DoesNotContain(Glyphs.Compress, closeLikeGlyphs);
        Assert.Equal("", Glyphs.StripExif);     // Hide: an eye with a slash
    }

    [Fact]
    public void NoToolGlyphIsAlsoACategoryGlyph()
    {
        var categories = new[] { Glyphs.Image, Glyphs.Document, Glyphs.Pdf, Glyphs.Video, Glyphs.Audio };
        Assert.Empty(Glyphs.All.Intersect(categories));
        Assert.Equal(categories.Length, categories.Distinct().Count());
    }

    [Theory]
    [InlineData("PNG")]
    [InlineData("JPG")]
    [InlineData("TIFF")]
    [InlineData("GIF")]
    [InlineData("BMP")]
    [InlineData("HEIC")]
    [InlineData("WebP")]
    [InlineData("webp")]
    public void PictureFormatsShowThePhotoGlyph(string title) => Assert.Equal(Glyphs.Image, Glyphs.ForFormat(title));

    [Theory]
    [InlineData("TXT")]
    [InlineData("DOCX")]
    [InlineData("RTF")]
    [InlineData("HTML")]
    [InlineData("ODT")]
    public void TextDocumentFormatsShowThePageGlyph(string title) => Assert.Equal(Glyphs.Document, Glyphs.ForFormat(title));

    [Fact]
    public void PdfVideoAndAudioHaveTheirOwn()
    {
        Assert.Equal(Glyphs.Pdf, Glyphs.ForFormat("PDF"));
        foreach (var t in new[] { "MP4", "M4V", "WMV" }) Assert.Equal(Glyphs.Video, Glyphs.ForFormat(t));
        foreach (var t in new[] { "MP3", "M4A", "WAV", "FLAC", "AIFF" }) Assert.Equal(Glyphs.Audio, Glyphs.ForFormat(t));
        Assert.Equal(Glyphs.Document, Glyphs.ForFormat("something-new"));
    }

    [Fact]
    public void EveryFormatTileOfTheRealCatalogGetsACategoryGlyph()
    {
        foreach (var category in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
            foreach (var action in ActionCatalog.ListFor(category).Where(a => a.Page == WheelPage.Format))
            {
                Assert.Contains(action.Title, AllFormatTitles);
                var glyph = Glyphs.ForFormat(action.Title);
                Assert.Contains(glyph, Glyphs.Everything);
                Assert.Null(action.Glyph);   // the glyph is the renderer's lookup, tool tiles carry theirs
            }
    }

    [Fact]
    public void EveryToolActionCarriesAGlyphFromTheToolSet()
    {
        foreach (var category in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
            foreach (var action in ActionCatalog.ListFor(category).Where(a => a.Page == WheelPage.Tool))
                Assert.Contains(action.Glyph, Glyphs.All);
        Assert.Equal(Glyphs.Zip, ZipAction.Def.Glyph);
    }
}
