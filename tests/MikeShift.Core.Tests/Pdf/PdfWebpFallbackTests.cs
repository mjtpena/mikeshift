using MikeShift.Core.Pdf;
using MikeShift.Core.Tests.Integrity;
using SkiaSharp;

namespace MikeShift.Core.Tests.Pdf;

/// <summary>Edge cases of the SkiaSharp path that reads WebP when Windows has no WebP decoder.</summary>
public class PdfWebpFallbackTests
{
    private static SKColor PageColour(PdfImageSource.PageImage page, int x, int y)
    {
        using var bmp = SKBitmap.Decode(page.Jpeg);
        return bmp.GetPixel(x, y);
    }

    /// <summary>Lossless WebP of raw BGRA bytes, tagged with <paramref name="colorSpace"/> (written as an ICC profile when not sRGB).</summary>
    private static byte[] EncodeWebp(byte[] bgra, int w, int h, SKAlphaType alpha, SKColorSpace? colorSpace)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, alpha, colorSpace));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var data = bmp.PeekPixels().Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))!;
        return data.ToArray();
    }

    private static bool Near(SKColor c, int r, int g, int b, int tolerance = 24) =>
        Math.Abs(c.Red - r) <= tolerance && Math.Abs(c.Green - g) <= tolerance && Math.Abs(c.Blue - b) <= tolerance;

    [Fact]
    public async Task An_animated_webp_becomes_one_page_showing_its_first_frame()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("anim.webp", WebpFixtures.Animated(48, 32, new SKColor(220, 20, 20), new SKColor(20, 20, 220), new SKColor(20, 200, 20)));
        Assert.True(PixDecode.SkiaFrameCount(path) >= 2, "the fixture is not animated");

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.Equal((48, 32), (page.Width, page.Height));
        var c = PageColour(page, 24, 16);
        Assert.True(Near(c, 220, 20, 20), $"the page is not the first frame: {c}");
    }

    [Fact]
    public async Task A_lossy_webp_with_alpha_is_flattened_on_white()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("ghost.webp", WebpFixtures.Solid(32, 32, new SKColor(0, 0, 0, 0), lossy: true));

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        var c = PageColour(page, 16, 16);
        Assert.True(c.Red > 235 && c.Green > 235 && c.Blue > 235, $"transparent pixels are not white: {c}");
    }

    [Fact]
    public async Task A_half_transparent_webp_is_blended_with_white_not_black()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("half.webp", WebpFixtures.Solid(32, 32, new SKColor(0, 0, 0, 128)));

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.InRange(PageColour(page, 16, 16).Red, 110, 145);
    }

    [Fact]
    public async Task A_downscaled_picture_with_transparency_gets_no_dark_edges()
    {
        // Opaque red up to just past the middle, fully transparent black after it: scaling straight-alpha pixels before flattening would
        // blend the transparent black into the red at the seam. (One column past the middle, so the seam does not fall exactly on a
        // boundary of the 8000 -> 3508 box filter.)
        const int w = 8000, h = 400;
        var bgra = new byte[w * h * 4];
        for (var y0 = 0; y0 < h; y0++)
            for (var x0 = 0; x0 <= w / 2; x0++)
            {
                bgra[(y0 * w + x0) * 4 + 2] = 255; // R
                bgra[(y0 * w + x0) * 4 + 3] = 255; // A
            }
        // PNG, not WebP: libwebp may rewrite the colour hidden under alpha 0, which would hide the bug. The SkiaSharp path reads both alike.
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var png = bmp.PeekPixels().Encode(SKPngEncoderOptions.Default)!;
        using var tmp = new TempDir();
        var path = tmp.WriteFile("seam.png", png.ToArray());

        // The pixels before JPEG encoding (whose chroma subsampling blurs any red edge): red and white both have R = 255, so every
        // pixel of a row must too; transparent black scaled in first would pull the seam down to about 191.
        var (raster, _) = PdfPixels.DecodeSkia(path, PdfImageSource.MaxLongSide, 200_000_000)!.Value;
        Assert.Equal(PdfImageSource.MaxLongSide, raster.Width);
        var row = raster.Height / 2 * raster.Width * 4;
        for (var x = 0; x < raster.Width; x++)
            Assert.True(raster.Data[row + x * 4 + 2] == 255, $"dark seam at x={x}: R={raster.Data[row + x * 4 + 2]}");
    }

    [Fact]
    public async Task A_webp_with_an_icc_profile_is_converted_to_srgb()
    {
        // Grey 128 in a linear-light profile is about 188 in sRGB; copying the channel values unconverted would give 128.
        using var linear = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear, SKColorSpaceXyz.Srgb);
        var grey = new byte[32 * 32 * 4];
        Array.Fill(grey, (byte)128);
        for (var i = 3; i < grey.Length; i += 4) grey[i] = 255;
        var bytes = EncodeWebp(grey, 32, 32, SKAlphaType.Opaque, linear);
        Assert.Contains("ICCP", ImageBytes.WebpChunks(bytes)); // the fixture really carries the profile
        using var tmp = new TempDir();
        var path = tmp.WriteFile("linear.webp", bytes);

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.InRange(PageColour(page, 16, 16).Red, 180, 196);
    }

    [Theory]
    [InlineData(8000, 2000, PdfImageSource.MaxLongSide, 877)]
    [InlineData(2000, 8000, 877, PdfImageSource.MaxLongSide)]
    public async Task A_webp_over_the_long_side_limit_is_capped_and_keeps_its_aspect_ratio(int w, int h, int expectedW, int expectedH)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("big.webp", WebpFixtures.Solid(w, h, new SKColor(30, 120, 200)));

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.Equal((expectedW, expectedH), (page.Width, page.Height));
    }

    [Fact]
    public async Task A_one_pixel_webp_becomes_a_one_pixel_page()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("dot.webp", WebpFixtures.Solid(1, 1, new SKColor(200, 30, 30)));

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.Equal((1, 1), (page.Width, page.Height));
        Assert.Equal([0xFF, 0xD8, 0xFF], page.Jpeg[..3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(-1)] // half of the file
    public async Task A_truncated_webp_is_a_clean_could_not_be_read_error(int cut)
    {
        using var tmp = new TempDir();
        var whole = WebpFixtures.Solid(64, 64, new SKColor(10, 160, 90), lossy: true);
        var path = tmp.WriteFile("cut.webp", whole[..(cut < 0 ? whole.Length / 2 : cut)]);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadWithSkiaAsync(path));

        Assert.Equal("cut.webp could not be read as an image.", ex.Message);
    }

    [Fact]
    public async Task A_png_named_webp_is_read_by_its_content()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("really.webp", Fixtures.PdfSourcePng(40, 30));

        var page = await PdfImageSource.LoadWithSkiaAsync(path);

        Assert.Equal((40, 30), (page.Width, page.Height));
    }

    [Fact]
    public async Task Merging_webp_jpeg_and_tiff_keeps_every_page_in_order_and_the_originals_untouched()
    {
        using var tmp = new TempDir();
        var tiff = tmp.WriteFile("scan.tif", Fixtures.PdfSourceTiff(3, 40, 80));                       // 3 portrait pages
        var webp = tmp.WriteFile("shot.webp", Fixtures.Webp(64, 48));                                  // landscape
        var jpeg = tmp.WriteFile("photo.jpg", Fixtures.PdfSourceJpeg(48, 64));                         // portrait
        var wide = tmp.WriteFile("wide.webp", WebpFixtures.Solid(90, 30, new SKColor(40, 40, 200)));   // landscape

        using var guard = OriginalsGuard.Watch(tiff, webp, jpeg, wide);
        var output = await PdfConverter.MergeImagesAsync([tiff, webp, jpeg, wide], default);

        var pages = PdfCheck.Read(output);
        Assert.Equal(6, pages.Count);
        Assert.Equal(new[] { false, false, false, true, false, true }, pages.Select(p => p.Width > p.Height).ToArray());
        Assert.All(pages, p => Assert.Equal(1, p.Images));
        guard.AssertUnchanged();
    }
}
