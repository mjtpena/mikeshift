using MikeShift.Core;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests;

public class ActionCatalogTests
{
    private static readonly Capabilities Full = Capabilities.All;

    private static IReadOnlyList<ActionDef> For(Capabilities caps, params string[] paths) =>
        ActionCatalog.For(paths, caps, KindByExtension);

    private static string[] Titles(Capabilities caps, params string[] paths) =>
        For(caps, paths).Select(a => a.Title).ToArray();

    /// <summary>Classifies purely by extension so tests need no files on disk.</summary>
    private static Category KindByExtension(string path)
    {
        var e = Kind.ExtensionOf(path);
        if (e == "") return Category.Other;
        if (e == "pdf") return Category.Pdf;
        if (Kind.ImageExtensions.Contains(e)) return Category.Image;
        if (Kind.VideoExtensions.Contains(e)) return Category.Video;
        if (Kind.AudioExtensions.Contains(e)) return Category.Audio;
        if (Kind.DocumentExtensions.Contains(e)) return Category.Document;
        return Category.Other;
    }

    [Fact]
    public void Image_list_and_order()
    {
        // A single .ico: nothing is hidden by extension and there is no Merge PDF.
        Assert.Equal(
            ["PNG", "JPG", "TIFF", "GIF", "BMP", "HEIC", "WebP", "PDF", "Compress", "Resize 50%", "Crop", "Strip EXIF", "Zip"],
            Titles(Full, @"C:\x\a.ico"));
    }

    [Fact]
    public void Image_pages_and_glyphs_are_assigned()
    {
        var all = For(Full, @"C:\x\a.ico", @"C:\x\b.avif");
        var formats = all.Where(a => a.Page == WheelPage.Format).Select(a => a.Title).ToArray();
        var tools = all.Where(a => a.Page == WheelPage.Tool).Select(a => a.Title).ToArray();
        Assert.Equal(["PNG", "JPG", "TIFF", "GIF", "BMP", "HEIC", "WebP", "PDF"], formats);
        Assert.Equal(["Compress", "Resize 50%", "Crop", "Strip EXIF", "Merge PDF", "Zip"], tools);
        Assert.All(all.Where(a => a.Page == WheelPage.Tool), a => Assert.False(string.IsNullOrEmpty(a.Glyph)));
        Assert.All(all.Where(a => a.Page == WheelPage.Format), a => Assert.Null(a.Glyph));
        Assert.Single(all.Where(a => a.Interactive), a => a.Title == "Crop");
    }

    [Fact]
    public void Pdf_list_and_order()
    {
        Assert.Equal(["PNG", "JPG", "TXT", "Compress", "Split", "Zip"], Titles(Full, @"C:\x\a.pdf"));
        Assert.Equal(["PNG", "JPG", "TXT", "Compress", "Split", "Merge", "Zip"], Titles(Full, @"C:\x\a.pdf", @"C:\x\b.pdf"));
    }

    [Fact]
    public void Video_list_and_order()
    {
        Assert.Equal(["MP4", "M4V", "WMV", "GIF", "M4A", "MP3", "Compress", "Trim", "Zip"], Titles(Full, @"C:\x\a.mov"));
        Assert.DoesNotContain("MOV", Titles(Full, @"C:\x\a.mp4"));
    }

    [Fact]
    public void Audio_list_and_order()
    {
        Assert.Equal(["M4A", "WAV", "AIFF", "FLAC", "MP3", "Compress", "Trim", "Zip"], Titles(Full, @"C:\x\a.wma"));
    }

    [Fact]
    public void Document_list_and_order()
    {
        Assert.Equal(["PDF", "DOCX", "RTF", "TXT", "HTML", "ODT", "Zip"], Titles(Full, @"C:\x\a.md"));
    }

    [Fact]
    public void Titles_are_unique_within_every_category_list()
    {
        foreach (var c in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
        {
            var titles = ActionCatalog.ListFor(c).Select(a => a.Title).ToList();
            Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain("Zip", titles); // Zip is appended by the catalog, never part of a category list
        }
    }

    [Fact]
    public void Mixed_categories_get_only_zip()
    {
        Assert.Equal(["Zip"], Titles(Full, @"C:\x\a.png", @"C:\x\b.pdf"));
        Assert.Equal(["Zip"], Titles(Full, @"C:\x\a.mp3", @"C:\x\b.mp4"));
        Assert.Equal(["Zip"], Titles(Full, @"C:\x\a.png", @"C:\x\b.png", @"C:\x\c.txt"));
    }

    [Fact]
    public void Unknown_files_and_folders_get_only_zip()
    {
        Assert.Equal(["Zip"], Titles(Full, @"C:\x\a.xyz"));
        Assert.Equal(["Zip"], Titles(Full, @"C:\x\a.png", @"C:\x\unknown.bin"));

        using var tmp = new TempDir();
        var folder = tmp.CreateDir("pics");
        tmp.WriteFile(Path.Combine("pics", "a.png"), Fixtures.Png());
        Assert.Equal(["Zip"], ActionCatalog.For([folder], Full).Select(a => a.Title).ToArray());
    }

    [Fact]
    public void Empty_selection_has_no_actions()
    {
        Assert.Empty(For(Full));
    }

    [Fact]
    public void Real_files_are_classified_through_Kind()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.Png());
        var pdf = tmp.WriteFile("b.pdf", Fixtures.PdfStub());
        Assert.Contains("Crop", ActionCatalog.For([png], Full).Select(a => a.Title));
        Assert.Contains("Split", ActionCatalog.For([pdf], Full).Select(a => a.Title));
        Assert.Equal(["Zip"], ActionCatalog.For([png, pdf], Full).Select(a => a.Title).ToArray());
    }

    [Fact]
    public void Zip_is_always_last()
    {
        foreach (var sel in new[]
                 {
                     new[] { @"C:\x\a.png" }, new[] { @"C:\x\a.png", @"C:\x\b.jpg" }, new[] { @"C:\x\a.pdf" },
                     new[] { @"C:\x\a.mp4" }, new[] { @"C:\x\a.mp3" }, new[] { @"C:\x\a.docx" }, new[] { @"C:\x\a.xyz" },
                 })
        {
            var list = For(Full, sel);
            Assert.Equal("Zip", list[^1].Title);
            Assert.Single(list, a => a.Title == "Zip");
        }
    }

    [Fact]
    public void Action_is_hidden_when_every_selected_extension_is_already_that_format()
    {
        Assert.DoesNotContain("PNG", Titles(Full, @"C:\x\a.png"));
        Assert.DoesNotContain("PNG", Titles(Full, @"C:\x\a.png", @"C:\x\b.PNG"));
        Assert.DoesNotContain("JPG", Titles(Full, @"C:\x\a.jpeg"));
        Assert.DoesNotContain("JPG", Titles(Full, @"C:\x\a.jpg", @"C:\x\b.jfif"));
        Assert.DoesNotContain("TIFF", Titles(Full, @"C:\x\a.tif"));
        Assert.DoesNotContain("HEIC", Titles(Full, @"C:\x\a.heif"));
        Assert.DoesNotContain("WAV", Titles(Full, @"C:\x\a.wav"));
        Assert.DoesNotContain("AIFF", Titles(Full, @"C:\x\a.aif"));
        Assert.DoesNotContain("DOCX", Titles(Full, @"C:\x\a.docx"));
        Assert.DoesNotContain("HTML", Titles(Full, @"C:\x\a.htm"));
    }

    [Fact]
    public void Action_stays_in_a_mixed_extension_selection_and_skips_the_matching_files()
    {
        var png = Titles(Full, @"C:\x\a.png", @"C:\x\b.jpg");
        Assert.Contains("PNG", png);
        Assert.Contains("JPG", png);
    }

    [Fact]
    public void Tools_are_never_hidden_by_extension()
    {
        Assert.Contains("Compress", Titles(Full, @"C:\x\a.png"));
        Assert.Contains("Compress", Titles(Full, @"C:\x\a.jpg"));
        Assert.Contains("Resize 50%", Titles(Full, @"C:\x\a.png"));
    }

    [Fact]
    public void Merge_pdf_for_images_needs_two_or_more_items()
    {
        Assert.DoesNotContain("Merge PDF", Titles(Full, @"C:\x\a.png"));
        Assert.Contains("Merge PDF", Titles(Full, @"C:\x\a.png", @"C:\x\b.png"));
        Assert.Contains("Merge PDF", Titles(Full, @"C:\x\a.png", @"C:\x\b.jpg", @"C:\x\c.gif"));
        // Per-image PDF stays available alongside the merge.
        Assert.Contains("PDF", Titles(Full, @"C:\x\a.png", @"C:\x\b.png"));
    }

    [Fact]
    public void Merge_for_pdfs_needs_two_or_more_items()
    {
        Assert.DoesNotContain("Merge", Titles(Full, @"C:\x\a.pdf"));
        Assert.Contains("Merge", Titles(Full, @"C:\x\a.pdf", @"C:\x\b.pdf"));
    }

    [Fact]
    public void Capability_dependent_actions_are_hidden_with_fake_capabilities()
    {
        var none = Capabilities.None;
        var img = Titles(none, @"C:\x\a.ico");
        Assert.DoesNotContain("HEIC", img);
        Assert.DoesNotContain("WebP", img);
        Assert.Contains("PNG", img);

        Assert.Contains("HEIC", Titles(none with { HeifEncode = true }, @"C:\x\a.ico"));
        Assert.Contains("WebP", Titles(none with { WebpEncode = true }, @"C:\x\a.ico"));
        Assert.DoesNotContain("HEIC", Titles(Full with { HeifEncode = false }, @"C:\x\a.ico"));
        Assert.DoesNotContain("WebP", Titles(Full with { WebpEncode = false }, @"C:\x\a.ico"));
    }

    [Fact]
    public void Audio_encoders_gate_their_tiles()
    {
        var baseline = Capabilities.None;
        var t = Titles(baseline, @"C:\x\a.wma");
        Assert.Equal(["WAV", "AIFF", "Trim", "Zip"], t); // no AAC/FLAC/MP3 encoder: only the self-written ones remain

        Assert.Contains("FLAC", Titles(baseline with { FlacEncode = true }, @"C:\x\a.wma"));
        Assert.DoesNotContain("FLAC", Titles(Full with { FlacEncode = false }, @"C:\x\a.wma"));
        Assert.Contains("MP3", Titles(baseline with { Mp3Encode = true }, @"C:\x\a.wma"));
        Assert.Contains("M4A", Titles(baseline with { AacEncode = true }, @"C:\x\a.wma"));
        Assert.Contains("Compress", Titles(baseline with { AacEncode = true }, @"C:\x\a.wma"));
    }

    [Fact]
    public void Video_encoders_gate_their_tiles()
    {
        var none = Capabilities.None;
        Assert.Equal(["WMV", "GIF", "Trim", "Zip"], Titles(none, @"C:\x\a.mov"));
        var h264 = none with { H264Encode = true, AacEncode = true };
        Assert.Contains("MP4", Titles(h264, @"C:\x\a.mov"));
        Assert.Contains("M4V", Titles(h264, @"C:\x\a.mov"));
        Assert.Contains("Compress", Titles(h264, @"C:\x\a.mov"));
        Assert.Contains("M4A", Titles(h264, @"C:\x\a.mov"));
        Assert.DoesNotContain("MP3", Titles(h264, @"C:\x\a.mov"));
        Assert.Contains("MP3", Titles(h264 with { Mp3Encode = true }, @"C:\x\a.mov"));
    }

    [Fact]
    public void Selection_predicate_receives_paths_and_capabilities()
    {
        var seen = (IReadOnlyList<string>?)null;
        Capabilities? seenCaps = null;
        var def = new ActionDef("Probe", WheelPage.Tool, (_, _, _) => Task.FromResult(new ActionResult()))
        {
            AppliesTo = (p, c) => { seen = p; seenCaps = c; return p.Count == 2; },
        };
        Assert.False(def.IsVisibleFor(["a"], Full));
        Assert.True(def.IsVisibleFor(["a", "b"], Full));
        Assert.Equal(["a", "b"], seen);
        Assert.Same(Full, seenCaps);
    }

    [Fact]
    public void Drop_description_reads_naturally()
    {
        Assert.Equal("Convert to PNG", ActionCatalog.Find(ActionCatalog.ListFor(Category.Image), "png")!.DropDescription);
        Assert.Equal("Compress", ActionCatalog.Find(ActionCatalog.ListFor(Category.Image), "Compress")!.DropDescription);
    }

    [Fact]
    public void Find_ignores_case_and_surrounding_space()
    {
        var list = ActionCatalog.ListFor(Category.Image);
        Assert.Equal("Resize 50%", ActionCatalog.Find(list, "  resize 50%  ")!.Title);
        Assert.Null(ActionCatalog.Find(list, "nonsense"));
    }
}
