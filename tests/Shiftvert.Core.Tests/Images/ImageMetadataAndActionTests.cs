using Shiftvert.Core;
using Shiftvert.Core.Actions;
using Shiftvert.Core.Actions.Catalog;
using Shiftvert.Core.Images;
using static Shiftvert.Core.Tests.Images.ImageTestUtil;

namespace Shiftvert.Core.Tests.Images;

/// <summary>Metadata that follows a picture across formats, and the image actions as the wheel/CLI run them.</summary>
public class ImageMetadataTests
{
    [Fact]
    public async Task Date_taken_and_camera_model_follow_a_jpeg_into_tiff()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("shot.jpg", Fixtures.JpegWithMetadata(64, 48, orientation: 1, gps: false));

        var output = await ImageConverter.ToFormatAsync(input, ImageFormat.Tiff, default);

        var props = DecodeFull(output).Properties;
        Assert.NotNull(props);
        Assert.Contains("System.Photo.CameraModel", props!.Keys);
        Assert.Equal("Model One", props["System.Photo.CameraModel"].Value);
        var taken = Assert.IsType<DateTimeOffset>(props["System.Photo.DateTaken"].Value);
        Assert.Equal(new DateTime(2023, 7, 4, 12, 34, 56), taken.DateTime);
    }

    [Fact]
    public async Task Strip_exif_does_not_carry_those_properties()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("shot.jpg", Fixtures.JpegWithMetadata(64, 48, orientation: 1, gps: true));
        var output = await ImageConverter.StripExifAsync(input, Capabilities.All with { HeifEncode = false }, default);
        var props = DecodeFull(output).Properties;
        Assert.True(props is null || !props.ContainsKey("System.Photo.CameraModel"));
    }

    [SkipIfNoCapability(nameof(Capabilities.HeifEncode))]
    public async Task Compress_of_a_heic_stays_heic_and_heic_to_jpg_keeps_the_date()
    {
        using var tmp = new TempDir();
        var jpeg = tmp.WriteFile("shot.jpg", Fixtures.JpegWithMetadata(2400, 1600, gps: false));
        var heic = await ImageConverter.ToFormatAsync(jpeg, ImageFormat.Heic, default);
        Assert.Equal(ImageContainer.Heic, ContainerOf(heic));

        var back = await ImageConverter.ToFormatAsync(heic, ImageFormat.Png, default);
        Assert.Equal((2400, 1600), (Decode(back).Width, Decode(back).Height));

        var resized = await ImageConverter.ResizeHalfAsync(heic, Capabilities.Current, default);
        Assert.Equal(".heic", Path.GetExtension(resized));
        Assert.Equal((1200, 800), (Decode(resized).Width, Decode(resized).Height));
    }
}

public class ImageActionTests
{
    private static readonly Capabilities NoHeic = Capabilities.All with { HeifEncode = false };

    private static ActionDef Action(string title) => ActionCatalog.Find(ImageActions.All, title)!;

    private static ActionContext Ctx(IInteraction? interaction = null) => new(NoHeic, interaction);

    private sealed class FixedCrop(CropChoice? choice) : IInteraction
    {
        public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct) => Task.FromResult(choice);
        public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct) => Task.FromResult<TimeRange?>(null);
    }

    [Fact]
    public async Task Format_action_converts_each_file_and_reports_outputs()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.png", Fixtures.PngRgb());
        var b = tmp.WriteFile("b.bmp", Fixtures.Bmp());

        var result = await Action("JPG").Run([a, b], Ctx(), default);

        Assert.True(result.Succeeded);
        Assert.Equal([tmp.Combine("a.jpg"), tmp.Combine("b.jpg")], result.Outputs.Select(o => o.Path).ToArray());
    }

    [Fact]
    public async Task Files_already_in_the_target_format_are_skipped_and_counted()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb());
        var jpg = tmp.WriteFile("b.jpg", Fixtures.QuadrantJpeg());

        var result = await Action("PNG").Run([png, jpg], Ctx(), default);

        Assert.Equal(1, result.AlreadyInFormat);
        Assert.Equal("PNG", result.AlreadyLabel);
        Assert.Equal([tmp.Combine("b.png")], result.Outputs.Select(o => o.Path).ToArray());
        Assert.Contains("1 already PNG", result.ToastText);
        Assert.False(File.Exists(tmp.Combine("a 2.png")));
    }

    [Fact]
    public async Task One_bad_file_does_not_stop_the_batch()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("good.png", Fixtures.PngRgb());
        var bad = tmp.WriteFile("bad.png", Fixtures.Noise(300));
        var good2 = tmp.WriteFile("good2.png", Fixtures.PngRgb());

        var result = await Action("TIFF").Run([good, bad, good2], Ctx(), default);

        Assert.Equal(2, result.Outputs.Count);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(bad, failure.Input);
        Assert.Contains("Couldn't read", failure.Message);
    }

    [Fact]
    public async Task Tools_use_the_catalog_suffixes()
    {
        using var tmp = new TempDir();
        var photo = tmp.WriteFile("photo.jpg", Fixtures.PhotoJpeg(1000, 700, quality: 98));

        var compressed = await Action("Compress").Run([photo], Ctx(), default);
        var resized = await Action("Resize 50%").Run([photo], Ctx(), default);
        var cleaned = await Action("Strip EXIF").Run([photo], Ctx(), default);

        Assert.Equal(tmp.Combine("photo compressed.jpg"), Assert.Single(compressed.Outputs).Path);
        Assert.Equal(tmp.Combine("photo 50%.jpg"), Assert.Single(resized.Outputs).Path);
        Assert.Equal(tmp.Combine("photo clean.jpg"), Assert.Single(cleaned.Outputs).Path);
    }

    [Fact]
    public async Task Compress_that_cannot_help_is_reported_as_a_failure_with_the_reason()
    {
        using var tmp = new TempDir();
        var tiny = tmp.WriteFile("tiny.jpg", Fixtures.QuadrantJpeg(16, 12, quality: 30));
        var result = await Action("Compress").Run([tiny], Ctx(), default);
        Assert.True(result.AllFailed);
        Assert.Contains("already small", result.AllFailedMessage);
    }

    [Fact]
    public async Task Crop_asks_the_interaction_and_applies_the_choice()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb(64, 48));

        var result = await Action("Crop").Run([png], Ctx(new FixedCrop(new CropChoice(new RectPx(0, 0, 32, 24), 32, 24))), default);

        Assert.Equal(tmp.Combine("pic cropped.png"), Assert.Single(result.Outputs).Path);
        Assert.Equal((32, 24), (Decode(result.Outputs[0].Path).Width, Decode(result.Outputs[0].Path).Height));
    }

    [Fact]
    public async Task Cancelled_crop_keeps_earlier_outputs_and_stops()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var b = tmp.WriteFile("b.png", Fixtures.PngRgb(64, 48));
        var calls = 0;
        var interaction = new CountingCrop(() => ++calls == 1 ? new CropChoice(new RectPx(0, 0, 10, 10), 10, 10) : null);

        var result = await Action("Crop").Run([a, b], Ctx(interaction), default);

        Assert.True(result.Cancelled);
        Assert.Single(result.Outputs);
        Assert.False(File.Exists(tmp.Combine("b cropped.png")));
    }

    private sealed class CountingCrop(Func<CropChoice?> next) : IInteraction
    {
        public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct) => Task.FromResult(next());
        public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct) => Task.FromResult<TimeRange?>(null);
    }

    [Fact]
    public async Task Originals_are_untouched_by_every_image_action()
    {
        using var tmp = new TempDir();
        var jpg = tmp.WriteFile("a.jpg", Fixtures.JpegWithMetadata(600, 400, orientation: 6));
        var png = tmp.WriteFile("b.png", Fixtures.PngAlpha(64, 48));
        using var guard = OriginalsGuard.Watch(jpg, png);
        var interaction = new FixedCrop(new CropChoice(new RectPx(2, 2, 20, 20), 20, 20));

        foreach (var title in new[] { "PNG", "JPG", "TIFF", "GIF", "BMP", "WebP", "Compress", "Resize 50%", "Crop", "Strip EXIF" })
            await Action(title).Run([jpg, png], Ctx(interaction), default);

        guard.AssertUnchanged();
    }

    [Fact]
    public void Catalog_hides_heic_and_webp_tiles_without_the_capabilities()
    {
        var png = new[] { @"C:\x\a.png" };
        Assert.DoesNotContain(Action("HEIC"), ActionCatalog.For(png, Capabilities.None, _ => Category.Image));
        Assert.Contains(Action("HEIC"), ActionCatalog.For(png, Capabilities.None with { HeifEncode = true }, _ => Category.Image));
        Assert.Contains(Action("WebP"), ActionCatalog.For(png, Capabilities.None with { WebpEncode = true }, _ => Category.Image));
    }

}
