using MikeShift.Core;
using MikeShift.Core.Images;

namespace MikeShift.Cli.Tests;

/// <summary>Image actions end to end through the real executable (exercises WIC + SkiaSharp in a child process).</summary>
public class ImageCliTests
{
    [Fact]
    public void List_on_a_png_shows_the_image_actions_except_png_itself()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var r = CliProcess.Run("--list", png);

        Assert.Equal(0, r.ExitCode);
        var titles = r.Stdout.Trim().Split(", ");
        Assert.DoesNotContain("PNG", titles);
        foreach (var expected in new[] { "JPG", "TIFF", "GIF", "BMP", "PDF", "Compress", "Resize 50%", "Crop", "Strip EXIF", "Zip" })
            Assert.Contains(expected, titles);
        Assert.Equal("Zip", titles[^1]);
    }

    [Fact]
    public void List_on_a_misnamed_png_still_sees_an_image()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--list", tmp.WriteFile("really-a-png.jpg", Fixtures.PngRgb()));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Compress", r.Stdout);
    }

    [Fact]
    public void List_on_unreadable_image_offers_only_zip()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--list", tmp.WriteFile("broken.png", Fixtures.Noise(300)));
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("Zip", r.Stdout.Trim());
    }

    [Fact]
    public void Run_png_on_a_jpg_prints_an_existing_png_path()
    {
        using var tmp = new TempDir();
        var jpg = tmp.WriteFile("holiday.jpg", Fixtures.QuadrantJpeg(64, 48));
        using var guard = OriginalsGuard.Watch(jpg);

        var r = CliProcess.Run("--run", "PNG", jpg);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("holiday.png"), output);
        Assert.True(File.Exists(output));
        Assert.Equal(0x89, File.ReadAllBytes(output)[0]);
    }

    [Fact]
    public void Run_with_a_mix_skips_files_already_in_the_target_format()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb());
        var bmp = tmp.WriteFile("b.bmp", Fixtures.Bmp());

        var r = CliProcess.Run("--run", "png", png, bmp);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal([tmp.Combine("b.png")], r.StdoutLines);
        Assert.Contains("1 already PNG", r.Stderr);
    }

    [Fact]
    public void Run_resize_crop_and_strip_use_their_suffixes()
    {
        using var tmp = new TempDir();
        var jpg = tmp.WriteFile("photo.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 1, gps: true));

        var resized = CliProcess.Run("--run", "Resize 50%", jpg);
        var cropped = CliProcess.Run("--run", "Crop", "--crop", "10,10,60,40@30x20", jpg);
        var clean = CliProcess.Run("--run", "Strip EXIF", jpg);

        Assert.Equal(0, resized.ExitCode);
        Assert.Equal(tmp.Combine("photo 50%.jpg"), Assert.Single(resized.StdoutLines));
        Assert.Equal(0, cropped.ExitCode);
        Assert.Equal(tmp.Combine("photo cropped.jpg"), Assert.Single(cropped.StdoutLines));
        Assert.Equal(0, clean.ExitCode);
        Assert.Equal(tmp.Combine("photo clean.jpg"), Assert.Single(clean.StdoutLines));
        Assert.False(JpegMeta.HasMetadataSegments(File.ReadAllBytes(clean.StdoutLines[0])));
    }

    [SkipIfNoCapability(nameof(Capabilities.FaceDetect))]
    public void Run_blur_faces_on_a_picture_without_faces_exits_1_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("scenery.png", Fixtures.PngRgb(320, 240));

        var listed = CliProcess.Run("--list", png);
        var r = CliProcess.Run("--run", "Blur faces", png);

        Assert.Contains("Blur faces", listed.Stdout.Trim().Split(", "));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("error: No faces found", r.Stderr);
        Assert.Equal(["scenery.png"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Run_compress_on_a_tiny_image_exits_1_and_says_why()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Compress", tmp.WriteFile("tiny.jpg", Fixtures.QuadrantJpeg(16, 12, quality: 30)));
        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Stdout);
        Assert.Contains("already small", r.Stderr);
    }

    [Fact]
    public void Run_compress_on_an_animated_gif_is_refused()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "Compress", tmp.WriteFile("anim.gif", Fixtures.Gif(64, 48, animated: true)));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("Animated", r.Stderr);
    }

    [Fact]
    public void Run_on_a_300_megapixel_header_is_refused()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "JPG", tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000)));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("too large", r.Stderr);
    }

    [Fact]
    public void Run_webp_writes_a_webp_file()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "WebP", tmp.WriteFile("pic.png", Fixtures.PngRgb()));
        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("pic.webp"), output);
        var head = File.ReadAllBytes(output);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(head, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(head, 8, 4));
    }

    [Fact]
    public void Run_ico_writes_an_icon_and_says_when_the_picture_is_small()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run("--run", "ICO", tmp.WriteFile("logo.png", Fixtures.PngRgb()));
        Assert.Equal(0, r.ExitCode);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("logo.ico"), output);
        var head = File.ReadAllBytes(output);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 7, 0 }, head[..6]);
        Assert.Contains("note: ", r.Stderr);
        Assert.Contains("256 px", r.Stderr);
    }

    [Fact]
    public void Run_every_image_fixture_through_png_or_jpg()
    {
        using var tmp = new TempDir();
        Fixtures.WriteAll(tmp.CreateDir("fx"));
        var images = new[] { "rgb.png", "alpha.png", "photo.jpg", "exif-gps.jpg", "exif-rotated.jpg", "static.gif", "animated.gif", "pages.tif", "sample.bmp", "sample.webp" };
        foreach (var name in images)
        {
            var path = tmp.Combine("fx", name);
            var target = name.EndsWith(".png", StringComparison.Ordinal) ? "JPG" : "PNG";
            var r = CliProcess.Run("--run", target, path);
            Assert.True(r.ExitCode == 0, $"{name}: {r.Stderr}");
            Assert.True(File.Exists(Assert.Single(r.StdoutLines)), name);
        }
    }
}
