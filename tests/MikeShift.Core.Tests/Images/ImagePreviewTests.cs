using MikeShift.Core;
using MikeShift.Core.Images;
using MikeShift.Core.Tests.Integrity;

namespace MikeShift.Core.Tests.Images;

public class ImagePreviewTests
{
    [Fact]
    public async Task A_webp_preview_has_the_picture_at_full_size_when_it_fits()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.webp", Fixtures.Webp(64, 48));

        var px = await ImagePreview.DecodeAsync(path, 1600);

        Assert.Equal((64, 48, 64, 48), (px.Width, px.Height, px.ImageWidth, px.ImageHeight));
        var reference = PixDecode.Skia(path)!;
        Assert.True(PixMetrics.MaxDiff(reference, new Px(px.Width, px.Height, px.Bgra)) <= 1, "the preview is not the picture");
    }

    [Fact]
    public async Task A_preview_is_scaled_down_to_the_long_side_and_upright()
    {
        using var tmp = new TempDir();

        var webp = await ImagePreview.DecodeAsync(tmp.WriteFile("a.webp", Fixtures.Webp(64, 48)), 32);
        Assert.Equal((32, 24, 64, 48), (webp.Width, webp.Height, webp.ImageWidth, webp.ImageHeight));
        Assert.Equal(32 * 24 * 4, webp.Bgra.Length);

        var rotated = await ImagePreview.DecodeAsync(tmp.WriteFile("r.jpg", Fixtures.JpegWithMetadata(64, 32, orientation: 6)), 16);
        Assert.Equal((8, 16, 32, 64), (rotated.Width, rotated.Height, rotated.ImageWidth, rotated.ImageHeight));
    }

    [Fact]
    public async Task A_picture_that_cannot_be_read_has_no_preview()
    {
        using var tmp = new TempDir();
        await Assert.ThrowsAsync<ConversionException>(() => ImagePreview.DecodeAsync(tmp.WriteFile("junk.webp", Fixtures.Noise(400)), 1600));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ImagePreview.DecodeAsync(tmp.WriteFile("a.webp", Fixtures.Webp(8, 8)), 0));
    }
}
