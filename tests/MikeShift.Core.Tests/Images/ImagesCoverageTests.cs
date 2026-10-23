using System.Buffers.Binary;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Images;
using SkiaSharp;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>Container sniffing, raster maths, Exif byte surgery and the less-travelled branches of the image converters.</summary>
public class ImagesCoverageTests
{
    // ------------------------------------------------------------------ container sniffing

    private static byte[] Header(params byte[] bytes) => bytes;

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var list = new List<byte>();
        list.AddRange(new byte[4]);
        list.AddRange("ftyp"u8.ToArray());
        list.AddRange(System.Text.Encoding.ASCII.GetBytes(major));
        list.AddRange(new byte[4]); // minor version
        foreach (var c in compatible) list.AddRange(System.Text.Encoding.ASCII.GetBytes(c));
        var bytes = list.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        return bytes;
    }

    public static TheoryData<string, byte[], string> SniffCases() => new()
    {
        { "png", Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), "Png" },
        { "png too short", Header(0x89, 0x50, 0x4E, 0x47), "Unknown" },
        { "jpeg", Header(0xFF, 0xD8, 0xFF, 0xE0), "Jpeg" },
        { "gif89", "GIF89a"u8.ToArray(), "Gif" },
        { "gif87", "GIF87a"u8.ToArray(), "Gif" },
        { "gif too short", "GIF8"u8.ToArray(), "Unknown" },
        { "bmp", "BM"u8.ToArray(), "Bmp" },
        { "tiff little endian", Header((byte)'I', (byte)'I', 42, 0), "Tiff" },
        { "tiff big endian", Header((byte)'M', (byte)'M', 0, 42), "Tiff" },
        { "webp", "RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), "Webp" },
        { "riff but wave", "RIFF\0\0\0\0WAVEfmt "u8.ToArray(), "Unknown" },
        { "ico", Header(0, 0, 1, 0, 1, 0), "Ico" },
        { "jpeg xr", Header(0x49, 0x49, 0xBC, 0x01), "JpegXr" },
        { "avif major", Ftyp("avif"), "Avif" },
        { "avif sequence", Ftyp("avis"), "Avif" },
        { "avif compatible", Ftyp("mif1", "miaf", "avif"), "Avif" },
        { "heic major", Ftyp("heic"), "Heic" },
        { "heic compatible", Ftyp("isom", "mif1"), "Heic" },
        { "heix", Ftyp("heix"), "Heic" },
        { "avif wins over heic", Ftyp("mif1", "heic", "avif"), "Avif" },
        { "mp4 is not an image", Ftyp("mp42", "isom"), "Unknown" },
        { "ftyp too short", "\0\0\0\u0010ftyp"u8.ToArray(), "Unknown" },
        { "empty", [], "Unknown" },
        { "text", "hello world, definitely not an image"u8.ToArray(), "Unknown" },
    };

    [Theory, MemberData(nameof(SniffCases))]
    public void Container_is_recognised_from_the_first_bytes(string _, byte[] head, string expected) =>
        Assert.Equal(Enum.Parse<ImageContainer>(expected), ImageCodec.Sniff(head));

    [Theory]
    [InlineData("a.png", "Png")]
    [InlineData("a.JPG", "Jpeg")]
    [InlineData("a.jpeg", "Jpeg")]
    [InlineData("a.jpe", "Jpeg")]
    [InlineData("a.jfif", "Jpeg")]
    [InlineData("a.gif", "Gif")]
    [InlineData("a.bmp", "Bmp")]
    [InlineData("a.dib", "Bmp")]
    [InlineData("a.tif", "Tiff")]
    [InlineData("a.tiff", "Tiff")]
    [InlineData("a.webp", "Webp")]
    [InlineData("a.heic", "Heic")]
    [InlineData("a.heif", "Heic")]
    [InlineData("a.avif", "Avif")]
    [InlineData("a.ico", "Ico")]
    [InlineData("a.jxr", "JpegXr")]
    [InlineData("a.wdp", "JpegXr")]
    [InlineData("a.xyz", "Unknown")]
    [InlineData("noextension", "Unknown")]
    public void When_the_bytes_say_nothing_the_extension_decides(string name, string expected)
    {
        using var tmp = new TempDir();
        Assert.Equal(Enum.Parse<ImageContainer>(expected), ImageCodec.SniffFile(tmp.WriteFile(name, "no magic number in here")));
    }

    [Fact]
    public void The_bytes_win_over_a_lying_extension()
    {
        using var tmp = new TempDir();
        Assert.Equal(ImageContainer.Png, ImageCodec.SniffFile(tmp.WriteFile("really-png.jpg", Fixtures.Png())));
        Assert.Equal(ImageContainer.Jpeg, ImageCodec.SniffFile(tmp.WriteFile("really-jpeg.png", Fixtures.QuadrantJpeg(16, 12))));
    }

    [Fact]
    public void ReadHead_returns_at_most_the_requested_bytes_and_copes_with_short_and_empty_files()
    {
        using var tmp = new TempDir();
        var data = Fixtures.Noise(100);
        Assert.Equal(data[..10], ImageCodec.ReadHead(tmp.WriteFile("a.bin", data), 10));
        Assert.Equal(data, ImageCodec.ReadHead(tmp.WriteFile("b.bin", data), 1000));
        Assert.Empty(ImageCodec.ReadHead(tmp.WriteFile("c.bin", []), 64));
    }

    [Fact]
    public void Animated_png_is_told_apart_from_a_plain_one_and_from_junk()
    {
        using var tmp = new TempDir();
        Assert.True(ImageCodec.IsApng(tmp.WriteFile("anim.png", Fixtures.AnimatedPng())));
        Assert.False(ImageCodec.IsApng(tmp.WriteFile("plain.png", Fixtures.Png())));
        Assert.False(ImageCodec.IsApng(tmp.WriteFile("junk.png", Fixtures.Noise(200))));
        Assert.False(ImageCodec.IsApng(tmp.WriteFile("empty.png", [])));
        Assert.False(ImageCodec.IsApng(tmp.WriteFile("sig-only.png", Fixtures.Png()[..8])));
        Assert.False(ImageCodec.IsApng(tmp.Combine("missing.png")));
    }

    [Fact]
    public void Animated_webp_is_the_vp8x_animation_flag()
    {
        byte[] Webp(byte flags)
        {
            var b = new byte[32];
            "RIFF"u8.CopyTo(b);
            "WEBP"u8.CopyTo(b.AsSpan(8));
            "VP8X"u8.CopyTo(b.AsSpan(12));
            b[20] = flags;
            return b;
        }
        Assert.True(ImageCodec.IsAnimatedWebp(Webp(0x02)));
        Assert.True(ImageCodec.IsAnimatedWebp(Webp(0xFF)));
        Assert.False(ImageCodec.IsAnimatedWebp(Webp(0x10)));
        Assert.False(ImageCodec.IsAnimatedWebp(Webp(0x00)));
        Assert.False(ImageCodec.IsAnimatedWebp(Webp(0x02)[..20]));
        var lossy = Webp(0x02);
        "VP8 "u8.CopyTo(lossy.AsSpan(12));
        Assert.False(ImageCodec.IsAnimatedWebp(lossy));
    }

    // ------------------------------------------------------------------ frame info and header probes

    [Fact]
    public async Task Frame_info_reports_pages_and_animation_from_the_header_alone()
    {
        using var tmp = new TempDir();
        Assert.Equal((1, true), await ImageCodec.FrameInfoAsync(tmp.WriteFile("a.png", Fixtures.AnimatedPng()), default));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("p.png", Fixtures.Png()), default));
        Assert.Equal((2, true), await ImageCodec.FrameInfoAsync(tmp.WriteFile("a.gif", Fixtures.Gif(16, 12, animated: true)), default));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("s.gif", Fixtures.Gif(16, 12)), default));
        var (pages, animated) = await ImageCodec.FrameInfoAsync(tmp.WriteFile("m.tif", Fixtures.TiffMultiFrame(16, 12, 3)), default);
        Assert.Equal((3, false), (pages, animated));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("i.ico", Fixtures.Ico()), default));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("j.jpg", Fixtures.QuadrantJpeg(16, 12)), default));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("w.webp", Fixtures.Webp(16, 12)), default));
    }

    [Fact]
    public async Task Frame_info_of_unreadable_files_is_a_single_still_frame_but_cancellation_is_honoured()
    {
        using var tmp = new TempDir();
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.WriteFile("broken.gif", "GIF89a-broken"), default));
        Assert.Equal((1, false), await ImageCodec.FrameInfoAsync(tmp.Combine("missing.tif"), default));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageCodec.FrameInfoAsync(tmp.WriteFile("c.gif", Fixtures.Gif(16, 12, animated: true)), cts.Token));
    }

    [Fact]
    public void Header_probe_accepts_real_images_and_rejects_junk_without_throwing()
    {
        using var tmp = new TempDir();
        Assert.True(ImageCodec.CanDecodeHeader(tmp.WriteFile("ok.png", Fixtures.Png())));
        Assert.False(ImageCodec.CanDecodeHeader(tmp.WriteFile("junk.png", Fixtures.Noise(300))));
        Assert.False(ImageCodec.CanDecodeHeader(tmp.WriteFile("empty.png", [])));
        Assert.False(ImageCodec.CanDecodeHeader(tmp.Combine("missing.png")));
        Assert.False(ImageProbe.CanDecode(tmp.Combine("missing.png")));
        Assert.True(ImageProbe.CanDecode(tmp.WriteFile("ok.bmp", Fixtures.Bmp(8, 8))));
    }

    [Fact]
    public void Webp_encoding_is_available_and_the_capability_probe_is_consistent_with_it()
    {
        Assert.True(ImageCodec.CanEncodeWebp());
        var caps = ImageProbe.ProbeCapabilities(Capabilities.None);
        Assert.True(caps.WebpEncode);
        Assert.Equal(ImageCodec.CanEncodeHeif(), caps.HeifEncode);
        Assert.Equal(ImageCodec.CanDecodeAvif(), caps.AvifDecode);
    }

    [Fact]
    public async Task Decoding_junk_and_cancelled_requests_fail_cleanly()
    {
        using var tmp = new TempDir();
        var junk = tmp.WriteFile("junk.png", Fixtures.Noise(500));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageCodec.DecodeAsync(junk, ImageCodec.MaxPixels, default));
        Assert.Contains("Couldn't read this image", ex.Message);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageCodec.DecodeAsync(tmp.WriteFile("ok.png", Fixtures.Png()), ImageCodec.MaxPixels, cts.Token));

        var tooBig = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageCodec.DecodeAsync(tmp.WriteFile("ok2.png", Fixtures.Png(32, 32)), maxPixels: 100, default));
        Assert.Contains("too large", tooBig.Message);
    }

    [Fact]
    public void Blank_heic_and_avif_decodes_are_rejected_but_black_pictures_in_other_formats_are_fine()
    {
        var black = new Raster(4, 4, new byte[64]);
        var transparent = new Raster(2, 2, new byte[16]);
        Assert.Throws<ConversionException>(() => ImageCodec.RejectBlankDecode(ImageContainer.Heic, black));
        Assert.Throws<ConversionException>(() => ImageCodec.RejectBlankDecode(ImageContainer.Avif, transparent));
        ImageCodec.RejectBlankDecode(ImageContainer.Png, black);
        ImageCodec.RejectBlankDecode(ImageContainer.Jpeg, black);
        var lit = new byte[64];
        lit[5] = 1;
        ImageCodec.RejectBlankDecode(ImageContainer.Heic, new Raster(4, 4, lit));
    }

    [Fact]
    public async Task Encoding_an_undefined_format_is_rejected_and_every_defined_one_but_heic_works()
    {
        var raster = new Raster(8, 8, Fixtures.QuadrantsBgra(8, 8));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ImageCodec.EncodeAsync(raster, (ImageFormat)99, 0.9, null, null, default));

        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpg, ImageFormat.Tiff, ImageFormat.Gif, ImageFormat.Bmp, ImageFormat.Webp })
        {
            var bytes = await ImageCodec.EncodeAsync(raster, format, 2.0, null, null, default); // quality is clamped, not rejected
            Assert.NotEmpty(bytes);
        }
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageCodec.EncodeAsync(raster, ImageFormat.Png, 0.5, null, null, cts.Token));
    }

    [Fact]
    public async Task Jpeg_and_bmp_output_is_flattened_on_white_while_png_keeps_the_transparency()
    {
        var px = new byte[4 * 4 * 4]; // fully transparent black
        var raster = new Raster(4, 4, px);
        foreach (var format in new[] { ImageFormat.Jpg, ImageFormat.Bmp })
        {
            using var tmp = new TempDir();
            var path = tmp.WriteFile("out." + (format == ImageFormat.Jpg ? "jpg" : "bmp"), await ImageCodec.EncodeAsync(raster, format, 0.95, null, null, default));
            Assert.True(Near(Decode(path), 1, 1, White, tol: 8), format.ToString());
        }
        using var t2 = new TempDir();
        var png = t2.WriteFile("out.png", await ImageCodec.EncodeAsync(raster, ImageFormat.Png, 0.9, null, null, default));
        Assert.Equal(0, At(Decode(png), 1, 1).A);
    }

    // ------------------------------------------------------------------ Raster

    private static Raster Numbered(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)i;       // blue carries the pixel's index
            px[i * 4 + 3] = 0xFF;
        }
        return new Raster(w, h, px);
    }

    private static int[][] Ids(Raster r) =>
        [.. Enumerable.Range(0, r.Height).Select(y => Enumerable.Range(0, r.Width).Select(x => (int)r.Bgra[(y * r.Width + x) * 4]).ToArray())];

    [Fact]
    public void Raster_rejects_impossible_dimensions_and_buffers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Raster(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Raster(1, -1, new byte[4]));
        Assert.Throws<ArgumentException>(() => new Raster(2, 2, new byte[15]));
        Assert.Throws<ArgumentException>(() => new Raster(2, 2, new byte[17]));
    }

    [Fact]
    public void Alpha_and_blackness_queries()
    {
        var opaque = new Raster(2, 1, [1, 2, 3, 255, 4, 5, 6, 255]);
        var seeThrough = new Raster(2, 1, [1, 2, 3, 255, 4, 5, 6, 254]);
        Assert.False(opaque.HasRealAlpha);
        Assert.True(seeThrough.HasRealAlpha);
        Assert.False(opaque.IsAllBlack());
        Assert.True(new Raster(2, 1, [0, 0, 0, 255, 0, 0, 0, 7]).IsAllBlack());
        Assert.False(new Raster(1, 1, [0, 0, 1, 0]).IsAllBlack());
    }

    [Fact]
    public void Flattening_blends_straight_alpha_over_white()
    {
        var src = new Raster(3, 1, [0, 0, 0, 255, 0, 0, 0, 0, 100, 100, 100, 128]);
        var flat = src.FlattenOnWhite();
        Assert.Equal([0, 0, 0, 255], flat.Bgra[..4]);                   // opaque pixel untouched
        Assert.Equal([255, 255, 255, 255], flat.Bgra[4..8]);            // transparent becomes white
        var mixed = flat.Bgra[8..12];
        Assert.Equal(0xFF, mixed[3]);
        Assert.InRange(mixed[0], 176, 179);                             // 100*128/255 + 255*127/255 = 177.3
        Assert.NotSame(src.Bgra, flat.Bgra);
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, new[] { 0, 1, 2 }, new[] { 3, 4, 5 })]
    [InlineData(SKEncodedOrigin.TopRight, new[] { 2, 1, 0 }, new[] { 5, 4, 3 })]
    [InlineData(SKEncodedOrigin.BottomRight, new[] { 5, 4, 3 }, new[] { 2, 1, 0 })]
    [InlineData(SKEncodedOrigin.BottomLeft, new[] { 3, 4, 5 }, new[] { 0, 1, 2 })]
    public void Origins_that_keep_the_dimensions(SKEncodedOrigin origin, int[] row0, int[] row1)
    {
        var r = Numbered(3, 2).ApplyOrigin(origin);
        Assert.Equal((3, 2), (r.Width, r.Height));
        Assert.Equal([row0, row1], Ids(r));
    }

    [Theory]
    [InlineData(SKEncodedOrigin.LeftTop, new[] { 0, 3 }, new[] { 1, 4 }, new[] { 2, 5 })]       // transpose
    [InlineData(SKEncodedOrigin.RightTop, new[] { 3, 0 }, new[] { 4, 1 }, new[] { 5, 2 })]       // rotate 90 clockwise
    [InlineData(SKEncodedOrigin.RightBottom, new[] { 5, 2 }, new[] { 4, 1 }, new[] { 3, 0 })]    // transverse
    [InlineData(SKEncodedOrigin.LeftBottom, new[] { 2, 5 }, new[] { 1, 4 }, new[] { 0, 3 })]     // rotate 90 counter-clockwise
    public void Origins_that_swap_width_and_height(SKEncodedOrigin origin, int[] row0, int[] row1, int[] row2)
    {
        var r = Numbered(3, 2).ApplyOrigin(origin);
        Assert.Equal((2, 3), (r.Width, r.Height));
        Assert.Equal([row0, row1, row2], Ids(r));
    }

    [Fact]
    public void Crop_scale_returns_the_same_instance_for_the_whole_image_and_exact_pixels_for_a_one_to_one_crop()
    {
        var r = Numbered(4, 4);
        Assert.Same(r, r.CropScale(new RectPx(0, 0, 4, 4), 4, 4));
        var crop = r.CropScale(new RectPx(1, 1, 2, 2), 2, 2);
        Assert.Equal([[5, 6], [9, 10]], Ids(crop));
    }

    [Fact]
    public void Crop_scale_shrinks_and_enlarges_keeping_colours()
    {
        var r = new Raster(8, 8, Fixtures.QuadrantsBgra(8, 8));
        var small = r.CropScale(new RectPx(0, 0, 8, 8), 4, 4);
        var big = r.CropScale(new RectPx(0, 0, 8, 8), 32, 24);
        AssertQuadrants(small, tol: 40);
        Assert.Equal((32, 24), (big.Width, big.Height));
        AssertQuadrants(big, tol: 40);
    }

    [Fact]
    public void Webp_output_starts_with_a_riff_webp_header_and_skia_bitmaps_of_any_colour_type_convert()
    {
        var bytes = new Raster(8, 8, Fixtures.QuadrantsBgra(8, 8)).EncodeWebp(80);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        using var gray = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Gray8));
        gray.Erase(new SKColor(128, 128, 128));
        var r = Raster.FromSkia(gray);
        Assert.Equal((2, 2), (r.Width, r.Height));
        Assert.InRange(r.Bgra[0], 120, 136);
        Assert.Equal(255, r.Bgra[3]);
    }

    // ------------------------------------------------------------------ JpegMeta (hand-built Exif)

    private static byte[] BuildExif(bool little, (ushort Tag, ushort Type, uint Value)[] ifd0, (ushort Tag, ushort Type, uint Value)[]? exifIfd = null, bool thumbnailLink = true)
    {
        var entries = ifd0.ToList();
        var hasExifIfd = exifIfd is not null;
        if (hasExifIfd) entries.Add((0x8769, 4, 0));
        var ifd0Size = 2 + entries.Count * 12 + 4;
        var exifIfdOffset = 8 + ifd0Size;

        using var ms = new MemoryStream();
        ms.Write("Exif\0\0"u8);
        void W16(ushort v) { Span<byte> b = stackalloc byte[2]; if (little) BinaryPrimitives.WriteUInt16LittleEndian(b, v); else BinaryPrimitives.WriteUInt16BigEndian(b, v); ms.Write(b); }
        void W32(uint v) { Span<byte> b = stackalloc byte[4]; if (little) BinaryPrimitives.WriteUInt32LittleEndian(b, v); else BinaryPrimitives.WriteUInt32BigEndian(b, v); ms.Write(b); }
        void Entry(ushort tag, ushort type, uint value)
        {
            W16(tag); W16(type); W32(1);
            if (type == 3) { W16((ushort)value); W16(0); } else W32(value);
        }
        ms.Write(little ? "II"u8 : "MM"u8);
        W16(42);
        W32(8);
        W16((ushort)entries.Count);
        foreach (var (tag, type, value) in entries)
            Entry(tag, type, tag == 0x8769 ? (uint)exifIfdOffset : value);
        W32(thumbnailLink ? 0x1234u : 0u);
        if (exifIfd is not null)
        {
            W16((ushort)exifIfd.Length);
            foreach (var (tag, type, value) in exifIfd) Entry(tag, type, value);
            W32(0);
        }
        return ms.ToArray();
    }

    private static ushort Read16(byte[] exif, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(exif.AsSpan(offset)) : BinaryPrimitives.ReadUInt16BigEndian(exif.AsSpan(offset));

    private static uint Read32(byte[] exif, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(exif.AsSpan(offset)) : BinaryPrimitives.ReadUInt32BigEndian(exif.AsSpan(offset));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Making_exif_upright_resets_orientation_unlinks_the_thumbnail_and_updates_the_dimension_tags(bool little)
    {
        var exif = BuildExif(little,
            [(0x0112, 3, 6), (0x0100, 3, 4000), (0x0101, 4, 3000), (0x010F, 4, 77)],
            [(0xA002, 3, 4000), (0xA003, 4, 3000), (0xA005, 4, 5)]);

        var upright = JpegMeta.MakeUpright(exif, 640, 480)!;

        Assert.NotSame(exif, upright);
        Assert.Equal(exif.Length, upright.Length);
        const int tiff = 6;
        // IFD0 entries start at tiff + 8 + 2; each is 12 bytes, value field at +8.
        int Entry(int i) => tiff + 8 + 2 + i * 12;
        Assert.Equal(1, Read16(upright, Entry(0) + 8, little));       // Orientation
        Assert.Equal(640, Read16(upright, Entry(1) + 8, little));     // ImageWidth (SHORT)
        Assert.Equal(480u, Read32(upright, Entry(2) + 8, little));    // ImageLength (LONG)
        Assert.Equal(77u, Read32(upright, Entry(3) + 8, little));     // unrelated tag untouched
        Assert.Equal(0u, Read32(upright, tiff + 8 + 2 + 5 * 12, little)); // next-IFD (thumbnail) link removed
        var exifIfd = tiff + 8 + 2 + 5 * 12 + 4;
        Assert.Equal(640, Read16(upright, exifIfd + 2 + 8, little));  // PixelXDimension
        Assert.Equal(480u, Read32(upright, exifIfd + 2 + 12 + 8, little)); // PixelYDimension
        Assert.Equal(5u, Read32(upright, exifIfd + 2 + 24 + 8, little));   // other Exif tags untouched
        Assert.Equal(6, Read16(exif, Entry(0) + 8, little));           // the input is never modified
    }

    [Fact]
    public void Exif_without_an_exif_sub_ifd_or_dimension_tags_is_still_made_upright()
    {
        var exif = BuildExif(true, [(0x0112, 3, 8)]);
        var upright = JpegMeta.MakeUpright(exif, 10, 10)!;
        Assert.Equal(1, Read16(upright, 6 + 8 + 2 + 8, true));
    }

    [Theory]
    [InlineData("too short")]
    [InlineData("bad byte order")]
    [InlineData("bad magic")]
    [InlineData("ifd offset past the end")]
    [InlineData("entry count past the end")]
    [InlineData("overflowing offset")]
    public void Malformed_exif_is_dropped_rather_than_kept_with_a_wrong_orientation(string kind)
    {
        var good = BuildExif(true, [(0x0112, 3, 6)]);
        var bad = (byte[])good.Clone();
        switch (kind)
        {
            case "too short": bad = bad[..10]; break;
            case "bad byte order": bad[6] = (byte)'X'; bad[7] = (byte)'X'; break;
            case "bad magic": bad[8] = 43; break;
            case "ifd offset past the end": BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(10), 5000); break;
            case "entry count past the end": BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(14), 5000); break;
            case "overflowing offset": BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(10), uint.MaxValue); break;
        }
        Assert.Null(JpegMeta.MakeUpright(bad, 10, 10));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gps_and_orientation_are_read_from_either_byte_order(bool little)
    {
        var withGps = BuildExif(little, [(0x0112, 3, 6), (0x8825, 4, 100)]);
        var without = BuildExif(little, [(0x010F, 4, 1)]);
        Assert.True(JpegMeta.HasGps(withGps));
        Assert.False(JpegMeta.HasGps(without));

        var jpeg = JpegMeta.InsertExif(Fixtures.QuadrantJpeg(16, 12), withGps);
        Assert.Equal(6, JpegMeta.ReadOrientation(jpeg));
        Assert.Equal(1, JpegMeta.ReadOrientation(JpegMeta.InsertExif(Fixtures.QuadrantJpeg(16, 12), without)));
    }

    [Fact]
    public void Gps_and_orientation_queries_never_throw_on_garbage()
    {
        Assert.False(JpegMeta.HasGps([]));
        Assert.False(JpegMeta.HasGps("Exif\0\0II"u8.ToArray()));
        Assert.False(JpegMeta.HasGps(Fixtures.Noise(64)));

        Assert.Equal(1, JpegMeta.ReadOrientation([]));
        Assert.Equal(1, JpegMeta.ReadOrientation(Fixtures.Noise(64)));
        Assert.Equal(1, JpegMeta.ReadOrientation(Fixtures.QuadrantJpeg(16, 12)));  // no Exif at all
        var truncated = BuildExif(true, [(0x0112, 3, 6)])[..12];
        var jpeg = JpegMeta.InsertExif(Fixtures.QuadrantJpeg(16, 12), truncated);
        Assert.Equal(1, JpegMeta.ReadOrientation(jpeg));
    }

    [Fact]
    public void Segment_scanning_skips_fill_bytes_and_markers_without_a_length_and_stops_at_bad_data()
    {
        // SOI, fill bytes, RSTn, TEM, APP1 (len 6), garbage length, SOS
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xFF, 0xFF, 0xD3, 0xFF, 0x01, 0xFF, 0xE1, 0x00, 0x06, 1, 2, 3, 4, 0xFF, 0xE2, 0x00, 0x01, 0, 0, 0xFF, 0xDA, 0, 2];
        var segments = JpegMeta.Segments(jpeg);
        var only = Assert.Single(segments);
        Assert.Equal((0xE1, 4), ((int)only.Marker, only.PayloadLength));
        Assert.Equal("FFE1(6)", JpegMeta.Describe(jpeg));

        Assert.Empty(JpegMeta.Segments([]));
        Assert.Empty(JpegMeta.Segments([0xFF, 0xD8]));
        Assert.Empty(JpegMeta.Segments([0x00, 0x01, 0x02, 0x03, 0x04]));
        Assert.Empty(JpegMeta.Segments([0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00]));
        Assert.Empty(JpegMeta.Segments([0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x50, 1, 2])); // length runs past the data
        Assert.Empty(JpegMeta.Segments([0xFF, 0xD8, 0xFF, 0xD9, 0x00, 0x00]));       // EOI ends the scan
        Assert.Equal("", JpegMeta.Describe([]));
    }

    [Fact]
    public void Exif_insertion_goes_after_a_leading_jfif_segment_and_refuses_unfit_inputs()
    {
        var payload = BuildExif(true, [(0x0112, 3, 1)]);
        byte[] jfif = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, 0xFF, 0xDB, 0x00, 0x02, 0xFF, 0xD9];
        var withJfif = JpegMeta.InsertExif(jfif, payload);
        Assert.Equal("FFE0(4) FFE1(" + (payload.Length + 2) + ") FFDB(2)", JpegMeta.Describe(withJfif));
        Assert.Equal(payload, JpegMeta.ExtractExif(withJfif));

        byte[] bare = [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x02, 0xFF, 0xD9];
        Assert.Equal("FFE1(" + (payload.Length + 2) + ") FFDB(2)", JpegMeta.Describe(JpegMeta.InsertExif(bare, payload)));

        Assert.Same(bare, JpegMeta.InsertExif(bare, new byte[70_000]));
        var notJpeg = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Same(notJpeg, JpegMeta.InsertExif(notJpeg, payload));
        var tiny = new byte[] { 0xFF, 0xD8 };
        Assert.Same(tiny, JpegMeta.InsertExif(tiny, payload));
    }

    [Fact]
    public void Exif_extraction_ignores_xmp_and_short_app1_segments()
    {
        byte[] xmp = [.. "http://ns.adobe.com/xap/1.0/\0"u8.ToArray()];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, (byte)(xmp.Length + 2), .. xmp, 0xFF, 0xE1, 0x00, 0x04, (byte)'E', (byte)'x', 0xFF, 0xD9];
        Assert.Null(JpegMeta.ExtractExif(jpeg));
        Assert.Null(JpegMeta.ExtractExif([]));
    }

    [Fact]
    public void Metadata_segments_are_detected_and_removed_without_touching_the_rest()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 1, 2, 0xFF, 0xE1, 0x00, 0x04, 9, 9, 0xFF, 0xED, 0x00, 0x03, 7, 0xFF, 0xDB, 0x00, 0x02, 0xFF, 0xD9];
        Assert.True(JpegMeta.HasMetadataSegments(jpeg));
        var clean = JpegMeta.RemoveMetadataSegments(jpeg);
        Assert.Equal("FFE0(4) FFDB(2)", JpegMeta.Describe(clean));
        Assert.False(JpegMeta.HasMetadataSegments(clean));
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 1, 2, 0xFF, 0xDB, 0x00, 0x02, 0xFF, 0xD9], clean);
        Assert.Same(clean, JpegMeta.RemoveMetadataSegments(clean));
        Assert.False(JpegMeta.HasMetadataSegments([]));
    }

    // ------------------------------------------------------------------ converter branches

    [Fact]
    public async Task The_overloads_without_explicit_capabilities_use_this_machines_real_ones()
    {
        using var tmp = new TempDir();
        var big = tmp.WriteFile("big.bmp", Fixtures.Bmp(300, 200));
        var compressed = await ImageConverter.CompressAsync(big, default);
        Assert.Equal(tmp.Combine("big compressed.jpg"), compressed);

        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var half = await ImageConverter.ResizeHalfAsync(png, default);
        Assert.Equal((32, 24), (Decode(half).Width, Decode(half).Height));

        var cropped = await ImageConverter.CropAsync(png, new CropChoice(new RectPx(0, 0, 20, 10), 20, 10), default);
        Assert.Equal((20, 10), (Decode(cropped).Width, Decode(cropped).Height));

        var jpg = tmp.WriteFile("p.jpg", Fixtures.JpegWithMetadata(64, 48, orientation: 1, gps: true, xmp: true));
        var clean = await ImageConverter.StripExifAsync(jpg, default);
        Assert.False(JpegMeta.HasMetadataSegments(File.ReadAllBytes(clean)));
    }

    [Fact]
    public async Task Crop_refuses_outputs_that_would_be_absurdly_large_and_areas_outside_the_picture()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        using var guard = OriginalsGuard.Watch(png);

        var huge = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.CropAsync(png, new CropChoice(new RectPx(0, 0, 10, 10), 40_000, 10), Capabilities.All, default));
        Assert.Contains("too large", huge.Message);
        var manyPixels = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.CropAsync(png, new CropChoice(new RectPx(0, 0, 10, 10), 20_000, 20_000), Capabilities.All, default));
        Assert.Contains("too large", manyPixels.Message);
        var outside = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.CropAsync(png, new CropChoice(new RectPx(500, 500, 10, 10), 10, 10), Capabilities.All, default));
        Assert.Contains("outside the image", outside.Message);
        await Assert.ThrowsAsync<ArgumentNullException>(() => ImageConverter.CropAsync(png, null!, Capabilities.All, default));
        Assert.Equal(["a.png"], FileNames(tmp.Path));
    }

    [Fact]
    public async Task Crop_clamps_a_rectangle_that_hangs_over_the_edge_and_zero_output_sizes_mean_unscaled()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var output = await ImageConverter.CropAsync(png, new CropChoice(new RectPx(-10, -10, 50, 40), 0, 0), Capabilities.All, default);
        var back = Decode(output);
        Assert.Equal((40, 30), (back.Width, back.Height));
    }

    [Fact]
    public async Task Webp_keeps_its_own_extension_when_this_pc_can_write_it_and_becomes_png_when_it_cannot()
    {
        using var tmp = new TempDir();
        var webp = tmp.WriteFile("pic.webp", Fixtures.Webp(32, 24));
        var same = await ImageConverter.ResizeHalfAsync(webp, Capabilities.All with { WebpEncode = true }, default);
        Assert.Equal(tmp.Combine("pic 50%.webp"), same);
        var fallback = await ImageConverter.ResizeHalfAsync(webp, Capabilities.All with { WebpEncode = false }, default);
        Assert.Equal(tmp.Combine("pic 50%.png"), fallback);
    }

    [Fact]
    public async Task Output_extensions_keep_the_inputs_own_spelling_for_every_format()
    {
        using var tmp = new TempDir();
        Assert.Equal(tmp.Combine("a 50%.jpeg"), await ImageConverter.ResizeHalfAsync(tmp.WriteFile("a.jpeg", Fixtures.QuadrantJpeg(32, 24)), Capabilities.All, default));
        Assert.Equal(tmp.Combine("b 50%.tif"), await ImageConverter.ResizeHalfAsync(tmp.WriteFile("b.tif", Fixtures.TiffMultiFrame(32, 24, 1)), Capabilities.All, default));
        Assert.Equal(tmp.Combine("c 50%.dib"), await ImageConverter.ResizeHalfAsync(tmp.WriteFile("c.dib", Fixtures.Bmp(32, 24)), Capabilities.All, default));
        Assert.Equal(tmp.Combine("d 50%.gif"), await ImageConverter.ResizeHalfAsync(tmp.WriteFile("d.gif", Fixtures.Gif(32, 24)), Capabilities.All, default));
        // An ICO has no writer of its own: it becomes a PNG.
        Assert.Equal(tmp.Combine("e 50%.png"), await ImageConverter.ResizeHalfAsync(tmp.WriteFile("e.ico", Fixtures.Ico(32, 32)), Capabilities.All, default));
    }

    [Fact]
    public async Task Tools_refuse_animations_and_multi_page_files_with_the_right_verb()
    {
        using var tmp = new TempDir();
        var gif = tmp.WriteFile("a.gif", Fixtures.Gif(16, 12, animated: true));
        var tif = tmp.WriteFile("p.tif", Fixtures.TiffMultiFrame(16, 12, 3));
        var anim = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.StripExifAsync(gif, Capabilities.All, default));
        Assert.Equal("Animated images can't be cleaned without losing the animation.", anim.Message);
        var pages = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.ResizeHalfAsync(tif, Capabilities.All, default));
        Assert.Equal("This file has 3 pages, and only the first would be kept, so it can't be resized.", pages.Message);
        var early = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.RequireSingleFrameAsync(tif, "cropped", default));
        Assert.Contains("cropped", early.Message);
        await ImageConverter.RequireSingleFrameAsync(tmp.WriteFile("ok.png", Fixtures.Png()), "cropped", default);
    }
}
