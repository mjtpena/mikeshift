using MikeShift.Core;
using MikeShift.Core.Images;
using MikeShift.Core.Tests.Integrity;
using SkiaSharp;

namespace MikeShift.Core.Tests.Images;

/// <summary>Edge cases of the preview decoder behind the crop editor.</summary>
public class ImagePreviewEdgeTests
{
    [Theory]
    [InlineData(4000, 8, 100, 1)]
    [InlineData(8, 4000, 1, 100)]
    public async Task A_very_wide_or_tall_picture_never_previews_with_a_zero_side(int w, int h, int expectedW, int expectedH)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("strip.webp", WebpFixtures.Solid(w, h, new SKColor(200, 80, 20)));

        var px = await ImagePreview.DecodeAsync(path, 100);

        Assert.Equal((expectedW, expectedH, w, h), (px.Width, px.Height, px.ImageWidth, px.ImageHeight));
        Assert.Equal(expectedW * expectedH * 4, px.Bgra.Length);
    }

    [Fact]
    public async Task A_picture_with_alpha_previews_with_straight_alpha()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.png", Fixtures.PdfSourcePng(64, 48, alpha: true));

        var px = await ImagePreview.DecodeAsync(path, 1600);

        var reference = await PixDecode.WicAsync(path);
        var preview = new Px(px.Width, px.Height, px.Bgra);
        Assert.Equal(90, preview.At(60, 10).A);
        Assert.True(PixMetrics.MaxDiff(reference, preview) <= 2, "colours were premultiplied or altered");
    }

    [Fact]
    public async Task A_webp_with_alpha_previews_with_straight_alpha()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.webp", WebpFixtures.Solid(16, 16, new SKColor(200, 100, 50, 100)));

        var px = await ImagePreview.DecodeAsync(path, 1600);

        var p = new Px(px.Width, px.Height, px.Bgra).At(8, 8);
        Assert.Equal(100, p.A);
        Assert.InRange(p.R, 195, 205);
        Assert.InRange(p.G, 95, 105);
        Assert.InRange(p.B, 45, 55);
    }

    [Fact]
    public async Task A_cancelled_preview_stops_with_an_operation_cancelled_error()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.webp", Fixtures.Webp(64, 48));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImagePreview.DecodeAsync(path, 1600, cts.Token));
    }

    [Fact]
    public async Task A_picture_over_200_megapixels_has_no_preview()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImagePreview.DecodeAsync(path, 1600));

        Assert.Contains("200 megapixels", ex.Message);
    }
}
