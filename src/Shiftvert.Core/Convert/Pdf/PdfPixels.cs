using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Shiftvert.Core.Pdf;

/// <summary>
/// Small WIC helpers private to the PDF package (decode to pixels, JPEG-encode, downscale, orient, flatten on white).
/// Pixel buffers are either 4 channels (BGRA, 8 bits) or 1 channel (Gray8).
/// </summary>
internal static class PdfPixels
{
    /// <summary>A decoded 8-bit bitmap: <see cref="Channels"/> is 4 (BGRA) or 1 (gray).</summary>
    internal readonly record struct Raster(byte[] Data, int Width, int Height, int Channels);

    // ------------------------------------------------------------------ decode

    /// <summary>
    /// Decodes <paramref name="frame"/> to 8-bit pixels, scaled (Fant) so its long side is at most <paramref name="maxLongSide"/>
    /// (0 = no limit). Never applies EXIF orientation (see <see cref="ApplyOrientation"/>).
    /// </summary>
    public static async Task<Raster> DecodeFrameAsync(
        BitmapFrame frame, int channels, int maxLongSide, bool colorManage)
    {
        int w = (int)frame.PixelWidth, h = (int)frame.PixelHeight;
        var (nw, nh) = FitLongSide(w, h, maxLongSide);

        var transform = new BitmapTransform();
        if (nw != w || nh != h)
        {
            transform.ScaledWidth = (uint)nw;
            transform.ScaledHeight = (uint)nh;
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        var provider = await frame.GetPixelDataAsync(
            channels == 1 ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8,
            channels == 1 ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            colorManage ? ColorManagementMode.ColorManageToSRgb : ColorManagementMode.DoNotColorManage);

        return new Raster(provider.DetachPixelData(), nw, nh, channels);
    }

    /// <summary>The EXIF/TIFF orientation (1-8) of a frame; 1 when absent or unreadable.</summary>
    public static async Task<int> ReadOrientationAsync(BitmapFrame frame)
    {
        try
        {
            var props = await frame.BitmapProperties.GetPropertiesAsync(["System.Photo.Orientation"]);
            if (props.TryGetValue("System.Photo.Orientation", out var v) && v.Value is ushort o && o is >= 1 and <= 8)
                return o;
        }
        catch (Exception)
        {
            // No metadata: upright.
        }
        return 1;
    }

    // ------------------------------------------------------------------ encode

    /// <summary>Encodes pixels as a baseline JPEG at <paramref name="quality"/> (0..1). BGRA alpha is ignored.</summary>
    public static async Task<byte[]> EncodeJpegAsync(Raster r, double quality)
    {
        using var ms = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue((float)quality, Windows.Foundation.PropertyType.Single) },
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms, options);
        encoder.SetPixelData(
            r.Channels == 1 ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)r.Width, (uint)r.Height, 96, 96, r.Data);
        await encoder.FlushAsync();
        return await ReadAllAsync(ms);
    }

    /// <summary>Encodes pixels as a PNG (used when a JPEG of the same page is not wanted).</summary>
    public static async Task<byte[]> EncodePngAsync(Raster r)
    {
        using var ms = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
        encoder.SetPixelData(
            r.Channels == 1 ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)r.Width, (uint)r.Height, 96, 96, r.Data);
        await encoder.FlushAsync();
        return await ReadAllAsync(ms);
    }

    /// <summary>Decodes an image from memory (any WIC format) and returns its first frame as pixels.</summary>
    public static async Task<Raster> DecodeBytesAsync(byte[] data, int channels, int maxLongSide, bool colorManage)
    {
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(data.AsBuffer());
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        var frame = await decoder.GetFrameAsync(0);
        return await DecodeFrameAsync(frame, channels, maxLongSide, colorManage);
    }

    public static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ pixel maths

    /// <summary>Dimensions with the long side limited to <paramref name="maxLongSide"/> (0 = unlimited), never below 1 px.</summary>
    public static (int Width, int Height) FitLongSide(int w, int h, int maxLongSide)
    {
        var longSide = Math.Max(w, h);
        if (maxLongSide <= 0 || longSide <= maxLongSide) return (w, h);
        var s = (double)maxLongSide / longSide;
        return (Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)));
    }

    /// <summary>Composites BGRA over white and forces alpha to 255 (in place).</summary>
    public static void FlattenOnWhite(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255) continue;
            bgra[i] = (byte)((bgra[i] * a + 255 * (255 - a) + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 255 * (255 - a) + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 255 * (255 - a) + 127) / 255);
            bgra[i + 3] = 255;
        }
    }

    /// <summary>Box-filter (area average) downscale of 8-bit interleaved pixels.</summary>
    public static Raster Downscale(Raster src, int newWidth, int newHeight)
    {
        if (newWidth >= src.Width && newHeight >= src.Height) return src;
        newWidth = Math.Max(1, Math.Min(newWidth, src.Width));
        newHeight = Math.Max(1, Math.Min(newHeight, src.Height));

        var ch = src.Channels;
        var dst = new byte[newWidth * newHeight * ch];
        for (var y = 0; y < newHeight; y++)
        {
            var y0 = (int)((long)y * src.Height / newHeight);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * src.Height / newHeight));
            for (var x = 0; x < newWidth; x++)
            {
                var x0 = (int)((long)x * src.Width / newWidth);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * src.Width / newWidth));
                var count = (x1 - x0) * (y1 - y0);
                for (var c = 0; c < ch; c++)
                {
                    long sum = 0;
                    for (var yy = y0; yy < y1; yy++)
                    {
                        var row = (yy * src.Width + x0) * ch + c;
                        for (var xx = x0; xx < x1; xx++, row += ch)
                            sum += src.Data[row];
                    }
                    dst[(y * newWidth + x) * ch + c] = (byte)((sum + count / 2) / count);
                }
            }
        }
        return new Raster(dst, newWidth, newHeight, ch);
    }

    /// <summary>Applies an EXIF orientation (1-8) to pixels, returning an upright raster.</summary>
    public static Raster ApplyOrientation(Raster src, int orientation)
    {
        if (orientation is < 2 or > 8) return src;

        int w = src.Width, h = src.Height, ch = src.Channels;
        var swap = orientation >= 5;
        int ow = swap ? h : w, oh = swap ? w : h;
        var dst = new byte[src.Data.Length];

        for (var oy = 0; oy < oh; oy++)
        {
            for (var ox = 0; ox < ow; ox++)
            {
                int sx, sy;
                switch (orientation)
                {
                    case 2: sx = w - 1 - ox; sy = oy; break;
                    case 3: sx = w - 1 - ox; sy = h - 1 - oy; break;
                    case 4: sx = ox; sy = h - 1 - oy; break;
                    case 5: sx = oy; sy = ox; break;
                    case 6: sx = oy; sy = h - 1 - ox; break;
                    case 7: sx = w - 1 - oy; sy = h - 1 - ox; break;
                    default: sx = w - 1 - oy; sy = ox; break; // 8
                }
                System.Buffer.BlockCopy(src.Data, (sy * w + sx) * ch, dst, (oy * ow + ox) * ch, ch);
            }
        }
        return new Raster(dst, ow, oh, ch);
    }
}
