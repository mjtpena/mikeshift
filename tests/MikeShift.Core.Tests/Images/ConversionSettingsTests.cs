using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>
/// The user-facing conversion settings reaching the converters: quality, Compress strength, Resize percentage and where the
/// output goes. With the defaults every result is what it was before the settings existed.
/// </summary>
public class ConversionSettingsTests
{
    private static readonly Capabilities NoHeic = Capabilities.All with { HeifEncode = false };

    private static ActionContext Ctx(ConversionOptions? options = null) => new(NoHeic, options: options);

    private static ActionDef Tool(string title, ConversionOptions? options = null) =>
        ActionCatalog.Find(ActionCatalog.ListFor(Category.Image, options ?? ConversionOptions.Default), title)!;

    private static long Size(string path) => new FileInfo(path).Length;

    // ------------------------------------------------------------------ quality

    [Fact]
    public async Task Jpg_quality_changes_the_file_size_and_90_is_what_it_always_was()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.png", Fixtures.PhotoPng(900, 600));

        var low = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, new ConversionOptions { Quality = 50 }, default);
        var normal = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, ConversionOptions.Default, default);
        var high = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, new ConversionOptions { Quality = 100 }, default);
        var legacy = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, default);

        Assert.True(Size(low) < Size(normal), $"{Size(low)} < {Size(normal)}");
        Assert.True(Size(normal) < Size(high), $"{Size(normal)} < {Size(high)}");
        Assert.Equal(File.ReadAllBytes(normal), File.ReadAllBytes(legacy)); // no settings = the old quality, byte for byte
        Assert.Equal(File.ReadAllBytes(normal), File.ReadAllBytes(await ImageConverter.ToFormatAsync(
            input, ImageFormat.Jpg, new ConversionOptions { Quality = 90 }, default)));
    }

    [Fact]
    public async Task Webp_quality_changes_the_file_size()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.png", Fixtures.PhotoPng(900, 600));

        var low = await ImageConverter.ToFormatAsync(input, ImageFormat.Webp, new ConversionOptions { Quality = 50 }, default);
        var normal = await ImageConverter.ToFormatAsync(input, ImageFormat.Webp, ConversionOptions.Default, default);
        var high = await ImageConverter.ToFormatAsync(input, ImageFormat.Webp, new ConversionOptions { Quality = 100 }, default);

        Assert.True(Size(low) < Size(normal), $"{Size(low)} < {Size(normal)}");
        Assert.True(Size(normal) < Size(high), $"{Size(normal)} < {Size(high)}");
    }

    [SkipIfNoCapability(nameof(Capabilities.HeifEncode))]
    public async Task Heic_quality_changes_the_file_size()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.png", Fixtures.PhotoPng(600, 400));

        var low = await ImageConverter.ToFormatAsync(input, ImageFormat.Heic, new ConversionOptions { Quality = 50 }, default);
        var high = await ImageConverter.ToFormatAsync(input, ImageFormat.Heic, new ConversionOptions { Quality = 100 }, default);

        Assert.True(Size(low) < Size(high), $"{Size(low)} < {Size(high)}");
    }

    [Fact]
    public async Task Lossless_formats_ignore_the_quality()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.jpg", Fixtures.PhotoJpeg(300, 200));

        var a = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, new ConversionOptions { Quality = 50 }, default);
        var b = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, new ConversionOptions { Quality = 100 }, default);

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }

    [Fact]
    public async Task The_JPG_tile_passes_the_quality_from_the_context()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.png", Fixtures.PhotoPng(900, 600));
        var jpg = Tool("JPG");

        var low = await jpg.Run([input], Ctx(new ConversionOptions { Quality = 50 }), default);
        var high = await jpg.Run([input], Ctx(new ConversionOptions { Quality = 100 }), default);

        Assert.True(Size(Assert.Single(low.Outputs).Path) < Size(Assert.Single(high.Outputs).Path));
    }

    [Fact]
    public async Task Resize_and_crop_keep_the_chosen_jpeg_quality_too()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.jpg", Fixtures.PhotoJpeg(1000, 700, quality: 98));

        var low = await ImageConverter.ResizeAsync(input, 75, NoHeic, new ConversionOptions { Quality = 50 }, default);
        var high = await ImageConverter.ResizeAsync(input, 75, NoHeic, new ConversionOptions { Quality = 100 }, default);
        Assert.True(Size(low) < Size(high));

        var crop = new CropChoice(new RectPx(0, 0, 800, 600), 800, 600);
        var cropLow = await ImageConverter.CropAsync(input, crop, NoHeic, new ConversionOptions { Quality = 50 }, default);
        var cropHigh = await ImageConverter.CropAsync(input, crop, NoHeic, new ConversionOptions { Quality = 100 }, default);
        Assert.True(Size(cropLow) < Size(cropHigh));
    }

    // ------------------------------------------------------------------ compress

    [Fact]
    public async Task Compress_strengths_give_smaller_and_smaller_pictures_at_4000_3000_and_2000_px()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("wide.png", Fixtures.PhotoPng(4200, 700));

        var light = await ImageConverter.CompressAsync(input, NoHeic, new ConversionOptions { Compress = CompressStrength.Light }, default);
        var medium = await ImageConverter.CompressAsync(input, NoHeic, new ConversionOptions { Compress = CompressStrength.Medium }, default);
        var strong = await ImageConverter.CompressAsync(input, NoHeic, new ConversionOptions { Compress = CompressStrength.Strong }, default);

        Assert.Equal(4000, Decode(light).Width);
        Assert.Equal(3000, Decode(medium).Width);
        Assert.Equal(2000, Decode(strong).Width);
        Assert.True(Size(light) > Size(medium), $"{Size(light)} > {Size(medium)}");
        Assert.True(Size(medium) > Size(strong), $"{Size(medium)} > {Size(strong)}");

        // Medium is what Compress always did.
        var legacy = await ImageConverter.CompressAsync(input, NoHeic, default);
        Assert.Equal(File.ReadAllBytes(medium), File.ReadAllBytes(legacy));
    }

    [Fact]
    public async Task Compress_strength_changes_the_quality_even_when_the_picture_is_small()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.jpg", Fixtures.PhotoJpeg(1200, 800, quality: 98));

        var light = await ImageConverter.CompressAsync(input, NoHeic, new ConversionOptions { Compress = CompressStrength.Light }, default);
        var strong = await ImageConverter.CompressAsync(input, NoHeic, new ConversionOptions { Compress = CompressStrength.Strong }, default);

        Assert.Equal((1200, 800), (Decode(light).Width, Decode(light).Height)); // below every limit: not resized
        Assert.Equal((1200, 800), (Decode(strong).Width, Decode(strong).Height));
        Assert.True(Size(strong) < Size(light));
    }

    [Fact]
    public async Task The_Compress_tile_uses_the_strength_from_the_context()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("wide.png", Fixtures.PhotoPng(2600, 600));

        var result = await Tool("Compress").Run([input], Ctx(new ConversionOptions { Compress = CompressStrength.Strong }), default);

        Assert.Equal(2000, Decode(Assert.Single(result.Outputs).Path).Width);
    }

    // ------------------------------------------------------------------ resize

    [Theory]
    [InlineData(25, 100, 75)]
    [InlineData(50, 200, 150)]
    [InlineData(75, 300, 225)]
    public async Task Resize_percentages_are_exact_and_named_after_the_percentage(int percent, int width, int height)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(400, 300));

        var output = await ImageConverter.ResizeAsync(input, percent, NoHeic, ConversionOptions.Default, default);

        Assert.Equal(tmp.Combine($"pic {percent}%.png"), output);
        var back = Decode(output);
        Assert.Equal((width, height), (back.Width, back.Height));
    }

    [Theory]
    [InlineData(25, 25, 13)]
    [InlineData(50, 51, 26)]
    [InlineData(75, 76, 38)]
    public async Task Resize_rounds_half_up_to_whole_pixels(int percent, int width, int height)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("odd.png", Fixtures.PngRgb(101, 51));

        var back = Decode(await ImageConverter.ResizeAsync(input, percent, NoHeic, ConversionOptions.Default, default));

        Assert.Equal((width, height), (back.Width, back.Height));
    }

    [Fact]
    public void A_side_never_scales_below_one_pixel()
    {
        Assert.Equal(1, ImageConverter.ScaledSide(1, 25));
        Assert.Equal(1, ImageConverter.ScaledSide(2, 25));
        Assert.Equal(51, ImageConverter.ScaledSide(101, 50)); // as the old (width + 1) / 2
        for (var side = 1; side < 200; side++)
            Assert.Equal(Math.Max(1, (side + 1) / 2), ImageConverter.ScaledSide(side, 50));
    }

    [Fact]
    public async Task Resize_refuses_a_percentage_the_settings_do_not_offer()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(40, 30));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ImageConverter.ResizeAsync(input, 33, NoHeic, ConversionOptions.Default, default));
        Assert.Equal(["pic.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task The_old_half_size_entry_point_is_still_fifty_percent()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(400, 300));
        var output = await ImageConverter.ResizeHalfAsync(input, NoHeic, default);
        Assert.Equal(tmp.Combine("pic 50%.png"), output);
        Assert.Equal((200, 150), (Decode(output).Width, Decode(output).Height));
    }

    [Fact]
    public async Task The_catalog_tile_is_named_for_the_setting_and_resizes_to_it()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(400, 300));

        foreach (var percent in ConversionOptions.ResizeChoices)
        {
            var options = new ConversionOptions { ResizePercent = percent };
            var tool = Tool($"Resize {percent}%", options);
            Assert.Equal($"Resize {percent}%", tool.Title);
            Assert.Equal("Resize", tool.Id);

            var titles = ActionCatalog.For([@"C:\x\a.png"], NoHeic, _ => Category.Image, options).Select(a => a.Title).ToList();
            Assert.Contains($"Resize {percent}%", titles);
            Assert.Single(titles, t => t.StartsWith("Resize", StringComparison.Ordinal));

            var result = await tool.Run([input], Ctx(options), default);
            Assert.Equal(tmp.Combine($"pic {percent}%.png"), Assert.Single(result.Outputs).Path);
        }
    }

    [Fact]
    public void The_default_catalog_is_unchanged_and_resize_keeps_its_place_in_the_list()
    {
        var defaults = ActionCatalog.For([@"C:\x\a.png"], NoHeic, _ => Category.Image).Select(a => a.Title).ToList();
        Assert.Equal(["JPG", "TIFF", "GIF", "BMP", "WebP", "PDF", "Compress", "Resize 50%", "Crop", "Strip EXIF", "Zip"], defaults);

        var quarter = ActionCatalog.For([@"C:\x\a.png"], NoHeic, _ => Category.Image, new ConversionOptions { ResizePercent = 25 })
            .Select(a => a.Title).ToList();
        Assert.Equal(defaults.Select(t => t == "Resize 50%" ? "Resize 25%" : t), quarter);
        Assert.Same(ImageActions.All, ActionCatalog.ListFor(Category.Image));
    }

    [Fact]
    public void An_action_is_found_by_its_stable_name_whatever_the_percentage()
    {
        var list = ActionCatalog.ListFor(Category.Image, new ConversionOptions { ResizePercent = 75 });
        Assert.Equal("Resize 75%", ActionCatalog.Find(list, "Resize 75%")!.Title);
        Assert.Equal("Resize 75%", ActionCatalog.Find(list, "Resize 25%")!.Title); // a menu built before the setting changed
        Assert.Equal("Resize 75%", ActionCatalog.Find(list, "resize 50%")!.Title);
        Assert.Equal("Resize 75%", ActionCatalog.Find(list, "Resize")!.Title);
        Assert.Null(ActionCatalog.Find(list, "Crop 50%"));
        Assert.Null(ActionCatalog.Find(list, "Nothing 50%"));
        Assert.Equal("Compress", ActionCatalog.Find(list, "Compress")!.Title);

        Assert.Equal(25, ActionCatalog.ResizePercentIn("Resize 25%"));
        Assert.Equal(50, ActionCatalog.ResizePercentIn(" resize 50% "));
        Assert.Null(ActionCatalog.ResizePercentIn("Resize 30%"));
        Assert.Null(ActionCatalog.ResizePercentIn("Resize"));
        Assert.Null(ActionCatalog.ResizePercentIn("Compress"));
    }

    // ------------------------------------------------------------------ where converted files go

    [Fact]
    public async Task Converted_files_go_to_the_chosen_folder_and_the_originals_folder_stays_as_it_was()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("converted");
        var input = tmp.WriteFile(@"source\photo.png", Fixtures.PngRgb());
        using var guard = OriginalsGuard.Watch(input);

        var result = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = dest }), default);

        var output = Assert.Single(result.Outputs);
        Assert.Equal(Path.Combine(dest, "photo.jpg"), output.Path);
        Assert.False(output.UsedDownloadsFallback);
        Assert.False(result.AnyDownloadsFallback);
        Assert.Equal(dest, result.ChosenFolder);
        Assert.Equal(["photo.png"], FileNames(tmp.Combine("source")));
        Assert.Equal(["photo.jpg"], FileNames(dest));
        Assert.Null(Naming.OutputFolder); // the run left no trace in the flow that called it
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Same_named_inputs_from_different_folders_get_their_own_names_in_the_chosen_folder()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("out");
        var a = tmp.WriteFile(@"one\photo.png", Fixtures.PngRgb());
        var b = tmp.WriteFile(@"two\photo.png", Fixtures.PngRgb());

        var result = await Tool("JPG").Run([a, b], Ctx(new ConversionOptions { OutputFolder = dest }), default);

        Assert.Equal(["photo 2.jpg", "photo.jpg"], FileNames(dest).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, result.Outputs.Count);
        Assert.False(result.AnyDownloadsFallback);
    }

    [Fact]
    public async Task Without_a_chosen_folder_outputs_stay_beside_the_original_exactly_as_before()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile(@"source\photo.png", Fixtures.PngRgb());

        var result = await Tool("JPG").Run([input], Ctx(), default);

        Assert.Equal(tmp.Combine("source", "photo.jpg"), Assert.Single(result.Outputs).Path);
        Assert.Null(result.ChosenFolder);
    }

    [Fact]
    public async Task Whole_selection_actions_honour_the_chosen_folder_too()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("out");
        var a = tmp.WriteFile(@"src\a.png", Fixtures.PngRgb());
        var b = tmp.WriteFile(@"src\b.png", Fixtures.PngRgb());
        var ctx = Ctx(new ConversionOptions { OutputFolder = dest });

        var merged = await Tool("Merge PDF").Run([a, b], ctx, default);
        var zipped = await ZipAction.Def.Run([a, b], ctx, default);
        var zipOne = await ZipAction.Def.Run([a], ctx, default);

        Assert.Equal(Path.Combine(dest, "Merged.pdf"), Assert.Single(merged.Outputs).Path);
        Assert.Equal(Path.Combine(dest, "Archive.zip"), Assert.Single(zipped.Outputs).Path);
        Assert.Equal(Path.Combine(dest, "a.zip"), Assert.Single(zipOne.Outputs).Path);
        Assert.False(merged.AnyDownloadsFallback || zipped.AnyDownloadsFallback || zipOne.AnyDownloadsFallback);
        Assert.Equal(["a.png", "b.png"], FileNames(tmp.Combine("src")));
    }

    [Fact]
    public async Task A_chosen_folder_that_is_gone_sends_the_file_to_downloads_without_recreating_it()
    {
        using var tmp = new TempDir();
        var gone = tmp.Combine("unplugged", "photos");
        var input = tmp.WriteFile("photo.png", Fixtures.PngRgb());

        var result = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = gone }), default);

        var output = Assert.Single(result.Outputs);
        Assert.Equal(Path.Combine(tmp.Downloads, "photo.jpg"), output.Path);
        Assert.True(output.UsedDownloadsFallback);
        Assert.True(result.AnyDownloadsFallback);
        Assert.False(Directory.Exists(gone));
        Assert.Equal(["photo.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task A_chosen_folder_that_cannot_be_written_sends_the_file_to_downloads()
    {
        using var tmp = new TempDir();
        var locked = tmp.CreateDir("locked");
        var input = tmp.WriteFile("photo.png", Fixtures.PngRgb());

        ActionResult result;
        using (TempDir.DenyWrites(locked))
            result = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = locked }), default);

        Assert.Equal(Path.Combine(tmp.Downloads, "photo.jpg"), Assert.Single(result.Outputs).Path);
        Assert.True(result.AnyDownloadsFallback);
        Assert.Equal(locked, result.ChosenFolder);
    }

    [Fact]
    public async Task The_toast_says_where_the_files_went_and_why_when_it_was_not_the_chosen_folder()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("Converted");
        var input = tmp.WriteFile("photo.png", Fixtures.PngRgb());

        var ok = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = dest }), default);
        Assert.Equal("In Converted", FeedbackPlan.ToastFor(ok).Detail);
        Assert.Equal(ToastKind.Success, FeedbackPlan.ToastFor(ok).Kind);

        var gone = tmp.Combine("Vanished");
        var fallback = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = gone }), default);
        var toast = FeedbackPlan.ToastFor(fallback);
        Assert.Equal("In Downloads (Vanished isn't available)", toast.Detail);
        Assert.Equal(ToastKind.Warning, toast.Kind);

        // Without a chosen folder the wording is the one it always was.
        var plain = await Tool("JPG").Run([input], Ctx(), default);
        Assert.Equal("Next to the original", FeedbackPlan.ToastFor(plain).Detail);
    }

    [Fact]
    public async Task A_cancelled_run_still_restores_the_flow()
    {
        using var tmp = new TempDir();
        var dest = tmp.CreateDir("out");
        var input = tmp.WriteFile("photo.png", Fixtures.PngRgb());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Tool("JPG").Run([input], Ctx(new ConversionOptions { OutputFolder = dest }), cts.Token);

        Assert.True(result.Cancelled);
        Assert.Null(Naming.OutputFolder);
        Assert.Empty(FileNames(dest));
    }

    [Fact]
    public void Wheel_screen_reader_and_menu_wording_agree_on_the_percentage()
    {
        var quarter = Tool("Resize 25%", new ConversionOptions { ResizePercent = 25 });
        Assert.Equal("Resize 25%", MikeShift.Core.Interaction.WheelText.AutomationName(quarter));
        Assert.Equal("Resize 25%", quarter.DropDescription);
        Assert.Equal("Resize 25%", MikeShift.Core.Shell.ContextMenuPlanner.LabelFor(quarter));
        Assert.Equal("Save a copy at 25% of the width and height", MikeShift.Core.Interaction.WheelText.Describe(quarter));
        Assert.Equal("Save a copy at half the width and height", MikeShift.Core.Interaction.WheelText.Describe(Tool("Resize 50%")));
        Assert.Equal("Save a copy at 75% of the width and height", MikeShift.Core.Interaction.WheelText.Describe(Tool("Resize 75%", new ConversionOptions { ResizePercent = 75 })));
    }

    [Fact]
    public void The_resize_toast_and_description_name_the_percentage()
    {
        Assert.Equal("Resizing to 25%…", FeedbackPlan.StartToast(Tool("Resize 25%", new ConversionOptions { ResizePercent = 25 }), 0).Title);
        Assert.Equal("Resizing to 50%…", FeedbackPlan.StartToast(Tool("Resize 50%"), 0).Title);
        Assert.Equal("Resizing 2 of 3…", FeedbackPlan.ProgressToast(Tool("Resize 75%", new ConversionOptions { ResizePercent = 75 }), "x", 2, 3, 0).Title);
    }
}
