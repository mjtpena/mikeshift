using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Images;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>Tool page: Compress, Resize 50%, Crop, Strip EXIF (PLAN §5.1 Images).</summary>
public class ImageToolTests
{
    private static readonly Capabilities NoHeic = Capabilities.All with { HeifEncode = false };

    // ------------------------------------------------------------------ Compress

    [Fact]
    public async Task Compress_shrinks_a_big_photo_to_3000px_jpg_with_suffix()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("big.png", Fixtures.PhotoPng(3200, 1000));
        using var guard = OriginalsGuard.Watch(input);

        var output = await ImageConverter.CompressAsync(input, NoHeic, default);

        Assert.Equal(tmp.Combine("big compressed.jpg"), output);
        Assert.True(new FileInfo(output).Length <= new FileInfo(input).Length * 0.95);
        Assert.Equal(ImageContainer.Jpeg, ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((3000, 938), (back.Width, back.Height)); // long side 3000, aspect kept (3200x1000 -> 3000x937.5)
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Compress_recompresses_a_high_quality_jpeg_without_resizing_small_images()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.jpg", Fixtures.PhotoJpeg(1200, 800, quality: 98));

        var output = await ImageConverter.CompressAsync(input, NoHeic, default);

        Assert.Equal(tmp.Combine("photo compressed.jpg"), output);
        Assert.True(new FileInfo(output).Length <= new FileInfo(input).Length * 0.95);
        var back = Decode(output);
        Assert.Equal((1200, 800), (back.Width, back.Height));
    }

    [Fact]
    public async Task Compress_says_already_small_when_it_would_not_save_five_percent()
    {
        using var tmp = new TempDir();
        // A 16x12 picture is a few hundred bytes: re-encoding cannot beat it by 5 %.
        var input = tmp.WriteFile("tiny.jpg", Fixtures.QuadrantJpeg(16, 12, quality: 40));
        using var guard = OriginalsGuard.Watch(input);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.CompressAsync(input, NoHeic, default));

        Assert.Contains("already small", ex.Message);
        Assert.Equal(["tiny.jpg"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task Compress_keeps_transparency_by_writing_png_when_heic_cannot_be_encoded()
    {
        using var tmp = new TempDir();
        // Big smooth image with a transparent corner; downscaling to 3000 px makes the PNG clearly smaller.
        var bgra = Fixtures.PhotoBgra(3300, 1100);
        for (var y = 0; y < 300; y++)
            for (var x = 0; x < 300; x++)
                bgra[(y * 3300 + x) * 4 + 3] = 0;
        var png = tmp.WriteFile("logo.png", RasterToPng(new Raster(3300, 1100, bgra)));

        var output = await ImageConverter.CompressAsync(png, NoHeic, default);

        Assert.Equal(tmp.Combine("logo compressed.png"), output);
        Assert.Equal(ImageContainer.Png, ContainerOf(output));
        var back = Decode(output);
        Assert.Equal(3000, back.Width);
        Assert.True(back.HasRealAlpha);
        Assert.Equal(0, At(back, 10, 10).A);
    }

    [Fact]
    public async Task Compress_keeps_transparent_images_as_png_even_when_a_heic_encoder_exists()
    {
        // Windows' HEIF encoder drops the alpha channel, so HEIC would turn the transparent pixels into a solid colour.
        using var tmp = new TempDir();
        var bgra = Fixtures.PhotoBgra(3300, 1100);
        bgra[3] = 0;
        var png = tmp.WriteFile("logo.png", RasterToPng(new Raster(3300, 1100, bgra)));
        var output = await ImageConverter.CompressAsync(png, Capabilities.All, default);
        Assert.Equal(tmp.Combine("logo compressed.png"), output);
        Assert.True(Decode(output).Bgra[3] < 128, "the transparent corner was lost"); // resampled to 3000 px, so only almost transparent
    }

    [SkipIfNoCapability(nameof(Capabilities.HeifEncode))]
    public async Task Converting_a_transparent_image_to_heic_flattens_it_on_white_instead_of_keeping_a_colour_block()
    {
        using var tmp = new TempDir();
        var bgra = Fixtures.QuadrantsBgra(64, 48);
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 32; x++)
                bgra[(y * 64 + x) * 4 + 3] = 0;
        var png = tmp.WriteFile("logo.png", RasterToPng(new Raster(64, 48, bgra)));
        var output = await ImageConverter.ToFormatAsync(png, ImageFormat.Heic, default);
        var px = Decode(output).Bgra;
        // The transparent top-left quadrant is white (255,255,255) after a lossy HEIC round trip, not the red that sat under it.
        var o = (4 * 64 + 4) * 4;
        Assert.True(px[o] > 235 && px[o + 1] > 235 && px[o + 2] > 235, $"B{px[o]} G{px[o + 1]} R{px[o + 2]}");
    }

    [Fact]
    public async Task Compress_refuses_animated_gif_and_apng()
    {
        using var tmp = new TempDir();
        var gif = tmp.WriteFile("anim.gif", Fixtures.Gif(64, 48, animated: true));
        var apng = tmp.WriteFile("anim.png", Fixtures.AnimatedPng());
        foreach (var path in new[] { gif, apng })
        {
            var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.CompressAsync(path, NoHeic, default));
            Assert.Contains("Animated", ex.Message);
        }
        Assert.Equal(["anim.gif", "anim.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task Tools_refuse_animated_gif_and_multi_page_tiff_instead_of_dropping_frames()
    {
        using var tmp = new TempDir();
        var gif = tmp.WriteFile("anim.gif", Fixtures.Gif(64, 48, animated: true));
        var tif = tmp.WriteFile("pages.tif", Fixtures.TiffMultiFrame(64, 48, 3));
        foreach (var path in new[] { gif, tif })
        {
            await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.ResizeHalfAsync(path, NoHeic, default));
            await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.CropAsync(path, new CropChoice(new RectPx(0, 0, 10, 10), 10, 10), NoHeic, default));
            await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.StripExifAsync(path, NoHeic, default));
        }
    }

    // ------------------------------------------------------------------ Resize

    [Theory]
    [InlineData("pic.png")]
    [InlineData("pic.jpg")]
    [InlineData("pic.jpeg")]
    [InlineData("pic.bmp")]
    [InlineData("pic.gif")]
    [InlineData("pic.tif")]
    [InlineData("pic.webp")]
    public async Task Resize_halves_both_sides_in_the_same_format(string name)
    {
        using var tmp = new TempDir();
        var ext = Path.GetExtension(name)[1..];
        var bytes = ext switch
        {
            "png" => Fixtures.PngRgb(100, 60),
            "jpg" or "jpeg" => Fixtures.QuadrantJpeg(100, 60),
            "bmp" => Fixtures.Bmp(100, 60),
            "gif" => Fixtures.Gif(100, 60),
            "tif" => Fixtures.TiffMultiFrame(100, 60, 1),
            _ => Fixtures.Webp(100, 60),
        };
        var input = tmp.WriteFile(name, bytes);
        using var guard = OriginalsGuard.Watch(input);

        var output = await ImageConverter.ResizeHalfAsync(input, Capabilities.All, default);

        Assert.Equal(tmp.Combine("pic 50%" + Path.GetExtension(name)), output);
        Assert.Equal(ContainerOf(input), ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((50, 30), (back.Width, back.Height));
        AssertQuadrants(back, tol: 40);
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Resize_rounds_odd_sizes_up_and_never_goes_below_one_pixel()
    {
        using var tmp = new TempDir();
        var odd = await ImageConverter.ResizeHalfAsync(tmp.WriteFile("odd.png", Fixtures.PngRgb(101, 61)), NoHeic, default);
        Assert.Equal((51, 31), (Decode(odd).Width, Decode(odd).Height));
        var one = await ImageConverter.ResizeHalfAsync(tmp.WriteFile("one.png", Fixtures.PngRgb(1, 1)), NoHeic, default);
        Assert.Equal((1, 1), (Decode(one).Width, Decode(one).Height));
    }

    [Fact]
    public async Task Resize_of_a_jpeg_keeps_exif_and_bakes_in_the_orientation()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6));

        var output = await ImageConverter.ResizeHalfAsync(input, NoHeic, default);

        var bytes = File.ReadAllBytes(output);
        Assert.Equal(1, JpegMeta.ReadOrientation(bytes));
        Assert.NotNull(JpegMeta.ExtractExif(bytes));
        var back = Decode(output);
        Assert.Equal((40, 60), (back.Width, back.Height)); // upright 80x120 halved
    }

    [Fact]
    public async Task Resize_of_a_transparent_png_keeps_the_alpha_channel()
    {
        using var tmp = new TempDir();
        var output = await ImageConverter.ResizeHalfAsync(tmp.WriteFile("a.png", Fixtures.PngAlpha(64, 48)), NoHeic, default);
        var back = Decode(output);
        Assert.True(back.HasRealAlpha);
        Assert.Equal(0, At(back, 4, 4).A);
        Assert.Equal(255, At(back, 28, 20).A);
    }

    [Fact]
    public async Task Resize_of_an_ico_falls_back_to_png()
    {
        using var tmp = new TempDir();
        var output = await ImageConverter.ResizeHalfAsync(tmp.WriteFile("icon.ico", Fixtures.Ico(32, 32)), NoHeic, default);
        Assert.Equal(tmp.Combine("icon 50%.png"), output);
        Assert.Equal((16, 16), (Decode(output).Width, Decode(output).Height));
    }

    // ------------------------------------------------------------------ Crop

    [Fact]
    public async Task Crop_cuts_the_exact_pixels_without_scaling()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(64, 48));
        using var guard = OriginalsGuard.Watch(input);

        // Straddles the vertical colour boundary at x=32: 8 px of red then 12 px of green.
        var output = await ImageConverter.CropAsync(input, new CropChoice(new RectPx(24, 4, 20, 10), 20, 10), NoHeic, default);

        Assert.Equal(tmp.Combine("pic cropped.png"), output);
        var back = Decode(output);
        Assert.Equal((20, 10), (back.Width, back.Height));
        Assert.Equal(Red, (At(back, 7, 5).R, At(back, 7, 5).G, At(back, 7, 5).B));
        Assert.Equal(Green, (At(back, 8, 5).R, At(back, 8, 5).G, At(back, 8, 5).B));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Crop_scales_to_the_requested_output_size()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(64, 48));

        var down = await ImageConverter.CropAsync(input, new CropChoice(new RectPx(0, 0, 32, 24), 16, 12), NoHeic, default);
        var downPixels = Decode(down);
        Assert.Equal((16, 12), (downPixels.Width, downPixels.Height));
        Assert.True(Near(downPixels, 4, 3, Red, 6)); // the whole crop is the red quadrant

        var up = await ImageConverter.CropAsync(input, new CropChoice(new RectPx(0, 0, 32, 24), 100, 75), NoHeic, default);
        var upPixels = Decode(up);
        Assert.Equal((100, 75), (upPixels.Width, upPixels.Height));
        Assert.True(Near(upPixels, 50, 37, Red, 6));
    }

    [Fact]
    public async Task Crop_keeps_the_format_and_uses_upright_coordinates()
    {
        using var tmp = new TempDir();
        // Stored 120x80, orientation 6 -> displayed 80x120 with the stored red quadrant at the TOP-RIGHT.
        var input = tmp.WriteFile("rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6));

        var output = await ImageConverter.CropAsync(input, new CropChoice(new RectPx(40, 0, 40, 60), 40, 60), NoHeic, default);

        Assert.Equal(tmp.Combine("rot cropped.jpg"), output);
        Assert.Equal(ImageContainer.Jpeg, ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((40, 60), (back.Width, back.Height));
        Assert.True(Near(back, 20, 30, Red, 40), $"was {At(back, 20, 30)}");
    }

    [Fact]
    public async Task Crop_clamps_to_the_image_and_rejects_empty_areas()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.png", Fixtures.PngRgb(64, 48));

        var clamped = await ImageConverter.CropAsync(input, new CropChoice(new RectPx(50, 40, 100, 100), 14, 8), NoHeic, default);
        Assert.Equal((14, 8), (Decode(clamped).Width, Decode(clamped).Height));

        await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.CropAsync(input, new CropChoice(new RectPx(100, 100, 10, 10), 10, 10), NoHeic, default));
        await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.CropAsync(input, new CropChoice(new RectPx(0, 0, 0, 10), 10, 10), NoHeic, default));
    }

    // ------------------------------------------------------------------ Strip EXIF

    [Fact]
    public async Task Strip_exif_removes_exif_gps_xmp_and_photoshop_but_keeps_the_picture_upright()
    {
        using var tmp = new TempDir();
        var source = Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: true, xmp: true, photoshop: true);
        var input = tmp.WriteFile("trip.jpg", source);
        using var guard = OriginalsGuard.Watch(input);
        // Sanity: the fixture really carries everything we claim to remove.
        Assert.True(JpegMeta.HasMetadataSegments(source));
        Assert.True(JpegMeta.HasGps(JpegMeta.ExtractExif(source)!));

        var output = await ImageConverter.StripExifAsync(input, NoHeic, default);

        Assert.Equal(tmp.Combine("trip clean.jpg"), output);
        var bytes = File.ReadAllBytes(output);
        Assert.False(JpegMeta.HasMetadataSegments(bytes), JpegMeta.Describe(bytes));
        Assert.Null(JpegMeta.ExtractExif(bytes));
        Assert.DoesNotContain(Fixtures.ExifMake, System.Text.Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain("xmpmeta", System.Text.Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain("Photoshop", System.Text.Encoding.Latin1.GetString(bytes));
        var back = Decode(output);
        Assert.Equal((80, 120), (back.Width, back.Height)); // orientation baked into the pixels
        Assert.True(Near(back, 60, 30, Red, 40));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Strip_exif_works_for_png_and_keeps_the_format()
    {
        using var tmp = new TempDir();
        var output = await ImageConverter.StripExifAsync(tmp.WriteFile("pic.png", Fixtures.PngAlpha(64, 48)), NoHeic, default);
        Assert.Equal(tmp.Combine("pic clean.png"), output);
        Assert.Equal(ImageContainer.Png, ContainerOf(output));
        Assert.True(Decode(output).HasRealAlpha);
    }

    // ------------------------------------------------------------------ helpers

    private static byte[] RasterToPng(Raster r)
    {
        using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(r.Width, r.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(r.Bgra, 0, bmp.GetPixels(), r.Bgra.Length);
        using var pixmap = bmp.PeekPixels();
        using var data = pixmap.Encode(new SkiaSharp.SKPngEncoderOptions())!;
        return data.ToArray();
    }
}
