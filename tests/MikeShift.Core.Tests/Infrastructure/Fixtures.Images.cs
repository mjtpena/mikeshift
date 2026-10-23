using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Image fixtures (W2a), generated at test time with no imaging code from MikeShift.Core: PNG (RGB, RGBA, animated,
/// header-only huge), JPEG with hand-built Exif/GPS/XMP/Photoshop segments, GIF (static, animated), multi-page TIFF, BMP, WebP.
/// The recognisable test picture is four coloured quadrants: top-left red, top-right green, bottom-left blue, bottom-right yellow.
/// </summary>
public static partial class Fixtures
{
    static partial void WriteImagesFixtures(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "rgb.png"), PngRgb(64, 48));
        File.WriteAllBytes(Path.Combine(dir, "alpha.png"), PngAlpha(64, 48));
        File.WriteAllBytes(Path.Combine(dir, "animated.png"), AnimatedPng());
        File.WriteAllBytes(Path.Combine(dir, "photo.png"), PhotoPng(1200, 800));
        File.WriteAllBytes(Path.Combine(dir, "photo.jpg"), PhotoJpeg(1200, 800));
        File.WriteAllBytes(Path.Combine(dir, "exif-gps.jpg"), JpegWithMetadata(120, 80, orientation: 1, gps: true, xmp: true, photoshop: true));
        File.WriteAllBytes(Path.Combine(dir, "exif-rotated.jpg"), JpegWithMetadata(120, 80, orientation: 6, gps: true));
        File.WriteAllBytes(Path.Combine(dir, "static.gif"), Gif(64, 48, animated: false));
        File.WriteAllBytes(Path.Combine(dir, "animated.gif"), Gif(64, 48, animated: true));
        File.WriteAllBytes(Path.Combine(dir, "pages.tif"), TiffMultiFrame(64, 48, frames: 3));
        File.WriteAllBytes(Path.Combine(dir, "sample.bmp"), Bmp(64, 48));
        File.WriteAllBytes(Path.Combine(dir, "sample.webp"), Webp(64, 48));
        File.WriteAllBytes(Path.Combine(dir, "huge-300mp.png"), PngHeaderOnly(20_000, 15_000));
    }

    /// <summary>
    /// A small real image for the given image extension (lower case, no dot), or null for formats that cannot be fabricated here
    /// (HEIC/HEIF/AVIF/JPEG XR need codecs and encoders this repo does not ship).
    /// </summary>
    public static byte[]? ImageFor(string ext) => ext switch
    {
        "png" => PngRgb(16, 12),
        "jpg" or "jpeg" or "jpe" or "jfif" => QuadrantJpeg(16, 12),
        "gif" => Gif(16, 12),
        "tif" or "tiff" => TiffMultiFrame(16, 12, 1),
        "bmp" or "dib" => Bmp(16, 12),
        "webp" => Webp(16, 12),
        "ico" => Ico(16, 16),
        _ => null,
    };

    /// <summary>A one-image ICO whose image is an embedded PNG (the Vista+ format).</summary>
    public static byte[] Ico(int width = 16, int height = 16)
    {
        var png = PngRgb(width, height);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
        w.Write((byte)width); w.Write((byte)height); w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)png.Length); w.Write(22u);
        w.Write(png);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ pixels

    /// <summary>The four-quadrant test picture as 8-bit BGRA.</summary>
    public static byte[] QuadrantsBgra(int width, int height, byte alpha = 0xFF)
    {
        var px = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var right = x >= width / 2;
                var bottom = y >= height / 2;
                (byte r, byte g, byte b) = (bottom, right) switch
                {
                    (false, false) => ((byte)220, (byte)30, (byte)30),
                    (false, true) => ((byte)30, (byte)200, (byte)30),
                    (true, false) => ((byte)30, (byte)30, (byte)220),
                    _ => ((byte)230, (byte)230, (byte)30),
                };
                var i = (y * width + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = alpha;
            }
        }
        return px;
    }

    /// <summary>Photograph-like BGRA: smooth gradients plus film grain (compresses well as JPEG, badly as PNG).</summary>
    public static byte[] PhotoBgra(int width, int height, int seed = 3)
    {
        var rnd = new Random(seed);
        var px = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var grain = rnd.Next(-12, 13);
                px[i] = (byte)Math.Clamp(60 + 140 * y / height + grain, 0, 255);
                px[i + 1] = (byte)Math.Clamp(40 + 170 * x / width + grain, 0, 255);
                px[i + 2] = (byte)Math.Clamp(200 - 120 * (x + y) / (width + height) + grain, 0, 255);
                px[i + 3] = 0xFF;
            }
        }
        return px;
    }

    private static byte[] SkiaEncode(byte[] bgra, int width, int height, Func<SKPixmap, SKData?> encode)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var pixmap = bmp.PeekPixels();
        using var data = encode(pixmap) ?? throw new InvalidOperationException("Skia could not encode the fixture.");
        return data.ToArray();
    }

    // ------------------------------------------------------------------ PNG

    /// <summary>An opaque 8-bit RGB PNG (colour type 2, no alpha channel) of the quadrant picture.</summary>
    public static byte[] PngRgb(int width = 64, int height = 48) => BuildPng(width, height, QuadrantsBgra(width, height), alpha: false);

    /// <summary>An RGBA PNG of the quadrant picture whose top-left quadrant is fully transparent.</summary>
    public static byte[] PngAlpha(int width = 64, int height = 48)
    {
        var px = QuadrantsBgra(width, height);
        for (var y = 0; y < height / 2; y++)
            for (var x = 0; x < width / 2; x++)
                px[(y * width + x) * 4 + 3] = 0;
        return BuildPng(width, height, px, alpha: true);
    }

    /// <summary>An RGBA PNG where every pixel is opaque (alpha channel present but unused).</summary>
    public static byte[] PngOpaqueRgba(int width = 64, int height = 48) => BuildPng(width, height, QuadrantsBgra(width, height), alpha: true);

    /// <summary>A big photo-like PNG (poor compression), for Compress tests.</summary>
    public static byte[] PhotoPng(int width, int height) =>
        SkiaEncode(PhotoBgra(width, height), width, height, p => p.Encode(new SKPngEncoderOptions()));

    private static byte[] BuildPng(int width, int height, byte[] bgra, bool alpha)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                raw.WriteByte(bgra[i + 2]); raw.WriteByte(bgra[i + 1]); raw.WriteByte(bgra[i]);
                if (alpha) raw.WriteByte(bgra[i + 3]);
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteIhdr(png, width, height, alpha ? (byte)6 : (byte)2);
        WriteChunk(png, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteIhdr(Stream png, int width, int height, byte colorType)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = colorType; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(png, "IHDR", ihdr);
    }

    /// <summary>A PNG whose <c>acTL</c> chunk marks it as animated (APNG). Frame data is the plain still image.</summary>
    public static byte[] AnimatedPng(int width = 32, int height = 24)
    {
        var still = PngRgb(width, height);
        using var ms = new MemoryStream();
        ms.Write(still, 0, 8 + 25); // signature + IHDR chunk (4 + 4 + 13 + 4)
        Span<byte> actl = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, 2);      // num_frames
        BinaryPrimitives.WriteUInt32BigEndian(actl[4..], 0); // num_plays (infinite)
        WriteChunk(ms, "acTL", actl);
        ms.Write(still, 8 + 25, still.Length - 8 - 25);
        return ms.ToArray();
    }

    /// <summary>
    /// A PNG with a valid signature, IHDR (the given, possibly huge, size) and IEND but no usable pixel data. WIC reads the
    /// size from the header, so this exercises the 200-megapixel guard without allocating anything.
    /// </summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteIhdr(png, width, height, 2);
        WriteChunk(png, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    // ------------------------------------------------------------------ JPEG

    /// <summary>A plain JPEG (no Exif) of the quadrant picture.</summary>
    public static byte[] QuadrantJpeg(int width = 64, int height = 48, int quality = 95) =>
        SkiaEncode(QuadrantsBgra(width, height), width, height, p => p.Encode(new SKJpegEncoderOptions(quality)));

    /// <summary>A photo-like JPEG (compresses further when re-saved at lower quality).</summary>
    public static byte[] PhotoJpeg(int width, int height, int quality = 97) =>
        SkiaEncode(PhotoBgra(width, height), width, height, p => p.Encode(new SKJpegEncoderOptions(quality)));

    /// <summary>
    /// A JPEG of the quadrant picture (stored as <paramref name="width"/> x <paramref name="height"/>) carrying a hand-built Exif
    /// block (Make, Model, Orientation, pixel dimensions and, if <paramref name="gps"/>, a GPS IFD), optionally XMP (APP1)
    /// and a Photoshop resource block (APP13).
    /// </summary>
    public static byte[] JpegWithMetadata(int width, int height, int orientation = 1, bool gps = true, bool xmp = false, bool photoshop = false)
    {
        var jpeg = QuadrantJpeg(width, height);
        var segments = new List<byte[]> { Segment(0xE1, BuildExif(width, height, orientation, gps)) };
        if (xmp)
            segments.Add(Segment(0xE1, Concat(Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"),
                Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"))));
        if (photoshop)
            segments.Add(Segment(0xED, Concat(Encoding.ASCII.GetBytes("Photoshop 3.0\0"), "8BIM"u8.ToArray(), [0x04, 0x04, 0, 0, 0, 0, 0, 4], "test"u8.ToArray())));

        using var ms = new MemoryStream();
        ms.Write(jpeg, 0, 2); // SOI
        foreach (var s in segments) ms.Write(s);
        ms.Write(jpeg, 2, jpeg.Length - 2);
        return ms.ToArray();
    }

    /// <summary>Make string used in <see cref="JpegWithMetadata"/>.</summary>
    public const string ExifMake = "MikeShiftTestCam";

    /// <summary>DateTimeOriginal in <see cref="JpegWithMetadata"/> (Exif text format).</summary>
    public const string ExifDateTimeOriginal = "2023:07:04 12:34:56";

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var seg = new byte[payload.Length + 4];
        seg[0] = 0xFF; seg[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(seg, 4);
        return seg;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] BuildExif(int width, int height, int orientation, bool gps)
    {
        var make = Encoding.ASCII.GetBytes(ExifMake + "\0");
        var model = Encoding.ASCII.GetBytes("Model One\0");

        var ifd0Count = gps ? 5 : 4;
        var ifd0Pos = 8;
        var ifd0Size = 2 + 12 * ifd0Count + 4;
        var makePos = ifd0Pos + ifd0Size;
        var modelPos = makePos + make.Length;
        var exifPos = modelPos + model.Length;
        var exifSize = 2 + 12 * 3 + 4;
        var gpsPos = exifPos + exifSize;
        var gpsSize = 2 + 12 * 4 + 4;
        var ratPos = gpsPos + (gps ? gpsSize : 0);
        var dateTime = Encoding.ASCII.GetBytes(ExifDateTimeOriginal + "\0");
        var datePos = ratPos + (gps ? 48 : 0);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void Entry(ushort tag, ushort type, uint count, uint value) { w.Write(tag); w.Write(type); w.Write(count); w.Write(value); }

        w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write((uint)ifd0Pos);

        w.Write((ushort)ifd0Count);
        Entry(0x010F, 2, (uint)make.Length, (uint)makePos);
        Entry(0x0110, 2, (uint)model.Length, (uint)modelPos);
        Entry(0x0112, 3, 1, (uint)orientation);
        Entry(0x8769, 4, 1, (uint)exifPos);
        if (gps) Entry(0x8825, 4, 1, (uint)gpsPos);
        w.Write(0u); // no IFD1
        w.Write(make);
        w.Write(model);

        w.Write((ushort)3);
        Entry(0x9003, 2, (uint)dateTime.Length, (uint)datePos);
        Entry(0xA002, 4, 1, (uint)width);
        Entry(0xA003, 4, 1, (uint)height);
        w.Write(0u);

        if (gps)
        {
            w.Write((ushort)4);
            Entry(0x0001, 2, 2, 'N');                // GPSLatitudeRef "N\0"
            Entry(0x0002, 5, 3, (uint)ratPos);        // GPSLatitude
            Entry(0x0003, 2, 2, 'E');                // GPSLongitudeRef
            Entry(0x0004, 5, 3, (uint)(ratPos + 24)); // GPSLongitude
            w.Write(0u);
            foreach (var (n, d) in new (uint, uint)[] { (48, 1), (51, 1), (30, 1), (2, 1), (21, 1), (7, 1) })
            { w.Write(n); w.Write(d); }
        }
        w.Write(dateTime);

        var tiff = ms.ToArray();
        return Concat("Exif\0\0"u8.ToArray(), tiff);
    }

    // ------------------------------------------------------------------ GIF

    /// <summary>A GIF (4-colour palette, uncompressed-style LZW) of the quadrant picture; the animated variant has a second frame with swapped colours.</summary>
    public static byte[] Gif(int width = 64, int height = 48, bool animated = false)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("GIF89a"u8.ToArray());
        w.Write((ushort)width); w.Write((ushort)height);
        w.Write((byte)0xF1); // global colour table, 2 bits per colour -> 4 entries
        w.Write((byte)0); w.Write((byte)0);
        w.Write(new byte[] { 220, 30, 30, 30, 200, 30, 30, 30, 220, 230, 230, 30 });
        if (animated)
        {
            w.Write(new byte[] { 0x21, 0xFF, 0x0B });
            w.Write("NETSCAPE2.0"u8.ToArray());
            w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        var frames = animated ? 2 : 1;
        for (var f = 0; f < frames; f++)
        {
            if (animated)
                w.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00 }); // 100 ms delay
            w.Write((byte)0x2C);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)height);
            w.Write((byte)0);
            w.Write((byte)2); // LZW minimum code size

            var indices = new byte[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var q = (y >= height / 2 ? 2 : 0) + (x >= width / 2 ? 1 : 0);
                    indices[y * width + x] = (byte)(f == 0 ? q : 3 - q);
                }

            var lzw = LzwNoCompression(indices);
            for (var pos = 0; pos < lzw.Length; pos += 255)
            {
                var n = Math.Min(255, lzw.Length - pos);
                w.Write((byte)n);
                w.Write(lzw, pos, n);
            }
            w.Write((byte)0);
        }
        w.Write((byte)0x3B);
        return ms.ToArray();
    }

    /// <summary>GIF LZW with 3-bit codes that emits a clear code every two pixels so the table never grows.</summary>
    private static byte[] LzwNoCompression(byte[] indices)
    {
        var output = new List<byte>();
        var acc = 0;
        var bits = 0;
        void Emit(int code)
        {
            acc |= code << bits;
            bits += 3;
            while (bits >= 8) { output.Add((byte)acc); acc >>= 8; bits -= 8; }
        }

        Emit(4); // clear
        for (var i = 0; i < indices.Length; i++)
        {
            Emit(indices[i]);
            if ((i + 1) % 2 == 0 && i + 1 < indices.Length) Emit(4);
        }
        Emit(5); // end of information
        if (bits > 0) output.Add((byte)acc);
        return output.ToArray();
    }

    // ------------------------------------------------------------------ TIFF / BMP / WebP

    /// <summary>An uncompressed RGB TIFF with <paramref name="frames"/> pages; page n is the quadrant picture with the red channel shifted by 40 * n.</summary>
    public static byte[] TiffMultiFrame(int width = 64, int height = 48, int frames = 2)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write(8u);

        long prevNext = 0;
        for (var f = 0; f < frames; f++)
        {
            var px = QuadrantsBgra(width, height);
            var strip = new byte[width * height * 3];
            for (var i = 0; i < width * height; i++)
            {
                strip[i * 3] = (byte)Math.Min(255, px[i * 4 + 2] + 40 * f);
                strip[i * 3 + 1] = px[i * 4 + 1];
                strip[i * 3 + 2] = px[i * 4];
            }

            var pixelPos = (uint)ms.Position;
            // The IFD must start on a word boundary; pixel data length is a multiple of 3 so pad when needed.
            w.Write(strip);
            var bpsPos = (uint)ms.Position;
            w.Write((ushort)8); w.Write((ushort)8); w.Write((ushort)8);
            if (ms.Position % 2 != 0) w.Write((byte)0);
            var ifdPos = (uint)ms.Position;

            void Entry(ushort tag, ushort type, uint count, uint value) { w.Write(tag); w.Write(type); w.Write(count); w.Write(value); }
            w.Write((ushort)10);
            Entry(256, 4, 1, (uint)width);
            Entry(257, 4, 1, (uint)height);
            Entry(258, 3, 3, bpsPos);
            Entry(259, 3, 1, 1);
            Entry(262, 3, 1, 2);
            Entry(273, 4, 1, pixelPos);
            Entry(277, 3, 1, 3);
            Entry(278, 4, 1, (uint)height);
            Entry(279, 4, 1, (uint)strip.Length);
            Entry(284, 3, 1, 1);
            var nextPos = ms.Position;
            w.Write(0u);

            if (f == 0)
            {
                // Header's first-IFD pointer.
                var end = ms.Position;
                ms.Position = 4;
                w.Write(ifdPos);
                ms.Position = end;
            }
            else
            {
                var end = ms.Position;
                ms.Position = prevNext;
                w.Write(ifdPos);
                ms.Position = end;
            }
            prevNext = nextPos;
        }
        return ms.ToArray();
    }

    /// <summary>A 24-bit BMP of the quadrant picture.</summary>
    public static byte[] Bmp(int width = 64, int height = 48)
    {
        var px = QuadrantsBgra(width, height);
        var stride = (width * 3 + 3) & ~3;
        var dataSize = stride * height;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write((uint)(54 + dataSize)); w.Write(0u); w.Write(54u);
        w.Write(40u); w.Write(width); w.Write(height); w.Write((ushort)1); w.Write((ushort)24);
        w.Write(0u); w.Write((uint)dataSize); w.Write(2835); w.Write(2835); w.Write(0u); w.Write(0u);
        var row = new byte[stride];
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                row[x * 3] = px[i]; row[x * 3 + 1] = px[i + 1]; row[x * 3 + 2] = px[i + 2];
            }
            w.Write(row);
        }
        return ms.ToArray();
    }

    /// <summary>A lossy WebP of the quadrant picture (encoded with SkiaSharp).</summary>
    public static byte[] Webp(int width = 64, int height = 48) =>
        SkiaEncode(QuadrantsBgra(width, height), width, height, p => p.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100)));
}
