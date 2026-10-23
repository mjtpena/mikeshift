using System.Runtime.InteropServices;
using SkiaSharp;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Images;

/// <summary>
/// Upright pixels in memory: 8-bit BGRA, straight (non-premultiplied) alpha, tightly packed rows.
/// All converters work on this one representation; WIC (decode/encode) and SkiaSharp (resampling, WebP) meet here.
/// </summary>
internal sealed class Raster
{
    public Raster(int width, int height, byte[] bgra)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (bgra.Length != checked(width * height * 4)) throw new ArgumentException("Pixel buffer has the wrong size.", nameof(bgra));
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    /// <summary>True if any pixel is not fully opaque ("real alpha").</summary>
    public bool HasRealAlpha
    {
        get
        {
            var p = Bgra;
            for (var i = 3; i < p.Length; i += 4)
                if (p[i] != 0xFF) return true;
            return false;
        }
    }

    /// <summary>True if every pixel's colour channels are zero (black, or fully transparent): what a failed HEIC/AVIF decode produces.</summary>
    public bool IsAllBlack()
    {
        var p = Bgra;
        for (var i = 0; i < p.Length; i += 4)
            if (p[i] != 0 || p[i + 1] != 0 || p[i + 2] != 0) return false;
        return true;
    }

    /// <summary>A copy composited over opaque white (for JPEG/BMP, which have no usable alpha).</summary>
    public Raster FlattenOnWhite()
    {
        var src = Bgra;
        var dst = new byte[src.Length];
        for (var i = 0; i < src.Length; i += 4)
        {
            int a = src[i + 3];
            if (a == 255)
            {
                dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
            }
            else
            {
                // out = src * a + white * (1 - a), with straight alpha.
                dst[i] = (byte)((src[i] * a + 255 * (255 - a) + 127) / 255);
                dst[i + 1] = (byte)((src[i + 1] * a + 255 * (255 - a) + 127) / 255);
                dst[i + 2] = (byte)((src[i + 2] * a + 255 * (255 - a) + 127) / 255);
            }
            dst[i + 3] = 0xFF;
        }
        return new Raster(Width, Height, dst);
    }

    /// <summary>
    /// Crops to <paramref name="src"/> and scales the result to <paramref name="outWidth"/> x <paramref name="outHeight"/>.
    /// High-quality interpolation: mip-mapped linear when shrinking, Mitchell cubic when enlarging, exact copy at 1:1.
    /// </summary>
    public Raster CropScale(MikeShift.Core.Actions.RectPx src, int outWidth, int outHeight)
    {
        if (src.X == 0 && src.Y == 0 && src.W == Width && src.H == Height && outWidth == Width && outHeight == Height)
            return this;

        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(Bgra, 0, bmp.GetPixels(), Bgra.Length);
        bmp.SetImmutable();
        using var image = SKImage.FromBitmap(bmp);

        var oneToOne = src.W == outWidth && src.H == outHeight;
        var shrinking = outWidth < src.W || outHeight < src.H;
        var sampling = oneToOne
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : shrinking
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKCubicResampler.Mitchell);

        var dstInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(dstInfo)
                            ?? throw new ConversionException("There isn't enough memory to process this image.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, SKRect.Create(src.X, src.Y, src.W, src.H), SKRect.Create(0, 0, outWidth, outHeight), sampling, paint);
        using var snapshot = surface.Snapshot();

        var outBytes = new byte[checked(outWidth * outHeight * 4)];
        var outInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(outBytes, GCHandleType.Pinned);
        try
        {
            if (!snapshot.ReadPixels(outInfo, handle.AddrOfPinnedObject(), outWidth * 4, 0, 0))
                throw new ConversionException("The image could not be resized.");
        }
        finally
        {
            handle.Free();
        }
        return new Raster(outWidth, outHeight, outBytes);
    }

    /// <summary>Encodes as lossy WebP (quality 0-100) with SkiaSharp.</summary>
    public byte[] EncodeWebp(int quality)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(Bgra, 0, bmp.GetPixels(), Bgra.Length);
        using var pixmap = bmp.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality))
                         ?? throw new ConversionException("The image could not be saved as WebP.");
        return data.ToArray();
    }

    /// <summary>Wraps a decoded SkiaSharp bitmap (any colour type) as a <see cref="Raster"/>.</summary>
    public static Raster FromSkia(SKBitmap source)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bytes = new byte[checked(source.Width * source.Height * 4)];
        using var image = SKImage.FromBitmap(source);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), source.Width * 4, 0, 0))
                throw new ConversionException("Couldn't read this image.");
        }
        finally
        {
            handle.Free();
        }
        return new Raster(source.Width, source.Height, bytes);
    }

    /// <summary>Applies an EXIF/codec origin (rotation/mirroring) so the pixels are upright.</summary>
    public Raster ApplyOrigin(SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return this;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? Height : Width;
        var h = swap ? Width : Height;
        var dst = new byte[Bgra.Length];
        var src = Bgra;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int dx, dy;
                switch (origin)
                {
                    case SKEncodedOrigin.TopRight: dx = Width - 1 - x; dy = y; break;
                    case SKEncodedOrigin.BottomRight: dx = Width - 1 - x; dy = Height - 1 - y; break;
                    case SKEncodedOrigin.BottomLeft: dx = x; dy = Height - 1 - y; break;
                    case SKEncodedOrigin.LeftTop: dx = y; dy = x; break;
                    case SKEncodedOrigin.RightTop: dx = Height - 1 - y; dy = x; break;
                    case SKEncodedOrigin.RightBottom: dx = Height - 1 - y; dy = Width - 1 - x; break;
                    case SKEncodedOrigin.LeftBottom: dx = y; dy = Width - 1 - x; break;
                    default: dx = x; dy = y; break;
                }
                Buffer.BlockCopy(src, (y * Width + x) * 4, dst, (dy * w + dx) * 4, 4);
            }
        }
        return new Raster(w, h, dst);
    }
}
