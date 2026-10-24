using MikeShift.Core;
using MikeShift.Core.Images;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>Format page conversions: every conversion is decoded again and checked.</summary>
public class ImageFormatTests
{
    private static async Task<string> Convert(TempDir tmp, string name, byte[] bytes, ImageFormat to, CancellationToken ct = default)
    {
        var input = tmp.WriteFile(name, bytes);
        using var guard = OriginalsGuard.Watch(input);
        var output = await ImageConverter.ToFormatAsync(input, to, ct);
        Assert.True(File.Exists(output));
        guard.AssertUnchanged();
        return output;
    }

    public static TheoryData<ImageFormat, string, string> Targets => new()
    {
        { ImageFormat.Jpg, ".jpg", "Jpeg" },
        { ImageFormat.Tiff, ".tiff", "Tiff" },
        { ImageFormat.Gif, ".gif", "Gif" },
        { ImageFormat.Bmp, ".bmp", "Bmp" },
        { ImageFormat.Webp, ".webp", "Webp" },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Png_round_trips_to_the_other_formats(ImageFormat format, string ext, string containerName)
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "pic.png", Fixtures.PngRgb(64, 48), format);

        Assert.Equal(tmp.Combine("pic" + ext), output);
        Assert.Equal(Enum.Parse<ImageContainer>(containerName), ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((64, 48), (back.Width, back.Height));
        // GIF has a 256-colour palette and WebP/JPEG are lossy: allow a wider tolerance.
        AssertQuadrants(back, tol: 40);
    }

    [Theory]
    [InlineData("pic.jpg", "jpg")]
    [InlineData("pic.bmp", "bmp")]
    [InlineData("pic.gif", "gif")]
    [InlineData("pic.tif", "tif")]
    [InlineData("pic.webp", "webp")]
    public async Task Other_sources_convert_to_png(string name, string kind)
    {
        using var tmp = new TempDir();
        var bytes = kind switch
        {
            "jpg" => Fixtures.QuadrantJpeg(64, 48),
            "bmp" => Fixtures.Bmp(64, 48),
            "gif" => Fixtures.Gif(64, 48),
            "tif" => Fixtures.TiffMultiFrame(64, 48, 3),
            "webp" => Fixtures.Webp(64, 48),
            _ => throw new ArgumentException(kind),
        };

        var output = await Convert(tmp, name, bytes, ImageFormat.Png);

        Assert.Equal(ImageContainer.Png, ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((64, 48), (back.Width, back.Height));
        AssertQuadrants(back, tol: 30);
    }

    [Fact]
    public async Task Multi_page_tiff_converts_its_first_page()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "pages.tif", Fixtures.TiffMultiFrame(64, 48, 3), ImageFormat.Png);
        Assert.True(Near(Decode(output), 10, 10, Red));
    }

    [Fact]
    public async Task Rgba_with_transparency_becomes_an_opaque_jpg_on_white()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "alpha.png", Fixtures.PngAlpha(64, 48), ImageFormat.Jpg);

        var back = Decode(output);
        Assert.False(back.HasRealAlpha);
        Assert.True(Near(back, 8, 6, White, tol: 6), $"transparent area was {At(back, 8, 6)}");
        Assert.True(Near(back, 56, 6, Green, tol: 30));
    }

    [Fact]
    public async Task Opaque_rgba_png_converts_to_jpg_unchanged_in_colour()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "opaque.png", Fixtures.PngOpaqueRgba(64, 48), ImageFormat.Jpg);
        var back = Decode(output);
        Assert.False(back.HasRealAlpha);
        AssertQuadrants(back, tol: 30);
    }

    [Fact]
    public async Task Transparent_png_keeps_alpha_in_png_tiff_and_webp()
    {
        using var tmp = new TempDir();
        foreach (var f in new[] { ImageFormat.Tiff, ImageFormat.Webp })
        {
            var output = await Convert(tmp, $"alpha-{f}.png", Fixtures.PngAlpha(64, 48), f);
            var back = Decode(output);
            Assert.True(back.HasRealAlpha, f.ToString());
            Assert.True(At(back, 8, 6).A < 40, f.ToString());
            Assert.True(At(back, 56, 40).A > 200, f.ToString());
        }
    }

    [Fact]
    public async Task Transparent_png_can_be_saved_as_gif_and_bmp()
    {
        using var tmp = new TempDir();
        var gif = await Convert(tmp, "a1.png", Fixtures.PngAlpha(64, 48), ImageFormat.Gif);
        Assert.Equal(ImageContainer.Gif, ContainerOf(gif));
        var bmp = await Convert(tmp, "a2.png", Fixtures.PngAlpha(64, 48), ImageFormat.Bmp);
        Assert.Equal(ImageContainer.Bmp, ContainerOf(bmp));
        var back = Decode(bmp);
        Assert.Equal((64, 48), (back.Width, back.Height));
        Assert.True(Near(back, 56, 40, Yellow, tol: 30));
    }

    [Fact]
    public async Task Gif_is_rebuilt_from_upright_pixels_of_a_rotated_jpeg()
    {
        using var tmp = new TempDir();
        // Stored 120x80 with orientation 6 (display rotated 90 deg): upright size is 80x120.
        var output = await Convert(tmp, "rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: false), ImageFormat.Gif);
        var back = Decode(output);
        Assert.Equal((80, 120), (back.Width, back.Height));
    }

    [Fact]
    public async Task Exif_orientation_is_applied_to_the_pixels()
    {
        using var tmp = new TempDir();
        // Orientation 6 = rotate 90 deg clockwise to display. The stored top-left (red) ends up top-right.
        var output = await Convert(tmp, "rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: false), ImageFormat.Png);
        var back = Decode(output);
        Assert.Equal((80, 120), (back.Width, back.Height));
        Assert.True(Near(back, 60, 30, Red, 40), $"top-right was {At(back, 60, 30)}");
        Assert.True(Near(back, 20, 30, Blue, 40), $"top-left was {At(back, 20, 30)}");
    }

    [Fact]
    public async Task Jpeg_output_keeps_the_exif_block_made_upright()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "rot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: true), ImageFormat.Jpg);

        var bytes = File.ReadAllBytes(output);
        var exif = JpegMeta.ExtractExif(bytes);
        Assert.NotNull(exif);
        Assert.Contains(Fixtures.ExifMake, System.Text.Encoding.ASCII.GetString(exif!));
        Assert.True(JpegMeta.HasGps(exif!));
        Assert.Equal(1, JpegMeta.ReadOrientation(bytes));
        var back = Decode(output);
        Assert.Equal((80, 120), (back.Width, back.Height));
    }

    [Fact]
    public async Task Misnamed_file_is_decoded_by_content()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "actually-a-png.jpg", Fixtures.PngRgb(32, 24), ImageFormat.Tiff);
        Assert.Equal(ImageContainer.Tiff, ContainerOf(output));
        Assert.Equal((32, 24), (Decode(output).Width, Decode(output).Height));
    }

    [Fact]
    public async Task Output_names_are_uniquified_and_never_overwrite()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.jpg", Fixtures.QuadrantJpeg());
        var first = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, default);
        var second = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, default);
        var third = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, default);
        Assert.Equal(tmp.Combine("photo.png"), first);
        Assert.Equal(tmp.Combine("photo 2.png"), second);
        Assert.Equal(tmp.Combine("photo 3.png"), third);
    }

    [Fact]
    public async Task Unreadable_file_is_a_clean_failure_and_leaves_no_output()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("broken.png", Fixtures.Noise(500));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, default));
        Assert.Contains("Couldn't read", ex.Message);
        Assert.Equal(["broken.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task More_than_200_megapixels_is_refused_from_the_header_alone()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000));
        using var guard = OriginalsGuard.Watch(input);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, default));
        Assert.Contains("too large", ex.Message);
        Assert.Contains("300", ex.Message);
    }

    [Fact]
    public async Task Pixel_limit_is_enforced_at_the_configured_value()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("big.png", Fixtures.PngRgb(100, 100));
        await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, 9_999, default));
        var ok = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, 10_000, default);
        Assert.True(File.Exists(ok));
    }

    [Fact]
    public void Blank_heic_or_avif_decode_is_detected()
    {
        var black = new Raster(8, 8, new byte[8 * 8 * 4]);
        Assert.True(black.IsAllBlack());
        var opaqueBlack = new byte[8 * 8 * 4];
        for (var i = 3; i < opaqueBlack.Length; i += 4) opaqueBlack[i] = 255;
        Assert.True(new Raster(8, 8, opaqueBlack).IsAllBlack());
        Assert.False(new Raster(8, 8, Fixtures.QuadrantsBgra(8, 8)).IsAllBlack());

        Assert.Throws<ConversionException>(() => ImageCodec.RejectBlankDecode(ImageContainer.Heic, black));
        Assert.Throws<ConversionException>(() => ImageCodec.RejectBlankDecode(ImageContainer.Avif, black));
        ImageCodec.RejectBlankDecode(ImageContainer.Png, black); // a black PNG is a legitimate picture
    }

    [Fact]
    public async Task Cancellation_stops_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, cts.Token));
        Assert.Equal(["a.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task Falls_back_to_downloads_when_the_folder_is_not_writable()
    {
        using var tmp = new TempDir();
        var dir = tmp.CreateDir("locked");
        var input = tmp.WriteFile("locked/a.png", Fixtures.PngRgb());
        using (TempDir.DenyWrites(dir))
        {
            var output = await ImageConverter.ToFormatAsync(input, ImageFormat.Jpg, default);
            Assert.Equal(tmp.Downloads, Path.GetDirectoryName(output));
        }
    }

    [SkipIfNoCapability(nameof(Capabilities.HeifEncode))]
    public async Task Heic_round_trips()
    {
        using var tmp = new TempDir();
        var output = await Convert(tmp, "pic.png", Fixtures.PngRgb(64, 48), ImageFormat.Heic);
        Assert.Equal(tmp.Combine("pic.heic"), output);
        Assert.Equal(ImageContainer.Heic, ContainerOf(output));
        var back = Decode(output);
        Assert.Equal((64, 48), (back.Width, back.Height));
        AssertQuadrants(back, tol: 40);

        // And back to JPG, as a user with iPhone photos would do.
        var jpg = await ImageConverter.ToFormatAsync(output, ImageFormat.Jpg, default);
        Assert.Equal(ImageContainer.Jpeg, ContainerOf(jpg));
    }
}
