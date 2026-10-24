using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;
using MikeShift.Core.Interaction;
using MikeShift.Core.Shell;

namespace MikeShift.Core.Tests.Shell;

/// <summary>
/// The Explorer menu and the conversion settings: the Resize line carries the chosen percentage (ranked by its stable id), a HEIC/AVIF photo Windows cannot
/// open offers the Store extension instead of formats, and the tray app resolves a picked line the same way the menu planned it.
/// </summary>
public class ContextMenuSettingsTests
{
    private static readonly Capabilities Full = Capabilities.All;

    private static ContextMenuPlan Plan(Capabilities caps, ConversionOptions? options, params string[] paths) =>
        ContextMenuPlanner.Build(paths, caps, null, options);

    private static string[] Labels(ContextMenuPlan plan) => plan.Entries.Select(e => e.Label).ToArray();

    // ------------------------------------------------------------------ the Resize line

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    public void The_resize_line_names_the_chosen_percentage_in_its_usual_place(int percent)
    {
        var plan = Plan(Full, new ConversionOptions { ResizePercent = percent }, @"C:\x\IMG_1.heic");

        Assert.Equal(["Convert to JPG", "Convert to PNG", "Convert to PDF", "Compress", $"Resize {percent}%", "Zip"], Labels(plan));
        Assert.Equal(["JPG", "PNG", "PDF", "Compress", $"Resize {percent}%", "Zip"], plan.Entries.Select(e => e.ActionTitle));
    }

    [Fact]
    public void Resize_is_ranked_by_its_id_so_every_percentage_sits_where_Resize_50_always_did()
    {
        var baseline = Labels(Plan(Full, null, @"C:\x\a.png"));
        Assert.Equal(["Convert to JPG", "Convert to PDF", "Compress", "Resize 50%", "Convert to WebP", "Zip"], baseline);

        foreach (var percent in new[] { 25, 75 })
        {
            var labels = Labels(Plan(Full, new ConversionOptions { ResizePercent = percent }, @"C:\x\a.png"));
            Assert.Equal(baseline.Select(l => l == "Resize 50%" ? $"Resize {percent}%" : l), labels);
        }
    }

    [Fact]
    public void Every_ranked_entry_names_an_id_that_exists()
    {
        // A typo in the ranking would silently push a line to the end of the list: each id must match a real action of its category.
        foreach (var (category, ids) in ContextMenuPlanner.Ranking)
        {
            var existing = ActionCatalog.ListFor(category).Select(a => a.Id).ToHashSet();
            foreach (var id in ids) Assert.Contains(id, existing);
        }
        Assert.Contains("Resize", ContextMenuPlanner.Ranking[Category.Image]);
        Assert.DoesNotContain("Resize 50%", ContextMenuPlanner.Ranking[Category.Image]); // never the literal title
    }

    [Fact]
    public void A_menu_built_for_25_percent_still_runs_when_the_setting_has_changed_meanwhile()
    {
        // The entry says "Resize 25%"; by the time it is picked the tray app's catalog may be named for 75 %: the stable id still finds it.
        var entry = Plan(Full, new ConversionOptions { ResizePercent = 25 }, @"C:\x\a.png").Entries.Single(e => e.Label.StartsWith("Resize"));
        var catalog = ActionCatalog.For([@"C:\x\a.png"], Full, _ => Category.Image, new ConversionOptions { ResizePercent = 75 });

        Assert.Equal("Resize 75%", ActionCatalog.Find(catalog, entry.ActionTitle)!.Title);
    }

    [Fact]
    public void Every_entry_of_a_default_menu_is_found_by_the_exact_catalog()
    {
        foreach (var file in new[] { "a.heic", "a.png", "a.pdf", "a.mp4", "a.mp3", "a.docx" })
        {
            var paths = new[] { @"C:\x\" + file };
            var plan = ContextMenuPlanner.Build(paths, Full);
            var catalog = ActionCatalog.For(paths, Full, _ => Kind.ByExtension(paths[0]));
            foreach (var entry in plan.Entries)
                Assert.NotNull(ActionCatalog.Find(catalog, entry.ActionTitle));
        }
    }

    // ------------------------------------------------------------------ HEIC / AVIF without their decoder

    private static readonly Capabilities NoHeif = Full with { HeifDecode = false };
    private static readonly Capabilities NoHevc = Full with { HevcDecode = false };
    private static readonly Capabilities NoAv1 = Full with { AvifDecode = false };

    [Theory]
    [InlineData(@"C:\x\IMG_1.heic")]
    [InlineData(@"C:\x\IMG_1.HEIF")]
    public void A_heic_without_the_heif_extension_offers_to_get_it_instead_of_formats(string path)
    {
        var plan = Plan(NoHeif, null, path);

        Assert.True(plan.Visible);
        Assert.False(plan.Capped);
        Assert.Equal(["Get the free HEIF extension", "Zip"], Labels(plan));
        Assert.Equal(["Get HEIF", "Zip"], plan.Entries.Select(e => e.ActionTitle));
    }

    [Fact]
    public void Several_heic_files_get_one_line_not_one_per_file()
    {
        var plan = Plan(NoHeif, null, @"C:\x\a.heic", @"C:\x\b.heic", @"C:\x\c.heif");
        Assert.Equal(["Get the free HEIF extension", "Zip"], Labels(plan));
    }

    [Fact]
    public void Without_the_hevc_extension_the_line_asks_for_that_one()
    {
        var plan = Plan(NoHevc, null, @"C:\x\a.heic");
        Assert.Equal(["Get the HEVC video extension", "Zip"], Labels(plan));
        Assert.Equal("Get HEVC", plan.Entries[0].ActionTitle);
    }

    [Fact]
    public void An_avif_without_the_av1_extension_offers_that_extension()
    {
        var plan = Plan(NoAv1, null, @"C:\x\a.avif");
        Assert.Equal(["Get the free AV1 extension", "Zip"], Labels(plan));
        Assert.Equal("Get AV1", plan.Entries[0].ActionTitle);

        Assert.Equal(["Convert to JPG", "Convert to PNG", "Convert to PDF", "Compress", "Resize 50%", "Zip"], Labels(Plan(NoAv1, null, @"C:\x\a.heic")));
    }

    [Fact]
    public void With_the_decoders_present_or_a_mix_of_files_the_menu_is_the_usual_one()
    {
        Assert.DoesNotContain("Get the free HEIF extension", Labels(Plan(Full, null, @"C:\x\a.heic")));
        Assert.Contains("Convert to PNG", Labels(Plan(Full, null, @"C:\x\a.heic")));

        // A HEIC next to a JPG: the JPG can be converted, so the ordinary image menu is right (the HEIC fails with its own message if picked).
        var mixed = Plan(NoHeif, null, @"C:\x\a.heic", @"C:\x\b.jpg");
        Assert.DoesNotContain("Get the free HEIF extension", Labels(mixed));
        Assert.Contains("Convert to PNG", Labels(mixed));

        // A folder in the selection: Zip only, as always.
        var withFolder = ContextMenuPlanner.Build([@"C:\x\a.heic", @"C:\x\Folder"], NoHeif, p => p.EndsWith("Folder") ? Category.Other : Kind.ByExtension(p));
        Assert.Equal(["Zip"], Labels(withFolder));

        // A PDF or a JPG never asks for an extension, whatever is missing.
        Assert.DoesNotContain("Get the free HEIF extension", Labels(Plan(Capabilities.None, null, @"C:\x\a.jpg")));
    }

    [Fact]
    public void Until_the_tray_app_has_written_its_capabilities_no_line_asks_for_an_extension()
    {
        // Capabilities.Quick() assumes no decoder; trusting that would tell every HEIC owner to install what they have.
        var plan = ContextMenuPlanner.Build([@"C:\x\a.heic"], Capabilities.None, null, null, capabilitiesKnown: false);
        Assert.DoesNotContain("Get the free HEIF extension", Labels(plan));
        Assert.Contains("Convert to PNG", Labels(plan));
    }

    [Fact]
    public void A_huge_selection_is_still_zip_only()
    {
        var many = Enumerable.Range(0, ContextMenuPlanner.LargeSelection + 1).Select(i => $@"C:\x\{i}.heic").ToArray();
        var plan = ContextMenuPlanner.Build(many, NoHeif);
        Assert.True(plan.Capped);
        Assert.Equal(["Zip"], plan.Entries.Select(e => e.ActionTitle));
    }

    // ------------------------------------------------------------------ the menu and the tray app agree

    [Theory]
    [InlineData("heic", false, true, true)]   // no HEIF extension
    [InlineData("heic", true, false, true)]   // no HEVC extension
    [InlineData("avif", true, true, false)]   // no AV1 extension
    public void The_line_the_menu_plans_is_the_tile_the_catalog_builds_and_the_page_the_tray_opens(string ext, bool heif, bool hevc, bool av1)
    {
        var caps = Full with { HeifDecode = heif, HevcDecode = hevc, AvifDecode = av1 };
        string[] paths = [$@"C:\x\a.{ext}", $@"C:\x\b.{ext}"];

        var entry = ContextMenuPlanner.Build(paths, caps).Entries[0];

        // The wheel's exact catalog for files Windows cannot open: its first tile is the same line, and it carries the Store page.
        var tile = ActionCatalog.For(paths, caps, _ => Category.Other)[0];
        Assert.Equal(entry.ActionTitle, tile.Title);
        Assert.Equal(entry.Label, tile.DropDescription);
        Assert.NotNull(tile.OpensUri);

        // The tray app resolves the picked title with its live capabilities, and finds the very page the tile opens.
        var prompt = CodecHelp.ResolvePick(entry.ActionTitle, paths, caps);
        Assert.NotNull(prompt);
        Assert.Equal(tile.OpensUri, prompt!.StoreUri);
        Assert.Contains("ms-windows-store://pdp/?ProductId=", prompt.StoreUri);
    }

    [Fact]
    public void The_tray_app_only_opens_the_store_when_the_extension_is_still_missing_and_the_title_is_a_get_line()
    {
        string[] paths = [@"C:\x\a.heic"];

        Assert.Equal(CodecHelp.Heif, CodecHelp.ResolvePick("Get HEIF", paths, NoHeif));
        Assert.Equal(CodecHelp.Heif, CodecHelp.ResolvePick(" get heif ", paths, NoHeif));

        // Installed since the menu was drawn: nothing to open, the caller resolves the title in the ordinary catalog.
        Assert.Null(CodecHelp.ResolvePick("Get HEIF", paths, Full));
        // A different extension is missing than the one the line named, or the files changed kind.
        Assert.Null(CodecHelp.ResolvePick("Get AV1", paths, NoHeif));
        Assert.Null(CodecHelp.ResolvePick("Get HEIF", [@"C:\x\a.jpg"], NoHeif));
        Assert.Null(CodecHelp.ResolvePick("Get HEIF", [@"C:\x\a.heic", @"C:\x\b.jpg"], NoHeif));
        // Not a get line at all.
        Assert.Null(CodecHelp.ResolvePick("JPG", paths, NoHeif));
        Assert.Null(CodecHelp.ResolvePick("", paths, NoHeif));
        Assert.Null(CodecHelp.ResolvePick(null, paths, NoHeif));
        Assert.Null(CodecHelp.ResolvePick("Get HEIF", [], NoHeif));
    }

    [Fact]
    public void The_extension_only_rule_and_the_catalog_rule_give_the_same_prompt_for_files_Windows_cannot_open()
    {
        foreach (var caps in new[] { NoHeif, NoHevc, NoAv1, Capabilities.None })
            foreach (var ext in new[] { "heic", "heif", "avif" })
            {
                string[] paths = [$@"C:\x\a.{ext}"];
                Assert.Equal(CodecHelp.ForSelection(paths, caps, _ => Category.Other), CodecHelp.ForExtensions(paths, caps));
            }
    }

    // ------------------------------------------------------------------ the request that opens the wheel

    private static ShellRequest RoundTrip(ShellRequest r)
    {
        using var ms = new MemoryStream();
        r.WriteTo(ms);
        ms.Position = 0;
        return ShellRequest.ReadFrom(ms);
    }

    [Fact]
    public void More_options_carries_the_pointer_position_to_the_tray_app()
    {
        var back = RoundTrip(new ShellRequest(ShellVerb.Wheel, "", [@"C:\a.png"], new PointPx(1920, 1080)));
        Assert.Equal(new PointPx(1920, 1080), back.Anchor);
        Assert.Equal(ShellVerb.Wheel, back.Verb);

        // Multi-monitor desktops have negative coordinates.
        Assert.Equal(new PointPx(-1500, -40), RoundTrip(new ShellRequest(ShellVerb.Wheel, "", [@"C:\a.png"], new PointPx(-1500, -40))).Anchor);
    }

    [Fact]
    public void A_request_without_a_position_has_none()
    {
        Assert.Null(RoundTrip(new ShellRequest(ShellVerb.Run, "JPG", [@"C:\a.png"])).Anchor);
        Assert.Null(RoundTrip(new ShellRequest(ShellVerb.Ping, "", [])).Anchor);
    }

    [Fact]
    public void A_request_of_the_previous_protocol_is_refused_not_misread()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x3148534Du); // "MSH1"
            w.Write((byte)ShellVerb.Run);
            w.Write("PNG");
            w.Write(0);
        }
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(ms));
    }

    [Fact]
    public void A_request_cut_off_inside_the_position_is_rejected()
    {
        using var ms = new MemoryStream();
        new ShellRequest(ShellVerb.Wheel, "", [@"C:\a.png"], new PointPx(5, 6)).WriteTo(ms);
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(new MemoryStream(ms.ToArray()[..^3])));
    }
}
