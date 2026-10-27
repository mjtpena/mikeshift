using MikeShift.Core;
using MikeShift.Core.Shell;

namespace MikeShift.Core.Tests.Shell;

public class ContextMenuPlanTests
{
    private static readonly Capabilities Full = Capabilities.All;

    private static ContextMenuPlan Plan(Capabilities caps, params string[] paths) => ContextMenuPlanner.Build(paths, caps);
    private static string[] Labels(Capabilities caps, params string[] paths) => Plan(caps, paths).Entries.Select(e => e.Label).ToArray();

    [Fact]
    public void Texts_are_the_ones_the_docs_promise()
    {
        Assert.Equal("Convert with MikeShift", ContextMenuPlanner.TopLevelTitle);
        Assert.Equal("More options\u2026", ContextMenuPlanner.MoreOptionsLabel);
    }

    [Fact]
    public void Nothing_selected_hides_the_command()
    {
        var plan = ContextMenuPlanner.Build([], Full);
        Assert.False(plan.Visible);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void A_heic_offers_the_popular_formats_first_then_tools_then_zip()
    {
        Assert.Equal(["Convert to JPG", "Convert to PNG", "Convert to PDF", "Compress", "Resize 50%", "Zip"], Labels(Full, @"C:\x\IMG_1.heic"));
    }

    [Fact]
    public void The_source_format_is_not_offered_to_itself()
    {
        var labels = Labels(Full, @"C:\x\a.png");
        Assert.DoesNotContain("Convert to PNG", labels);
        Assert.Equal(["Convert to JPG", "Convert to PDF", "Compress", "Resize 50%", "Convert to WebP", "Zip"], labels);
        Assert.DoesNotContain("Convert to JPG", Labels(Full, @"C:\x\a.jpeg"));
    }

    [Fact]
    public void Several_images_offer_Merge_PDF_in_the_top_entries()
    {
        var plan = Plan(Full, @"C:\x\a.jpg", @"C:\x\b.png", @"C:\x\c.heic");
        Assert.Equal(["Convert to JPG", "Convert to PNG", "Merge PDF", "Convert to PDF", "Compress", "Zip"], plan.Entries.Select(e => e.Label));
        Assert.Contains(plan.Entries, e => e.ActionTitle == "Merge PDF");
        Assert.DoesNotContain("Merge PDF", Labels(Full, @"C:\x\a.jpg"));
    }

    [Fact]
    public void Entries_name_the_catalog_action_they_run()
    {
        var plan = Plan(Full, @"C:\x\a.heic");
        Assert.Equal(["JPG", "PNG", "PDF", "Compress", "Resize 50%", "Zip"], plan.Entries.Select(e => e.ActionTitle));
    }

    [Fact]
    public void The_menu_never_lists_more_than_five_actions_plus_zip_and_zip_is_last()
    {
        foreach (var file in new[] { "a.jpg", "a.pdf", "a.mp4", "a.mp3", "a.docx", "a.webp" })
        {
            var entries = Plan(Full, @"C:\x\" + file, @"C:\x\b" + Path.GetExtension(file)).Entries;
            Assert.True(entries.Count <= ContextMenuPlanner.MaxRankedActions + 1, file);
            Assert.Equal("Zip", entries[^1].ActionTitle);
            Assert.Single(entries, e => e.ActionTitle == "Zip");
        }
    }

    [Fact]
    public void Pdfs_Videos_Audio_and_Documents_get_their_own_lists()
    {
        Assert.Equal(["Compress", "Convert to PNG", "Convert to JPG", "Split pages", "Convert to TXT", "Zip"], Labels(Full, @"C:\x\a.pdf"));
        Assert.Equal(["Merge PDFs", "Compress", "Convert to PNG", "Convert to JPG", "Split pages", "Zip"], Labels(Full, @"C:\x\a.pdf", @"C:\x\b.pdf"));
        Assert.Equal(["Convert to MP3", "Convert to GIF", "Compress", "Fit 10 MB", "Convert to WMV", "Zip"], Labels(Full, @"C:\x\a.mp4"));
        Assert.Equal(["MP4", "MP3", "GIF", "Compress", "Fit 10 MB", "Zip"], Plan(Full, @"C:\x\a.mov").Entries.Select(e => e.ActionTitle));
        Assert.Equal(["Convert to M4A", "Convert to WAV", "Compress", "Convert to FLAC", "Convert to AIFF", "Zip"], Labels(Full, @"C:\x\a.mp3"));
        Assert.Equal(["Convert to PDF", "Convert to DOCX", "Convert to TXT", "Convert to HTML", "Convert to RTF", "Zip"], Labels(Full, @"C:\x\a.odt"));
    }

    [Fact]
    public void Missing_codecs_remove_entries_instead_of_offering_what_would_fail()
    {
        var noHeif = Full with { HeifEncode = false };
        Assert.DoesNotContain("Convert to HEIC", Labels(noHeif, @"C:\x\a.jpg", @"C:\x\b.jpg"));
        // No H.264/AAC encoder: video Compress and MP4 disappear, the rest stays.
        var noEncoders = Full with { H264Encode = false, AacEncode = false };
        var video = Labels(noEncoders, @"C:\x\a.avi");
        Assert.DoesNotContain("Compress", video);
        Assert.DoesNotContain("Convert to MP4", video);
        Assert.DoesNotContain("Fit 10 MB", video);
        Assert.Contains("Convert to MP3", video);
        // Nothing optional at all (a fresh PC before the probe): still something useful, never an empty submenu.
        Assert.NotEmpty(Plan(Capabilities.None, @"C:\x\a.heic").Entries);
    }

    [Fact]
    public void A_folder_a_mixed_selection_and_unknown_files_offer_zip_only()
    {
        static Category Kinds(string p) => p.EndsWith("folder", StringComparison.Ordinal) ? Category.Other : Kind.ByExtension(p);
        Assert.Equal(["Zip"], ContextMenuPlanner.Build([@"C:\x\folder"], Full, Kinds).Entries.Select(e => e.Label));
        Assert.Equal(["Zip"], ContextMenuPlanner.Build([@"C:\x\folder", @"C:\x\a.jpg"], Full, Kinds).Entries.Select(e => e.Label));
        Assert.Equal(["Zip"], Labels(Full, @"C:\x\a.jpg", @"C:\x\b.pdf"));
        Assert.Equal(["Zip"], Labels(Full, @"C:\x\a.xyz"));
        Assert.True(ContextMenuPlanner.Build([@"C:\x\folder"], Full, Kinds).Visible);
    }

    [Fact]
    public void A_selection_over_the_cap_is_not_looked_at()
    {
        var many = Enumerable.Range(0, ContextMenuPlanner.LargeSelection + 1).Select(i => $@"C:\x\p{i}.jpg").ToArray();
        var plan = ContextMenuPlanner.Build(many, Full, _ => throw new InvalidOperationException("must not classify a capped selection"));
        Assert.True(plan.Capped);
        Assert.Equal(["Zip"], plan.Entries.Select(e => e.Label));

        var atCap = many.Take(ContextMenuPlanner.LargeSelection).ToArray();
        var normal = ContextMenuPlanner.Build(atCap, Full);
        Assert.False(normal.Capped);
        Assert.Contains(normal.Entries, e => e.Label == "Convert to PNG");
    }

    [Fact]
    public void Classifying_by_extension_never_touches_the_disk()
    {
        // These files do not exist; the plan is built from the names alone.
        Assert.Equal(Category.Image, Kind.ByExtension(@"Z:\does\not\exist.HEIC"));
        Assert.Equal(Category.Pdf, Kind.ByExtension(@"Z:\nope.pdf"));
        Assert.Equal(Category.Other, Kind.ByExtension(@"Z:\nope.ts"));
        Assert.Equal(Category.Other, Kind.ByExtension(@"Z:\no-extension"));
    }

    [Fact]
    public void Legacy_doc_without_Word_offers_zip_only_and_with_Word_offers_documents()
    {
        Assert.Equal(["Zip"], Labels(Full with { WordCom = false }, @"C:\x\old.doc"));
        Assert.Contains("Convert to PDF", Labels(Full, @"C:\x\old.doc"));
    }

    [Fact]
    public void Planning_a_document_selection_does_not_open_the_files()
    {
        // ActionCatalog's Document probe would open each file; the menu plan must not (Explorer's UI thread builds it).
        var plan = Plan(Full, @"Z:\unreachable\a.docx", @"Z:\unreachable\b.docx");
        Assert.Contains("Convert to PDF", plan.Entries.Select(e => e.Label));
    }
}
