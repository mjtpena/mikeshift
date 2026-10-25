using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// The image fixtures of the integrity suite, with the ground truth each conversion is judged against.
/// <para>Truth is decoded independently of the converters (SkiaSharp where it can read the format, otherwise WIC driven directly
/// from the test) and is also compared with the pixels the fixture generator drew, so the oracle itself is checked.</para>
/// <para>Two kinds of picture: small hard-edged four-colour quadrant pictures (they prove orientation, exactness of the lossless
/// paths and palette handling) and a photograph-like scene (it carries the "lossy output is still a good picture" thresholds).</para>
/// </summary>
internal sealed record ImgSource(
    string Id, string FileName, int Frames, bool Animated, bool Lossy, bool HasAlpha, int ExpectW, int ExpectH, bool HasExif,
    bool Photo = false, int Slack = 0)
{
    public string Ext => Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();

    public override string ToString() => Id;

    /// <summary>All sources; HEIC only when this PC can encode it (it is generated through WIC's HEIF encoder).</summary>
    public static readonly string[] Ids =
        ["png-alpha", "jpg-exif6", "gif", "gif-anim", "tiff-multi", "bmp", "webp", "heic", "photo-png", "photo-jpg"];

    public static ImgSource Get(string id) => id switch
    {
        "png-alpha" => new(id, "alpha.png", 1, false, false, true, 64, 48, false),
        "jpg-exif6" => new(id, "rotated.jpg", 1, false, true, false, 80, 120, true), // stored 120x80, Orientation 6 -> upright 80x120
        "gif" => new(id, "static.gif", 1, false, false, false, 64, 48, false),
        "gif-anim" => new(id, "anim.gif", 2, true, false, false, 64, 48, false),
        "tiff-multi" => new(id, "pages.tif", 3, false, false, false, 64, 48, false),
        "bmp" => new(id, "plain.bmp", 1, false, false, false, 64, 48, false),
        // WIC's WebP decoder and libwebp round a few values differently (1 level); the converters read WebP through WIC.
        "webp" => new(id, "lossless.webp", 1, false, false, false, 64, 48, false, Slack: 1),
        "heic" => new(id, "photo.heic", 1, false, true, false, 64, 48, false),
        "photo-png" => new(id, "scene.png", 1, false, false, false, 640, 480, false, Photo: true),
        "photo-jpg" => new(id, "scene.jpg", 1, false, true, false, 640, 480, false, Photo: true),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>The bytes of the fixture (HEIC returns null if this PC has no HEIF encoder).</summary>
    public byte[]? Make() => Id switch
    {
        "png-alpha" => Fixtures.PngAlpha(64, 48),
        "jpg-exif6" => Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: true, xmp: true, photoshop: true),
        "gif" => Fixtures.Gif(64, 48),
        "gif-anim" => Fixtures.Gif(64, 48, animated: true),
        "tiff-multi" => Fixtures.TiffMultiFrame(64, 48, 3),
        "bmp" => Fixtures.Bmp(64, 48),
        "webp" => Fixtures.Webp(64, 48),
        "heic" => EncodeHeic(),
        "photo-png" => SkiaEncode(SoftPhotoBgra(640, 480), 640, 480, png: true),
        "photo-jpg" => SkiaEncode(SoftPhotoBgra(640, 480), 640, 480, png: false),
        _ => null,
    };

    /// <summary>A photograph-like picture: smooth colour gradients and fine grain (deterministic). Compresses well as JPEG, badly as PNG.</summary>
    public static byte[] SoftPhotoBgra(int w, int h)
    {
        var rnd = new Random(11);
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var n = rnd.Next(-4, 5);
                px[i] = (byte)Math.Clamp(60 + 140 * y / h + n, 0, 255);
                px[i + 1] = (byte)Math.Clamp(40 + 170 * x / w + n, 0, 255);
                px[i + 2] = (byte)Math.Clamp(200 - 120 * (x + y) / (w + h) + n, 0, 255);
                px[i + 3] = 255;
            }
        return px;
    }

    private static byte[] SkiaEncode(byte[] bgra, int w, int h, bool png)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var pm = bmp.PeekPixels();
        using var data = (png ? pm.Encode(new SKPngEncoderOptions()) : pm.Encode(new SKJpegEncoderOptions(97)))!;
        return data.ToArray();
    }

    private static byte[]? EncodeHeic()
    {
        if (!Kit.Caps.HeifEncode) return null;
        return Task.Run(async () =>
        {
            using var ms = new InMemoryRandomAccessStream();
            var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(0.95f, Windows.Foundation.PropertyType.Single) } };
            var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, ms, props);
            enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 64, 48, 96, 96, Fixtures.QuadrantsBgra(64, 48));
            await enc.FlushAsync();
            ms.Seek(0);
            using var output = new MemoryStream();
            await ms.AsStreamForRead().CopyToAsync(output);
            return output.ToArray();
        }).GetAwaiter().GetResult();
    }

    /// <summary>The picture the generator drew (before any lossy encoding), or null for the rotated JPEG whose truth is semantic.</summary>
    private Px? Generated()
    {
        if (Id == "jpg-exif6") return null;
        if (Photo) return new Px(640, 480, SoftPhotoBgra(640, 480));
        var quad = new Px(64, 48, Fixtures.QuadrantsBgra(64, 48));
        if (Id == "png-alpha")
            for (var y = 0; y < 24; y++)
                for (var x = 0; x < 32; x++)
                    quad.Bgra[(y * 64 + x) * 4 + 3] = 0;
        return quad;
    }

    /// <summary>The pixels a correct converter must reproduce (upright, frame 0), decoded independently and sanity-checked.</summary>
    public async Task<Px> TruthAsync(string path)
    {
        var skia = PixDecode.Skia(path);
        var px = skia ?? await PixDecode.WicAsync(path);

        // The oracle must itself be right: compare with what the generator drew.
        Assert.Equal((ExpectW, ExpectH), (px.W, px.H));
        var expected = Generated();
        if (expected is not null)
        {
            if (Lossy)
                Assert.True(PixMetrics.Psnr(px, expected) >= (Photo ? 35 : 22), $"{Id}: the fixture does not look like the picture that was drawn");
            else
                Assert.True(PixMetrics.MaxDiff(px, expected) <= Slack, $"{Id}: independent decode differs from the generator's pixels");
        }
        else
        {
            AssertRotatedQuadrants(px);
        }
        return px;
    }

    /// <summary>
    /// Orientation 6 means "rotate 90 degrees clockwise to display": the stored red top-left quadrant ends up top-right, green
    /// bottom-right, blue top-left, yellow bottom-left.
    /// </summary>
    public static void AssertRotatedQuadrants(Px px)
    {
        Assert.Equal((80, 120), (px.W, px.H));
        AssertNear(px, 20, 30, (30, 30, 220), "top-left should be blue");
        AssertNear(px, 60, 30, (220, 30, 30), "top-right should be red");
        AssertNear(px, 20, 90, (230, 230, 30), "bottom-left should be yellow");
        AssertNear(px, 60, 90, (30, 200, 30), "bottom-right should be green");
    }

    private static void AssertNear(Px px, int x, int y, (int R, int G, int B) want, string what)
    {
        var p = px.At(x, y);
        Assert.True(Math.Abs(p.R - want.R) < 40 && Math.Abs(p.G - want.G) < 40 && Math.Abs(p.B - want.B) < 40,
            $"upright orientation wrong: {what}; got R{p.R} G{p.G} B{p.B}");
    }
}
