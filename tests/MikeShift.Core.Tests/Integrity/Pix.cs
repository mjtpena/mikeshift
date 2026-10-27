using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>Decoded 8-bit BGRA pixels, straight (non-premultiplied) alpha, upright.</summary>
internal sealed record Px(int W, int H, byte[] Bgra)
{
    public (int B, int G, int R, int A) At(int x, int y)
    {
        var i = (y * W + x) * 4;
        return (Bgra[i], Bgra[i + 1], Bgra[i + 2], Bgra[i + 3]);
    }

    public bool HasAlpha
    {
        get
        {
            for (var i = 3; i < Bgra.Length; i += 4) if (Bgra[i] != 255) return true;
            return false;
        }
    }

    /// <summary>The sub-rectangle <paramref name="x"/>,<paramref name="y"/>,<paramref name="w"/>,<paramref name="h"/>.</summary>
    public Px Crop(int x, int y, int w, int h)
    {
        var o = new byte[w * h * 4];
        for (var r = 0; r < h; r++) System.Buffer.BlockCopy(Bgra, ((y + r) * W + x) * 4, o, r * w * 4, w * 4);
        return new Px(w, h, o);
    }

    /// <summary>Composites over opaque white (what JPEG / BMP / PDF output does with transparency).</summary>
    public Px OnWhite()
    {
        var o = new byte[Bgra.Length];
        for (var i = 0; i < Bgra.Length; i += 4)
        {
            int a = Bgra[i + 3];
            for (var c = 0; c < 3; c++) o[i + c] = (byte)((Bgra[i + c] * a + 255 * (255 - a) + 127) / 255);
            o[i + 3] = 255;
        }
        return new Px(W, H, o);
    }

    /// <summary>Exact 2x downscale by averaging 2x2 blocks (even sizes only), the "ideal" half-size picture.</summary>
    public Px HalfBox()
    {
        var w = W / 2;
        var h = H / 2;
        var o = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                for (var c = 0; c < 4; c++)
                {
                    var s = Bgra[((2 * y) * W + 2 * x) * 4 + c] + Bgra[((2 * y) * W + 2 * x + 1) * 4 + c]
                            + Bgra[((2 * y + 1) * W + 2 * x) * 4 + c] + Bgra[((2 * y + 1) * W + 2 * x + 1) * 4 + c];
                    o[(y * w + x) * 4 + c] = (byte)((s + 2) / 4);
                }
        return new Px(w, h, o);
    }

    /// <summary>Mean colour of a rectangle (alpha ignored, pixels composited on white first).</summary>
    public (double B, double G, double R) Mean(int x0, int y0, int w, int h)
    {
        double b = 0, g = 0, r = 0;
        var white = OnWhite();
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var p = white.At(x, y);
                b += p.B; g += p.G; r += p.R;
            }
        var n = (double)w * h;
        return (b / n, g / n, r / n);
    }
}

/// <summary>Pixel comparison metrics.</summary>
internal static class PixMetrics
{
    /// <summary>PSNR over the RGB channels after compositing both on white; +infinity when identical. Sizes must match.</summary>
    public static double Psnr(Px a, Px b)
    {
        Assert.Equal((a.W, a.H), (b.W, b.H));
        var x = a.OnWhite().Bgra;
        var y = b.OnWhite().Bgra;
        double se = 0;
        long n = 0;
        for (var i = 0; i < x.Length; i += 4)
            for (var c = 0; c < 3; c++)
            {
                double d = x[i + c] - y[i + c];
                se += d * d;
                n++;
            }
        if (se == 0) return double.PositiveInfinity;
        return 10 * Math.Log10(255.0 * 255.0 / (se / n));
    }

    /// <summary>Largest absolute channel difference over BGRA (alpha included only where either pixel is not opaque).</summary>
    public static int MaxDiff(Px a, Px b, bool includeAlpha = true)
    {
        Assert.Equal((a.W, a.H), (b.W, b.H));
        var max = 0;
        for (var i = 0; i < a.Bgra.Length; i += 4)
        {
            var transparentBoth = a.Bgra[i + 3] == 0 && b.Bgra[i + 3] == 0;
            for (var c = 0; c < 3 && !transparentBoth; c++) max = Math.Max(max, Math.Abs(a.Bgra[i + c] - b.Bgra[i + c]));
            if (includeAlpha) max = Math.Max(max, Math.Abs(a.Bgra[i + 3] - b.Bgra[i + 3]));
        }
        return max;
    }
}

/// <summary>
/// Image decoders that are independent of how the converters wrote their output: SkiaSharp (libpng/libjpeg-turbo/libgif/libwebp)
/// and Windows' own WIC decoders driven directly. Both return upright pixels.
/// </summary>
internal static class PixDecode
{
    /// <summary>EXIF-style origin application (written from the EXIF spec, independent of the product).</summary>
    private static Px Orient(Px src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return src;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? src.H : src.W;
        var h = swap ? src.W : src.H;
        var o = new byte[w * h * 4];
        for (var y = 0; y < src.H; y++)
            for (var x = 0; x < src.W; x++)
            {
                (int dx, int dy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (src.W - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (src.W - 1 - x, src.H - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, src.H - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (src.H - 1 - y, x),
                    SKEncodedOrigin.RightBottom => (src.H - 1 - y, src.W - 1 - x),
                    SKEncodedOrigin.LeftBottom => (y, src.W - 1 - x),
                    _ => (x, y),
                };
                System.Buffer.BlockCopy(src.Bgra, (y * src.W + x) * 4, o, (dy * w + dx) * 4, 4);
            }
        return new Px(w, h, o);
    }

    /// <summary>SkiaSharp decode of the first frame, with EXIF orientation applied. Null if Skia cannot decode the format (HEIC).</summary>
    public static Px? Skia(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bytes = new byte[info.Width * info.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var r = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (r != SKCodecResult.Success) throw new Xunit.Sdk.XunitException($"SkiaSharp could not decode {Path.GetFileName(path)}: {r}");
        }
        finally { handle.Free(); }
        return Orient(new Px(info.Width, info.Height, bytes), codec.EncodedOrigin);
    }

    public static int SkiaFrameCount(string path)
    {
        using var codec = SKCodec.Create(path);
        return codec is null ? 0 : Math.Max(1, codec.FrameCount);
    }

    private static readonly Lazy<HashSet<string>> s_wicExtensions = new(() => BitmapDecoder.GetDecoderInformationEnumerator()
        .SelectMany(d => d.FileExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase));

    // A listed decoder is not proof: the windows-latest runner lists a WebP decoder whose CreateAsync then throws an empty COMException.
    private static readonly Lazy<bool> s_wicDecodesWebp = new(() =>
    {
        try
        {
            using var ms = new InMemoryRandomAccessStream();
            ms.WriteAsync(Fixtures.Webp(4, 4).AsBuffer()).AsTask().GetAwaiter().GetResult();
            ms.Seek(0);
            var decoder = BitmapDecoder.CreateAsync(ms).AsTask().GetAwaiter().GetResult();
            using var bitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
            return bitmap.PixelWidth == 4;
        }
        catch (Exception)
        {
            return false;
        }
    });

    /// <summary>
    /// True when WIC really decodes <paramref name="extension"/> (".webp"): a decoder is listed for it and, for WebP, a tiny WebP
    /// actually decodes. WebP needs the free WebP Image Extension, which a desktop PC usually has; on the CI runner (Windows Server)
    /// it is missing or listed but not working.
    /// </summary>
    public static bool WicDecodes(string extension) =>
        s_wicExtensions.Value.Contains(extension) && (!extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) || s_wicDecodesWebp.Value);

    /// <summary>
    /// True when WIC can read the file at <paramref name="path"/>, judged by what it really is (its magic numbers). Only WebP can be
    /// missing; every other format these tests write has a decoder built into Windows.
    /// </summary>
    public static bool WicReads(string path) => ImageBytes.Sniff(path) != "webp" || WicDecodes(".webp");

    /// <summary>WIC (via the WinRT <c>BitmapDecoder</c>): upright pixels of frame <paramref name="frame"/>, no colour management, EXIF orientation applied.</summary>
    public static async Task<Px> WicAsync(string path, int frame = 0)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync(bytes.AsBuffer());
        ms.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ms);
        var f = await decoder.GetFrameAsync((uint)frame);
        var data = await f.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var px = data.DetachPixelData();
        var w = (int)f.OrientedPixelWidth;
        var h = (int)f.OrientedPixelHeight;
        Assert.Equal(w * h * 4, px.Length);
        return new Px(w, h, px);
    }

    public static async Task<(int Frames, uint Width, uint Height, string Mime)> WicInfoAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync(bytes.AsBuffer());
        ms.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ms);
        return ((int)decoder.FrameCount, decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, decoder.DecoderInformation.CodecId.ToString());
    }

    /// <summary>Reads named WIC metadata properties (e.g. System.GPS.Latitude); a missing one is simply absent.</summary>
    public static async Task<IDictionary<string, object?>> WicPropertiesAsync(string path, params string[] names)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync(bytes.AsBuffer());
        ms.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ms);
        var result = new Dictionary<string, object?>();
        foreach (var n in names)
        {
            try
            {
                var set = await decoder.BitmapProperties.GetPropertiesAsync([n]);
                if (set.TryGetValue(n, out var v)) result[n] = v.Value;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
            {
                // The container has no such property: that is the answer we are after.
            }
        }
        return result;
    }

    /// <summary>
    /// Decodes with both libraries and asserts they agree (within <paramref name="tolerance"/> per channel, which is 0 for
    /// lossless formats and a few levels for JPEG whose IDCT differs between libraries), returning the Skia pixels.
    /// WIC-only formats (HEIC) return the WIC pixels; WebP on a PC without a WIC WebP decoder returns the Skia pixels alone.
    /// </summary>
    public static async Task<Px> BothAsync(string path, int tolerance)
    {
        var skia = Skia(path);
        if (skia is not null && !WicReads(path)) return skia;
        var wic = await WicAsync(path);
        if (skia is null) return wic;
        Assert.Equal((skia.W, skia.H), (wic.W, wic.H));
        var diff = PixMetrics.MaxDiff(skia, wic, includeAlpha: true);
        Assert.True(diff <= tolerance, $"{Path.GetFileName(path)}: WIC and SkiaSharp disagree by {diff} levels (allowed {tolerance}).");
        return skia;
    }
}
