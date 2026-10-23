using MikeShift.Core;
using MikeShift.Core.Images;

namespace MikeShift.Core.Tests.Images;

public class ImageProbeTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("tif")]
    [InlineData("bmp")]
    [InlineData("webp")]
    [InlineData("ico")]
    public void Real_images_can_be_decoded(string ext)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("x." + ext, Fixtures.ImageFor(ext)!);
        Assert.True(ImageProbe.CanDecode(path));
    }

    [Fact]
    public void Decoder_is_chosen_by_content_not_by_extension()
    {
        using var tmp = new TempDir();
        Assert.True(ImageProbe.CanDecode(tmp.WriteFile("really-a-png.jpg", Fixtures.PngRgb())));
        Assert.True(ImageProbe.CanDecode(tmp.WriteFile("really-a-jpeg.png", Fixtures.QuadrantJpeg())));
        Assert.True(ImageProbe.CanDecode(tmp.WriteFile("no-extension", Fixtures.PngRgb())));
    }

    [Fact]
    public void Junk_empty_and_missing_files_cannot_be_decoded()
    {
        using var tmp = new TempDir();
        Assert.False(ImageProbe.CanDecode(tmp.WriteFile("noise.png", Fixtures.Noise(2048))));
        Assert.False(ImageProbe.CanDecode(tmp.WriteFile("empty.jpg", [])));
        Assert.False(ImageProbe.CanDecode(tmp.WriteFile("text.gif", "GIF but not really")));
        Assert.False(ImageProbe.CanDecode(tmp.Combine("missing.png")));
        Assert.False(ImageProbe.CanDecode(tmp.CreateDir("folder.png")));
    }

    [Fact]
    public void Probe_does_not_read_pixels()
    {
        // A header-only 300 MP PNG is "decodable" for classification purposes: the size guard belongs to the converter.
        using var tmp = new TempDir();
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(ImageProbe.CanDecode(tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000))));
        Assert.True(started.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void Kind_classifies_images_through_the_probe()
    {
        using var tmp = new TempDir();
        Assert.Equal(Category.Image, Kind.Of(tmp.WriteFile("a.jpg", Fixtures.PngRgb())));
        Assert.Equal(Category.Other, Kind.Of(tmp.WriteFile("b.jpg", Fixtures.Noise(300))));
    }

    [Fact]
    public void Capability_probe_reports_webp_and_leaves_other_flags_alone()
    {
        var start = Capabilities.None with { WordCom = true, Mp3Encode = true };
        var probed = ImageProbe.ProbeCapabilities(start);

        Assert.True(probed.WebpEncode); // SkiaSharp ships with the app
        Assert.True(probed.WordCom);
        Assert.True(probed.Mp3Encode);
        Assert.False(probed.H264Encode);
    }

    [Fact]
    public async Task Capability_probe_is_consistent_with_what_the_converter_can_really_do()
    {
        var caps = ImageProbe.ProbeCapabilities(Capabilities.None);
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb());
        if (caps.HeifEncode)
            Assert.True(File.Exists(await ImageConverter.ToFormatAsync(png, ImageFormat.Heic, default)));
        if (caps.WebpEncode)
            Assert.True(File.Exists(await ImageConverter.ToFormatAsync(png, ImageFormat.Webp, default)));
    }

    [Fact]
    public void Current_capabilities_include_the_image_probe()
    {
        Assert.Equal(ImageProbe.ProbeCapabilities(Capabilities.None).WebpEncode, Capabilities.Current.WebpEncode);
        Assert.Equal(ImageProbe.ProbeCapabilities(Capabilities.None).HeifEncode, Capabilities.Current.HeifEncode);
    }
}
