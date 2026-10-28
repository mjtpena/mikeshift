using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using MikeShift.Core.Interaction;
using MikeShift.Core.Shell;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>
/// Upscale 2x. The Windows AI image scaler is replaced by a stand-in that doubles the pixels, so none of these need a Copilot+ PC; the one test
/// that calls the real scaler is skipped on a PC where <see cref="Capabilities.AiUpscale"/> is false.
/// </summary>
public class UpscaleTests
{
    private static readonly Capabilities Caps = Capabilities.All with { HeifEncode = false };

    /// <summary>Doubles every pixel in each direction and remembers what it was given.</summary>
    private sealed class Doubler : IUpscaleEngine
    {
        public List<Raster> Seen { get; } = [];
        public Func<Raster, Raster>? Override { get; init; }

        public Task<Raster> ScaleAsync(Raster source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Seen.Add(source);
            return Task.FromResult(Override?.Invoke(source) ?? Double(source));
        }
    }

    private static Raster Double(Raster s)
    {
        var w = s.Width * 2;
        var h = s.Height * 2;
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Buffer.BlockCopy(s.Bgra, ((y / 2) * s.Width + x / 2) * 4, bgra, (y * w + x) * 4, 4);
        return new Raster(w, h, bgra);
    }

    private sealed class Throwing(Exception ex) : IUpscaleEngine
    {
        public Task<Raster> ScaleAsync(Raster source, CancellationToken ct) => throw ex;
    }

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    // ------------------------------------------------------------------ the output

    [Fact]
    public async Task The_output_is_exactly_twice_the_width_and_height_named_2x_in_the_same_format()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("logo.png", Fixtures.PngRgb(64, 48));
        using var guard = OriginalsGuard.Watch(input);

        var path = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, new Doubler(), default);

        Assert.Equal(tmp.Combine("logo 2x.png"), path);
        Assert.Equal(ImageContainer.Png, ContainerOf(path));
        var back = Decode(path);
        Assert.Equal((128, 96), (back.Width, back.Height));
        Assert.True(Near(back, 10, 10, Red) && Near(back, 120, 10, Green) && Near(back, 10, 90, Blue) && Near(back, 120, 90, Yellow));
        Assert.Equal(["logo 2x.png", "logo.png"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Theory]
    [InlineData("photo.jpg", "photo 2x.jpg")]
    [InlineData("photo.jpeg", "photo 2x.jpeg")]
    [InlineData("scan.bmp", "scan 2x.bmp")]
    [InlineData("scan.tif", "scan 2x.tif")]
    public async Task Other_formats_keep_their_format_and_spelling(string name, string expected)
    {
        using var tmp = new TempDir();
        var bytes = Path.GetExtension(name) switch
        {
            ".bmp" => Fixtures.Bmp(40, 30),
            ".tif" => Fixtures.TiffMultiFrame(40, 30, 1),
            _ => Fixtures.QuadrantJpeg(40, 30),
        };
        var input = tmp.WriteFile(name, bytes);

        var path = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, new Doubler(), default);

        Assert.Equal(tmp.Combine(expected), path);
        Assert.Equal((80, 60), (Decode(path).Width, Decode(path).Height));
    }

    [Fact]
    public async Task Exif_orientation_is_applied_before_scaling_so_a_portrait_photo_stays_a_portrait()
    {
        using var tmp = new TempDir();
        // Stored 120x80, orientation 6: upright it is 80x120, and the engine must be given those pixels.
        var input = tmp.WriteFile("rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: false));
        var engine = new Doubler();

        var path = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, engine, default);

        Assert.Equal((80, 120), (engine.Seen.Single().Width, engine.Seen.Single().Height));
        var back = DecodeFull(path);
        Assert.Equal((160, 240), (back.Width, back.Height));
        Assert.True(Near(back.Pixels, 120, 60, Red, 40), $"top-right was {At(back.Pixels, 120, 60)}");
        Assert.Equal(1, JpegMeta.ReadOrientation(File.ReadAllBytes(path))); // upright pixels: the tag no longer turns them
    }

    [Fact]
    public async Task Transparency_is_kept_the_engine_sees_opaque_colour_and_the_alpha_is_enlarged_separately()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("sticker.png", Fixtures.PngAlpha(64, 48)); // top-left quadrant fully transparent
        var engine = new Doubler();

        var path = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, engine, default);

        Assert.All(Enumerable.Range(0, engine.Seen.Single().Bgra.Length / 4), i => Assert.Equal(255, engine.Seen.Single().Bgra[i * 4 + 3]));
        var back = Decode(path);
        Assert.Equal((128, 96), (back.Width, back.Height));
        Assert.Equal(0, At(back, 10, 10).A);     // inside the transparent quadrant
        Assert.Equal(255, At(back, 120, 10).A);  // opaque elsewhere
        Assert.True(Near(back, 120, 90, Yellow));
    }

    [Fact]
    public async Task A_jpeg_is_saved_at_the_quality_in_the_settings_and_keeps_its_camera_data()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("trip.jpg", Fixtures.JpegWithMetadata(80, 60, orientation: 1, gps: false));

        var high = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default with { Quality = 100 }, new Doubler(), default);
        var highBytes = File.ReadAllBytes(high);
        File.Delete(high);
        var low = await ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default with { Quality = 50 }, new Doubler(), default);

        Assert.True(new FileInfo(low).Length < highBytes.Length);
        Assert.NotNull(JpegMeta.ExtractExif(File.ReadAllBytes(low)));
    }

    // ------------------------------------------------------------------ the limits

    [Theory]
    [InlineData(10_000, 5_000, true)]    // 50 MP becomes exactly 200 MP
    [InlineData(10_001, 5_000, false)]
    [InlineData(8_000, 6_500, false)]    // 52 MP: allowed as a picture, not as a result
    [InlineData(15_000, 1, true)]        // 30,000 px on a side
    [InlineData(15_001, 1, false)]
    [InlineData(1, 15_001, false)]
    public void The_result_may_not_pass_200_megapixels_or_30000_pixels_on_a_side(int width, int height, bool allowed)
    {
        if (allowed) ImageConverter.RequireUpscalable(width, height);
        else Assert.Equal(UpscaleCopy.TooLarge(width, height), Assert.Throws<ConversionException>(() => ImageConverter.RequireUpscalable(width, height)).Message);
    }

    [Fact]
    public async Task A_picture_whose_result_would_be_too_large_is_refused_before_the_engine_runs_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(8_000, 6_500)); // 52 MP, so 208 MP
        var engine = new Doubler();
        using var guard = OriginalsGuard.Watch(input);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, engine, default));

        Assert.Equal("This picture is too large to upscale: 52 megapixels would become 208, and MikeShift handles up to 200.", ex.Message);
        Assert.Empty(engine.Seen);
        Assert.Equal(["huge.png"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Animated_gifs_and_multi_page_tiffs_are_refused_before_the_engine_runs()
    {
        using var tmp = new TempDir();
        var engine = new Doubler();
        var gif = tmp.WriteFile("loop.gif", Fixtures.Gif(64, 48, animated: true));
        var tiff = tmp.WriteFile("scan.tiff", Fixtures.TiffMultiFrame(64, 48, 3));

        var animated = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.UpscaleAsync(gif, Caps, ConversionOptions.Default, engine, default));
        var pages = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.UpscaleAsync(tiff, Caps, ConversionOptions.Default, engine, default));

        Assert.Contains("upscaled", animated.Message);
        Assert.Contains("3 pages", pages.Message);
        Assert.Empty(engine.Seen);
        Assert.Equal(["loop.gif", "scan.tiff"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ failures and cancelling

    [Fact]
    public async Task A_result_that_is_not_exactly_twice_the_size_is_an_error_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var engine = new Doubler { Override = s => new Raster(s.Width * 2, s.Height * 2 - 1, new byte[s.Width * 2 * (s.Height * 2 - 1) * 4]) };

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, engine, default));

        Assert.Equal(UpscaleCopy.WrongSize, ex.Message);
        Assert.Equal(["a.png"], FileNames(tmp));
    }

    [Fact]
    public async Task An_engine_that_cannot_run_gives_a_plain_message_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));

        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, new Throwing(new ConversionException(UpscaleCopy.ModelNotInstalled)), default));

        Assert.Equal(UpscaleCopy.ModelNotInstalled, ex.Message);
        Assert.Contains("Windows Update", ex.Message);
        Assert.Equal(["a.png"], FileNames(tmp));
    }

    [Fact]
    public async Task Cancelling_before_the_start_and_during_the_scaling_both_leave_nothing_behind()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        using var early = new CancellationTokenSource();
        await early.CancelAsync();
        using var late = new CancellationTokenSource();
        var cancelsWhileScaling = new Doubler { Override = s => { late.Cancel(); return Double(s); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, new Doubler(), early.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageConverter.UpscaleAsync(input, Caps, ConversionOptions.Default, cancelsWhileScaling, late.Token));

        Assert.Equal(["a.png"], FileNames(tmp));
    }

    [Fact]
    public async Task Without_the_capability_the_real_entry_point_refuses_with_the_not_available_message()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));

        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.UpscaleAsync(input, Caps with { AiUpscale = false }, ConversionOptions.Default, default));

        Assert.Equal(UpscaleCopy.NotAvailable, ex.Message);
        Assert.Equal(["a.png"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ the tile

    [Fact]
    public void The_tile_is_a_picture_tool_shown_only_where_the_scaler_can_run()
    {
        var png = new[] { @"C:\x\a.png" };
        var tile = ActionCatalog.Find(ImageActions.All, "Upscale 2x")!;

        Assert.Equal(WheelPage.Tool, tile.Page);
        Assert.Equal("Upscale 2x", tile.Id);
        Assert.False(string.IsNullOrEmpty(tile.Glyph));
        Assert.True(tile.IsVisibleFor(png, Capabilities.All));
        Assert.False(tile.IsVisibleFor(png, Capabilities.All with { AiUpscale = false }));
        Assert.Contains(ActionCatalog.For(png, Capabilities.All, _ => Category.Image), a => a.Title == "Upscale 2x");
        Assert.DoesNotContain(ActionCatalog.For(png, Capabilities.All with { AiUpscale = false }, _ => Category.Image), a => a.Title == "Upscale 2x");
        Assert.DoesNotContain(ActionCatalog.For([@"C:\x\a.pdf"], Capabilities.All, _ => Category.Pdf), a => a.Title == "Upscale 2x");
    }

    [Fact]
    public void The_tile_has_words_for_the_wheel_and_the_progress_line()
    {
        var tile = ActionCatalog.Find(ImageActions.All, "Upscale 2x")!;

        Assert.Equal("Save a copy at twice the width and height", WheelText.Describe(tile));
        Assert.Equal("Upscaling 1 of 3…", FeedbackCopy.ProgressTitle(tile, 1, 3));
    }

    [Fact]
    public async Task The_tile_writes_through_naming_and_in_a_batch_a_failure_keeps_the_outputs_already_written()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("a.png", Fixtures.PngRgb(32, 24));
        var bad = tmp.WriteFile("b.png", Fixtures.PngRgb(32, 24)[..40]);   // truncated
        var next = tmp.WriteFile("c.png", Fixtures.PngRgb(16, 12));
        using var guard = OriginalsGuard.Watch(good, bad, next);
        var tile = UpscaleAction.Create(new Doubler());

        var result = await tile.Run([good, bad, next], new ActionContext(Caps), default);

        Assert.True(result.PartialFailure);
        Assert.Equal([tmp.Combine("a 2x.png"), tmp.Combine("c 2x.png")], result.Outputs.Select(o => o.Path));
        Assert.Equal(bad, Assert.Single(result.Failures).Input);
        Assert.Equal(["a 2x.png", "a.png", "b.png", "c 2x.png", "c.png"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Cancelling_a_batch_keeps_what_was_written_and_stops()
    {
        using var tmp = new TempDir();
        var one = tmp.WriteFile("a.png", Fixtures.PngRgb(32, 24));
        var two = tmp.WriteFile("b.png", Fixtures.PngRgb(32, 24));
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var engine = new Doubler { Override = s => { if (++calls == 2) cts.Cancel(); return Double(s); } };

        var result = await UpscaleAction.Create(engine).Run([one, two], new ActionContext(Caps), cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(tmp.Combine("a 2x.png"), Assert.Single(result.Outputs).Path);
        Assert.Equal(["a 2x.png", "a.png", "b.png"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ the capability

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("NotReady", true)]        // supported, model not installed yet: the first run asks Windows for it
    [InlineData("DisabledByUser", false)]
    [InlineData("NotSupported", false)]
    public void The_tile_shows_when_the_scaler_is_ready_or_only_waiting_for_its_model(string state, bool shown) =>
        Assert.Equal(shown, WindowsUpscaleEngine.IsShown(Enum.Parse<UpscaleReadiness>(state)));

    [Fact]
    public void Probing_never_throws_whatever_this_PC_is()
    {
        _ = WindowsUpscaleEngine.IsAvailable();
        Assert.NotNull(Capabilities.Probe());
    }

    [Fact]
    public void The_dev_switch_can_force_the_flag_on_or_off_and_the_removal_wins()
    {
        var none = Capabilities.None;
        Assert.True(CapabilityOverrides.Parse("ai-upscale")!(none).AiUpscale);
        Assert.Equal(none with { AiUpscale = true }, CapabilityOverrides.Parse("AI-Upscale")!(none));
        Assert.False(CapabilityOverrides.Parse("no-ai-upscale")!(Capabilities.All).AiUpscale);
        Assert.Equal(Capabilities.All with { AiUpscale = false }, CapabilityOverrides.Parse("no-ai-upscale")!(Capabilities.All));
        Assert.False(CapabilityOverrides.Parse("ai-upscale,no-ai-upscale")!(none).AiUpscale);
        Assert.True(CapabilityOverrides.Parse("no-heif,ai-upscale")!(none).AiUpscale);
        Assert.Null(CapabilityOverrides.Parse("upscale"));
    }

    [Fact]
    public void The_override_reaches_every_snapshot_and_is_cleared_with_the_dev_switch_off()
    {
        try
        {
            CapabilityOverrides.Apply("ai-upscale");
            Assert.True(Capabilities.Current.AiUpscale);
            Capabilities.TryGetCurrent(out var snapshot);   // probed or quick, whichever is ready: the override applies to both
            Assert.True(snapshot.AiUpscale);

            CapabilityOverrides.Apply("no-ai-upscale");
            Assert.False(Capabilities.Current.AiUpscale);
        }
        finally
        {
            CapabilityOverrides.Apply(null);
        }
        Assert.Null(Capabilities.Override);
    }

    [Fact]
    public void The_shell_cache_carries_the_flag_and_an_older_file_without_it_means_false()
    {
        Assert.True(CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.None with { AiUpscale = true })).AiUpscale);
        Assert.False(CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.All with { AiUpscale = false })).AiUpscale);
        Assert.False(CapabilityCache.Parse("Ocr=True\nFaceDetect=True").AiUpscale);
    }

    // ------------------------------------------------------------------ the real scaler

    [SkipIfNoCapability(nameof(Capabilities.AiUpscale))]
    public async Task The_real_scaler_makes_a_small_picture_exactly_twice_the_size()
    {
        // Runs only on a PC where Windows' image scaler is ready or can install its model (a Copilot+ PC, run from the installed package).
        using var tmp = new TempDir();
        var input = tmp.WriteFile("small.png", Fixtures.PhotoPng(96, 64));

        var path = await ImageConverter.UpscaleAsync(input, Capabilities.Current, ConversionOptions.Default, default);

        var back = Decode(path);
        Assert.Equal(tmp.Combine("small 2x.png"), path);
        Assert.Equal((192, 128), (back.Width, back.Height));
    }
}
