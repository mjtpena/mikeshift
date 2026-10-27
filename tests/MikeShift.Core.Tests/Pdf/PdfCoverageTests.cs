using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using MikeShift.Core.Pdf;

namespace MikeShift.Core.Tests.Pdf;

/// <summary>PDF guards, text extraction, pixel helpers, image sources and the image recompressor's decisions.</summary>
public class PdfCoverageTests
{
    // ================================================================== PdfGuard

    [Fact]
    public void Reading_a_pdf_checks_the_magic_and_reports_unreadable_files()
    {
        using var tmp = new TempDir();
        Assert.Equal(Fixtures.PdfStub(), PdfGuard.ReadBytes(tmp.WriteFile("a.pdf", Fixtures.PdfStub())));

        Assert.Equal(PdfGuard.NotPdfMessage, Assert.Throws<ConversionException>(() => PdfGuard.ReadBytes(tmp.WriteFile("b.pdf", "hello, not a pdf at all"))).Message);
        Assert.Equal(PdfGuard.NotPdfMessage, Assert.Throws<ConversionException>(() => PdfGuard.ReadBytes(tmp.WriteFile("c.pdf", "%PDF"))).Message);     // too short to be one
        Assert.Equal(PdfGuard.NotPdfMessage, Assert.Throws<ConversionException>(() => PdfGuard.ReadBytes(tmp.WriteFile("d.pdf", []))).Message);

        var missing = Assert.Throws<ConversionException>(() => PdfGuard.ReadBytes(tmp.Combine("gone.pdf")));
        Assert.StartsWith("The PDF could not be read: ", missing.Message);

        var locked = tmp.WriteFile("e.pdf", Fixtures.PdfStub());
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.StartsWith("The PDF could not be read: ", Assert.Throws<ConversionException>(() => PdfGuard.ReadBytes(locked)).Message);
    }

    [Fact]
    public void Encryption_is_detected_for_user_and_owner_passwords_and_never_thrown_for_damaged_files()
    {
        Assert.True(PdfGuard.IsEncryptedFile(Fixtures.PdfEncrypted()));
        Assert.True(PdfGuard.IsEncryptedFile(Fixtures.PdfOwnerPasswordOnly()));
        Assert.False(PdfGuard.IsEncryptedFile(Fixtures.PdfText(1)));
        Assert.False(PdfGuard.IsEncryptedFile(Fixtures.PdfCorrupt()));
        Assert.False(PdfGuard.IsEncryptedFile([]));

        Assert.Equal(PdfGuard.EncryptedMessage, Assert.Throws<ConversionException>(() => PdfGuard.RequireNotEncrypted(Fixtures.PdfEncrypted())).Message);
        PdfGuard.RequireNotEncrypted(Fixtures.PdfText(1));
    }

    [Fact]
    public void Opening_for_import_or_modify_refuses_encrypted_empty_and_damaged_documents()
    {
        foreach (var open in new Func<byte[], PdfDocument>[] { PdfGuard.OpenImport, PdfGuard.OpenModify })
        {
            using (var ok = open(Fixtures.PdfText(2))) Assert.Equal(2, ok.PageCount);
            Assert.Equal(PdfGuard.EncryptedMessage, Assert.Throws<ConversionException>(() => open(Fixtures.PdfEncrypted())).Message);
            Assert.Equal(PdfGuard.EncryptedMessage, Assert.Throws<ConversionException>(() => open(Fixtures.PdfOwnerPasswordOnly())).Message);
            Assert.Equal(PdfGuard.DamagedMessage, Assert.Throws<ConversionException>(() => open(Fixtures.PdfCorrupt())).Message);
            Assert.Equal(PdfGuard.DamagedMessage, Assert.Throws<ConversionException>(() => open(Fixtures.PdfStub())).Message);
            Assert.Equal("This PDF has no pages.", Assert.Throws<ConversionException>(() => open(BuildPdf())).Message);
        }
    }

    // ================================================================== PdfText

    [Fact]
    public void Text_extraction_reads_pages_in_order_and_separates_them_with_a_blank_line()
    {
        var text = PdfText.Extract(Fixtures.PdfText(3), default);
        var pages = text.Split(Environment.NewLine + Environment.NewLine);
        Assert.Equal(3, pages.Length);
        Assert.All(pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        Assert.Equal("", PdfText.Extract(Fixtures.PdfScan(), default));
    }

    [Fact]
    public void Text_extraction_maps_damaged_encrypted_and_cancelled_runs_to_clear_outcomes()
    {
        Assert.Equal(PdfGuard.DamagedMessage, Assert.Throws<ConversionException>(() => PdfText.Extract(Fixtures.PdfCorrupt(), default)).Message);
        Assert.Equal(PdfGuard.DamagedMessage, Assert.Throws<ConversionException>(() => PdfText.Extract([], default)).Message);
        Assert.Equal(PdfGuard.EncryptedMessage, Assert.Throws<ConversionException>(() => PdfText.Extract(Fixtures.PdfEncrypted(), default)).Message);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PdfText.Extract(Fixtures.PdfText(2), cts.Token));
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("hello world", 0, 0)]
    [InlineData("שלום", 1, 0)]                    // שלום: ם at the end
    [InlineData("םולש", 0, 1)]                    // reversed: ם at the start
    [InlineData("ם", 0, 0)]                                      // a one-letter word says nothing
    [InlineData("ךלמ and מלך", 1, 1)]
    [InlineData("אבג", 0, 0)]                          // no final letters at all
    [InlineData("שלוםשל", 0, 0)]        // ם in the middle only
    public void Final_form_letters_are_counted_at_the_ends_of_words(string text, int atEnd, int atStart) =>
        Assert.Equal((atEnd, atStart), PdfText.CountFinalForms(text));

    [Fact]
    public void Hebrew_is_judged_reversed_only_with_at_least_two_word_starts_outnumbering_ends()
    {
        var reversedWord = "םולש"; // ם at the start
        var normalWord = "שלום";
        Assert.False(PdfText.LooksReversedHebrew(reversedWord));                                // a single hit is not enough
        Assert.True(PdfText.LooksReversedHebrew(reversedWord + " " + reversedWord));
        Assert.False(PdfText.LooksReversedHebrew(reversedWord + " " + reversedWord + " " + normalWord + " " + normalWord + " " + normalWord));
        Assert.False(PdfText.LooksReversedHebrew("plain english text"));
        Assert.False(PdfText.LooksReversedHebrew(""));
    }

    [Fact]
    public void Font_resolution_knows_only_its_own_families()
    {
        PdfFonts.EnsureInitialized();
        PdfFonts.EnsureInitialized();
        Assert.True(PdfFonts.IsInstalled("Arial"));
        Assert.True(PdfFonts.IsInstalled("arial")); // case-insensitive
        Assert.False(PdfFonts.IsInstalled("Comic Papyrus 3000"));
        Assert.False(PdfFonts.IsInstalled(""));
    }

    // ================================================================== PdfRender

    [Theory]
    [InlineData(612.0, 792.0, 1224, 1584)]            // 2x natural size
    [InlineData(0.0, 0.0, 1, 1)]                      // degenerate pages still get a pixel
    [InlineData(0.2, 0.2, 1, 1)]
    [InlineData(5000.0, 2500.0, 8000, 4000)]          // capped at 8000 on the long side, aspect kept
    [InlineData(2500.0, 5000.0, 4000, 8000)]
    [InlineData(4000.0, 4000.0, 8000, 8000)]
    public void Pages_render_at_twice_their_size_capped_at_8000_pixels(double wDip, double hDip, int w, int h) =>
        Assert.Equal((w, h), PdfRender.TargetSize(wDip, hDip));

    [Fact]
    public async Task Rendering_refuses_damaged_and_encrypted_documents_with_clear_messages()
    {
        var damaged = await Assert.ThrowsAsync<ConversionException>(() => PdfRender.OpenAsync(Fixtures.PdfCorrupt()));
        Assert.Equal(PdfGuard.DamagedMessage, damaged.Message);
        var locked = await Assert.ThrowsAsync<ConversionException>(() => PdfRender.OpenAsync(Fixtures.PdfEncrypted()));
        Assert.Equal(PdfGuard.EncryptedMessage, locked.Message);
        var none = await Assert.ThrowsAsync<ConversionException>(() => PdfRender.OpenAsync(BuildPdf()));
        Assert.True(none.Message is "This PDF has no pages." or "This PDF is damaged and can't be read.", none.Message);
        var doc = await PdfRender.OpenAsync(Fixtures.PdfText(2));
        Assert.Equal(2u, doc.PageCount);
    }

    // ================================================================== PdfPixels

    private static PdfPixels.Raster Gray(int w, int h) =>
        new([.. Enumerable.Range(0, w * h).Select(i => (byte)i)], w, h, 1);

    private static int[][] Ids(PdfPixels.Raster r) =>
        [.. Enumerable.Range(0, r.Height).Select(y => Enumerable.Range(0, r.Width).Select(x => (int)r.Data[(y * r.Width + x) * r.Channels]).ToArray())];

    [Theory]
    [InlineData(100, 50, 0, 100, 50)]       // 0 = unlimited
    [InlineData(100, 50, 100, 100, 50)]
    [InlineData(100, 50, 200, 100, 50)]     // never enlarged
    [InlineData(4000, 2000, 2000, 2000, 1000)]
    [InlineData(2000, 4000, 2000, 1000, 2000)]
    [InlineData(10000, 3, 100, 100, 1)]     // the short side never drops below one pixel
    public void The_long_side_is_limited_and_the_short_side_follows(int w, int h, int max, int ew, int eh) =>
        Assert.Equal((ew, eh), PdfPixels.FitLongSide(w, h, max));

    [Fact]
    public void Flattening_composites_over_white_in_place_and_leaves_opaque_pixels_alone()
    {
        byte[] px = [10, 20, 30, 255, 0, 0, 0, 0, 100, 100, 100, 128];
        PdfPixels.FlattenOnWhite(px);
        Assert.Equal([10, 20, 30, 255], px[..4]);
        Assert.Equal([255, 255, 255, 255], px[4..8]);
        Assert.Equal(255, px[11]);
        Assert.InRange(px[8], 176, 179);
        PdfPixels.FlattenOnWhite([1, 2, 3]); // a ragged tail is ignored, not an error
    }

    [Fact]
    public void Downscaling_averages_boxes_and_does_nothing_when_the_target_is_not_smaller()
    {
        var gray = new PdfPixels.Raster([0, 100, 200, 100, 0, 100, 200, 100, 0, 100, 200, 100, 0, 100, 200, 100], 4, 4, 1);
        var half = PdfPixels.Downscale(gray, 2, 2);
        Assert.Equal((2, 2), (half.Width, half.Height));
        Assert.Equal([[50, 150], [50, 150]], Ids(half));
        Assert.Same(gray.Data, PdfPixels.Downscale(gray, 4, 4).Data);
        Assert.Same(gray.Data, PdfPixels.Downscale(gray, 10, 10).Data);
        var oneRow = PdfPixels.Downscale(gray, 4, 1);               // only the height shrinks
        Assert.Equal((4, 1), (oneRow.Width, oneRow.Height));
        var tiny = PdfPixels.Downscale(gray, 0, 0);                  // clamped to one pixel
        Assert.Equal((1, 1), (tiny.Width, tiny.Height));
        Assert.Equal(100, tiny.Data[0]);

        var bgra = new PdfPixels.Raster([10, 20, 30, 255, 30, 40, 50, 255], 2, 1, 4);
        var one = PdfPixels.Downscale(bgra, 1, 1);
        Assert.Equal([20, 30, 40, 255], one.Data);
        var uneven = PdfPixels.Downscale(Gray(5, 5), 2, 3);          // 5 does not divide evenly
        Assert.Equal((2, 3), (uneven.Width, uneven.Height));
    }

    [Theory]
    [InlineData(0, new[] { 0, 1, 2 }, new[] { 3, 4, 5 })]
    [InlineData(1, new[] { 0, 1, 2 }, new[] { 3, 4, 5 })]
    [InlineData(9, new[] { 0, 1, 2 }, new[] { 3, 4, 5 })]
    [InlineData(2, new[] { 2, 1, 0 }, new[] { 5, 4, 3 })]
    [InlineData(3, new[] { 5, 4, 3 }, new[] { 2, 1, 0 })]
    [InlineData(4, new[] { 3, 4, 5 }, new[] { 0, 1, 2 })]
    public void Orientations_that_keep_the_dimensions(int orientation, int[] row0, int[] row1)
    {
        var r = PdfPixels.ApplyOrientation(Gray(3, 2), orientation);
        Assert.Equal((3, 2), (r.Width, r.Height));
        Assert.Equal([row0, row1], Ids(r));
    }

    [Theory]
    [InlineData(5, new[] { 0, 3 }, new[] { 1, 4 }, new[] { 2, 5 })]
    [InlineData(6, new[] { 3, 0 }, new[] { 4, 1 }, new[] { 5, 2 })]
    [InlineData(7, new[] { 5, 2 }, new[] { 4, 1 }, new[] { 3, 0 })]
    [InlineData(8, new[] { 2, 5 }, new[] { 1, 4 }, new[] { 0, 3 })]
    public void Orientations_that_swap_the_dimensions(int orientation, int[] row0, int[] row1, int[] row2)
    {
        var r = PdfPixels.ApplyOrientation(Gray(3, 2), orientation);
        Assert.Equal((2, 3), (r.Width, r.Height));
        Assert.Equal([row0, row1, row2], Ids(r));
    }

    [Fact]
    public void Orientation_works_on_four_channel_pixels_too()
    {
        var bgra = new PdfPixels.Raster([1, 1, 1, 255, 2, 2, 2, 255], 2, 1, 4);
        var rotated = PdfPixels.ApplyOrientation(bgra, 6);
        Assert.Equal((1, 2), (rotated.Width, rotated.Height));
        Assert.Equal([1, 1, 1, 255, 2, 2, 2, 255], rotated.Data);
    }

    [Fact]
    public async Task Encoding_and_decoding_round_trip_png_exactly_and_jpeg_closely_for_color()
    {
        var color = new PdfPixels.Raster(Fixtures.QuadrantsBgra(16, 12), 16, 12, 4);
        var png = await PdfPixels.EncodePngAsync(color);
        var backColor = await PdfPixels.DecodeBytesAsync(png, 4, 0, colorManage: false);
        Assert.Equal(color.Data, backColor.Data);

        var jpeg = await PdfPixels.EncodeJpegAsync(color, 0.95);
        var viaJpeg = await PdfPixels.DecodeBytesAsync(jpeg, 4, 0, colorManage: false);
        Assert.Equal((16, 12), (viaJpeg.Width, viaJpeg.Height));
        Assert.True(Enumerable.Range(0, color.Data.Length).Where(i => i % 4 != 3).Average(i => Math.Abs(color.Data[i] - viaJpeg.Data[i])) < 12);

        var scaled = await PdfPixels.DecodeBytesAsync(png, 4, 8, colorManage: false);   // long side limited while decoding
        Assert.Equal((8, 6), (scaled.Width, scaled.Height));
    }

    [Fact]
    public async Task One_channel_rasters_round_trip_through_png_and_jpeg()
    {
        var gray = new PdfPixels.Raster([.. Enumerable.Range(0, 64).Select(i => (byte)(i * 4))], 8, 8, 1);
        var png = await PdfPixels.EncodePngAsync(gray);
        Assert.Equal(gray.Data, (await PdfPixels.DecodeBytesAsync(png, 1, 0, colorManage: false)).Data);
        var jpeg = await PdfPixels.EncodeJpegAsync(gray, 0.9);
        Assert.Equal(1, (await PdfPixels.DecodeBytesAsync(jpeg, 1, 0, colorManage: true)).Channels);
    }

    [Fact]
    public async Task Orientation_is_read_from_exif_and_defaults_to_upright()
    {
        using var tmp = new TempDir();
        async Task<int> OrientationOf(byte[] bytes)
        {
            using var fs = new FileStream(tmp.WriteFile(Guid.NewGuid().ToString("N") + ".img", bytes), FileMode.Open, FileAccess.Read);
            using var ras = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fs);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            return await PdfPixels.ReadOrientationAsync(await decoder.GetFrameAsync(0));
        }
        Assert.Equal(6, await OrientationOf(Fixtures.JpegWithMetadata(32, 24, orientation: 6)));
        Assert.Equal(1, await OrientationOf(Fixtures.JpegWithMetadata(32, 24, orientation: 1)));
        Assert.Equal(1, await OrientationOf(Fixtures.Png(8, 8)));
    }

    // ================================================================== PdfImageSource

    [Fact]
    public async Task Image_sources_become_upright_opaque_jpeg_pages()
    {
        using var tmp = new TempDir();
        var plain = await PdfImageSource.LoadAsync(tmp.WriteFile("a.png", Fixtures.PngAlpha(32, 24)), default);
        var page = Assert.Single(plain);
        Assert.Equal((32, 24), (page.Width, page.Height));
        Assert.Equal([0xFF, 0xD8, 0xFF], page.Jpeg[..3]);

        var rotated = await PdfImageSource.LoadAsync(tmp.WriteFile("r.jpg", Fixtures.JpegWithMetadata(64, 32, orientation: 6)), default);
        Assert.Equal((32, 64), (rotated[0].Width, rotated[0].Height));

        var pages = await PdfImageSource.LoadAsync(tmp.WriteFile("m.tif", Fixtures.TiffMultiFrame(32, 24, frames: 3)), default);
        Assert.Equal(3, pages.Count);

        var wide = await PdfImageSource.LoadAsync(tmp.WriteFile("w.png", Fixtures.PdfSourcePng(4000, 300)), default);
        Assert.Equal((PdfImageSource.MaxLongSide, 263), (wide[0].Width, wide[0].Height));   // A4 at 300 dpi is the cap

        var gifFirstFrameOnly = await PdfImageSource.LoadAsync(tmp.WriteFile("g.gif", Fixtures.Gif(16, 12, animated: true)), default);
        Assert.Single(gifFirstFrameOnly);
    }

    [Fact]
    public async Task Pictures_without_a_wic_decoder_become_upright_opaque_jpeg_pages_through_skia()
    {
        // WebP on a PC without the WebP Image Extension (Windows Server) reaches this path; SkiaSharp also reads the formats below, so
        // the path is checked on every PC.
        using var tmp = new TempDir();
        var webp = await PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("a.webp", Fixtures.Webp(64, 48)));
        Assert.Equal((64, 48), (webp.Width, webp.Height));
        Assert.Equal([0xFF, 0xD8, 0xFF], webp.Jpeg[..3]);

        var rotated = await PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("r.jpg", Fixtures.JpegWithMetadata(64, 32, orientation: 6)));
        Assert.Equal((32, 64), (rotated.Width, rotated.Height));

        var wide = await PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("w.png", Fixtures.PdfSourcePng(4000, 300)));
        Assert.Equal((PdfImageSource.MaxLongSide, 263), (wide.Width, wide.Height));

        // The transparent top-left quadrant is flattened on white, not left black.
        var alpha = await PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("t.png", Fixtures.PngAlpha(64, 48)));
        using var page = SkiaSharp.SKBitmap.Decode(alpha.Jpeg);
        var corner = page.GetPixel(8, 8);
        Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240, $"corner is {corner}");

        var junk = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("junk.webp", Fixtures.Noise(400))));
        Assert.Equal("junk.webp could not be read as an image.", junk.Message);
        var big = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadWithSkiaAsync(tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000))));
        Assert.Equal("huge.png is too large to convert (over 200 megapixels).", big.Message);
    }

    [Fact]
    public async Task Unusable_image_sources_say_which_file_and_why()
    {
        using var tmp = new TempDir();
        var big = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadAsync(tmp.WriteFile("huge.png", Fixtures.PngHeaderOnly(20_000, 15_000)), default));
        Assert.Equal("huge.png is too large to convert (over 200 megapixels).", big.Message);
        var junk = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadAsync(tmp.WriteFile("junk.png", Fixtures.Noise(400)), default));
        Assert.Equal("junk.png could not be read as an image.", junk.Message);
        var missing = await Assert.ThrowsAsync<ConversionException>(() => PdfImageSource.LoadAsync(tmp.Combine("gone.png"), default));
        Assert.StartsWith("gone.png could not be opened: ", missing.Message);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfImageSource.LoadAsync(tmp.WriteFile("ok.tif", Fixtures.TiffMultiFrame(16, 12, 2)), cts.Token));
    }

    [Fact]
    public async Task Merging_nothing_is_an_error_and_a_null_list_is_rejected()
    {
        var none = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeAsync([], default));
        Assert.Equal("Nothing was selected.", none.Message);
        await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeImagesAsync([], default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => PdfConverter.MergeAsync(null!, default));
    }

    // ================================================================== raw PDF construction for the recompressor

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>A one-page PDF whose resources list the given image objects (object numbers 5, 6, 7...; extras follow the images).</summary>
    private static byte[] BuildPdf(params (string Dict, byte[] Stream)[] objects)
    {
        var parts = new List<byte[]>();
        var offsets = new List<int>();
        var pos = 0;
        void Add(byte[] b) { parts.Add(b); pos += b.Length; }
        void Obj(string text, byte[]? stream = null)
        {
            offsets.Add(pos);
            Add(Ascii(text));
            if (stream is not null) { Add(Ascii("\nstream\n")); Add(stream); Add(Ascii("\nendstream")); }
            Add(Ascii("\nendobj\n"));
        }
        Add(Ascii("%PDF-1.4\n"));
        var images = string.Concat(objects.Select((_, i) => $"/Im{i} {5 + i} 0 R "));
        var kids = objects.Length == 0 ? "[]" : "[3 0 R]";
        Obj("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>");
        Obj($"2 0 obj\n<</Type/Pages/Kids{kids}/Count {(objects.Length == 0 ? 0 : 1)}>>");
        Obj($"3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</XObject<<{images}>>>>/Contents 4 0 R>>");
        Obj("4 0 obj\n<</Length 3>>", Ascii("q Q"));
        for (var i = 0; i < objects.Length; i++)
            Obj($"{5 + i} 0 obj\n<<{objects[i].Dict}/Length {objects[i].Stream.Length}>>", objects[i].Stream);

        var xref = pos;
        var sb = new StringBuilder($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {offsets.Count + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        Add(Ascii(sb.ToString()));
        return [.. parts.SelectMany(p => p)];
    }

    private const string RgbImage = "/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/BitsPerComponent 8";

    private static byte[] Photo(int w, int h, int seed = 1) => Fixtures.PdfSourcePixels(w, h, seed);

    private static byte[] Rgb(byte[] bgra)
    {
        var rgb = new byte[bgra.Length / 4 * 3];
        for (int i = 0, o = 0; i < bgra.Length; i += 4, o += 3) { rgb[o] = bgra[i + 2]; rgb[o + 1] = bgra[i + 1]; rgb[o + 2] = bgra[i]; }
        return rgb;
    }

    private static byte[] GrayOf(byte[] bgra)
    {
        var gray = new byte[bgra.Length / 4];
        for (var i = 0; i < gray.Length; i++) gray[i] = bgra[i * 4 + 2];
        return gray;
    }

    private static byte[] Flate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        return ms.ToArray();
    }

    /// <summary>Applies PNG row filters (cycling through the five types) as a PDF /Predictor 15 stream expects.</summary>
    private static byte[] PngPredict(byte[] raw, int w, int h, int bpp)
    {
        var row = w * bpp;
        var output = new byte[(row + 1) * h];
        for (var y = 0; y < h; y++)
        {
            var filter = y % 5;
            output[y * (row + 1)] = (byte)filter;
            for (var x = 0; x < row; x++)
            {
                int cur = raw[y * row + x];
                int left = x >= bpp ? raw[y * row + x - bpp] : 0;
                int up = y > 0 ? raw[(y - 1) * row + x] : 0;
                int upLeft = y > 0 && x >= bpp ? raw[(y - 1) * row + x - bpp] : 0;
                int predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    _ => Paeth(left, up, upLeft),
                };
                output[y * (row + 1) + 1 + x] = (byte)(cur - predicted);
            }
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static async Task<(int Replaced, PdfDocument Doc)> Recompress(byte[] pdf)
    {
        var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        var replaced = await PdfImageCompressor.RecompressAsync(doc, default);
        return (replaced, doc);
    }

    private static PdfSharp.Pdf.PdfDictionary[] Images(PdfDocument doc) =>
        [.. doc.Internals.GetAllObjects().OfType<PdfSharp.Pdf.PdfDictionary>().Where(d => d.Elements.GetName("/Subtype") == "/Image")];

    // ================================================================== PdfImageCompressor

    [Fact]
    public async Task A_photographic_flate_image_is_replaced_by_a_smaller_jpeg_that_still_looks_the_same()
    {
        var bgra = Photo(160, 120);
        var raw = Rgb(bgra);
        var (replaced, doc) = await Recompress(BuildPdf(($"{RgbImage}/Width 160/Height 120/Filter/FlateDecode", Flate(raw))));
        using (doc)
        {
            Assert.Equal(1, replaced);
            var image = Assert.Single(Images(doc));
            Assert.Equal("/DCTDecode", image.Elements.GetName("/Filter"));
            Assert.Equal((160, 120), (image.Elements.GetInteger("/Width"), image.Elements.GetInteger("/Height")));
            Assert.True(image.Stream.Value.Length < Flate(raw).Length * 0.7);
            var back = await PdfPixels.DecodeBytesAsync(image.Stream.Value, 4, 0, colorManage: false);
            Assert.True(Enumerable.Range(0, bgra.Length).Where(i => i % 4 != 3).Average(i => Math.Abs(bgra[i] - back.Data[i])) < 30);
        }
    }

    [Fact]
    public async Task Filter_and_parameter_arrays_with_a_single_entry_are_understood_and_every_png_filter_type_is_undone()
    {
        var bgra = Photo(160, 120, seed: 5);
        var raw = Rgb(bgra);
        var dict = $"{RgbImage}/Width 160/Height 120/Filter[/FlateDecode]/DecodeParms[<</Predictor 15/Colors 3/Columns 160/BitsPerComponent 8>>]";
        var (replaced, doc) = await Recompress(BuildPdf((dict, Flate(PngPredict(raw, 160, 120, 3)))));
        using (doc)
        {
            Assert.Equal(1, replaced);
            var image = Assert.Single(Images(doc));
            Assert.Equal("/DCTDecode", image.Elements.GetName("/Filter"));
            var back = await PdfPixels.DecodeBytesAsync(image.Stream.Value, 4, 0, colorManage: false);
            // Rows use filter types 0..4 in turn; if any were undone wrongly the picture would be visibly garbled.
            Assert.True(Enumerable.Range(0, bgra.Length).Where(i => i % 4 != 3).Average(i => Math.Abs(bgra[i] - back.Data[i])) < 30);
        }
    }

    [Fact]
    public async Task Icc_based_rgb_flate_images_are_recompressed()
    {
        var rgb = Rgb(Photo(200, 150, seed: 2));
        var (replaced, doc) = await Recompress(BuildPdf(
            ("/Type/XObject/Subtype/Image/ColorSpace[/ICCBased 6 0 R]/BitsPerComponent 8/Width 200/Height 150/Filter/FlateDecode", Flate(rgb)),
            ("/N 3", Array.Empty<byte>())));
        using (doc)
        {
            Assert.Equal(1, replaced);
            Assert.Equal("/DCTDecode", Images(doc).First(i => i.Elements.GetInteger("/Width") == 200).Elements.GetName("/Filter"));
        }
    }

    [Fact]
    public async Task Gray_flate_images_are_recompressed()
    {
        var gray = GrayOf(Photo(300, 200, seed: 2));
        var (replaced, doc) = await Recompress(BuildPdf(("/Type/XObject/Subtype/Image/ColorSpace/DeviceGray/BitsPerComponent 8/Width 300/Height 200/Filter/FlateDecode", Flate(gray))));
        using (doc)
        {
            Assert.Equal(1, replaced);
            Assert.Equal("/DCTDecode", Assert.Single(Images(doc)).Elements.GetName("/Filter"));
        }
    }

    [Fact]
    public async Task Flate_images_with_a_direct_png_predictor_dictionary_are_recompressed()
    {
        var bgra = Photo(160, 120, seed: 4);
        var raw = Rgb(bgra);
        var dict = $"{RgbImage}/Width 160/Height 120/Filter/FlateDecode/DecodeParms<</Predictor 15/Colors 3/Columns 160/BitsPerComponent 8>>";
        var (replaced, doc) = await Recompress(BuildPdf((dict, Flate(PngPredict(raw, 160, 120, 3)))));
        using (doc)
        {
            Assert.Equal(1, replaced);
            var image = Assert.Single(Images(doc));
            var back = await PdfPixels.DecodeBytesAsync(image.Stream.Value, 4, 0, colorManage: false);
            Assert.True(Enumerable.Range(0, bgra.Length).Where(i => i % 4 != 3).Average(i => Math.Abs(bgra[i] - back.Data[i])) < 30, "the predictor was undone incorrectly");
        }
    }

    private static byte[] LineArt(int w, int h, int colors)
    {
        var rng = new Random(11);
        var palette = Enumerable.Range(0, colors).Select(c => (R: (byte)(c * 60 % 256), G: (byte)(c * 90 % 256), B: (byte)(c * 30 % 256))).ToArray();
        var raw = new byte[w * h * 3];
        for (var i = 0; i < w * h; i++)
        {
            var c = palette[rng.Next(colors)];
            raw[i * 3] = c.R; raw[i * 3 + 1] = c.G; raw[i * 3 + 2] = c.B;
        }
        return raw;
    }

    [Fact]
    public async Task Images_that_would_look_worse_as_jpeg_or_that_are_too_small_are_left_alone()
    {
        var lineArt = Flate(LineArt(400, 300, 4));
        Assert.True(lineArt.Length >= 16 * 1024, "fixture must be big enough to be considered");
        var tinyRaw = Flate(Rgb(Photo(16, 16)));
        var (replaced, doc) = await Recompress(BuildPdf(
            ($"{RgbImage}/Width 400/Height 300/Filter/FlateDecode", lineArt),
            ($"{RgbImage}/Width 16/Height 16/Filter/FlateDecode", tinyRaw)));
        using (doc)
        {
            Assert.Equal(0, replaced);
            Assert.All(Images(doc), i => Assert.Equal("/FlateDecode", i.Elements.GetName("/Filter")));
        }
    }

    [Theory]
    [InlineData("/SMask 6 0 R", "soft mask")]
    [InlineData("/Mask [0 0 0 0 0 0]", "colour key mask")]
    [InlineData("/Decode [1 0 1 0 1 0]", "inverted decode array")]
    [InlineData("/ImageMask true", "stencil")]
    [InlineData("/BitsPerComponent 4", "4-bit samples")]
    public async Task Images_with_masks_or_unusual_sample_formats_are_never_touched(string extra, string why)
    {
        var raw = Rgb(Photo(160, 120));
        var dict = $"/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/Width 160/Height 120/Filter/FlateDecode {extra}";
        if (!extra.Contains("BitsPerComponent")) dict += "/BitsPerComponent 8";
        var tinyMask = ("/Type/XObject/Subtype/Image/ColorSpace/DeviceGray/BitsPerComponent 8/Width 2/Height 2/Filter/FlateDecode", Flate(new byte[4]));
        var (replaced, doc) = await Recompress(BuildPdf((dict, Flate(raw)), tinyMask));
        using (doc)
        {
            Assert.Equal(0, replaced);
            Assert.All(Images(doc), i => Assert.Equal("/FlateDecode", i.Elements.GetName("/Filter")));
        }
        _ = why;
    }

    [Theory]
    [InlineData("/ColorSpace/DeviceCMYK", "CMYK")]
    [InlineData("/ColorSpace[/Indexed/DeviceRGB 1 <000000FFFFFF>]", "indexed")]
    [InlineData("/ColorSpace/CalRGB", "calibrated")]
    [InlineData("/ColorSpace[/ICCBased 4 0 R]", "ICC profile without a usable N")]
    public async Task Colour_spaces_other_than_gray_and_rgb_are_left_alone(string colorSpace, string why)
    {
        var raw = Flate(Rgb(Photo(160, 120)));
        var (replaced, doc) = await Recompress(BuildPdf(($"/Type/XObject/Subtype/Image{colorSpace}/BitsPerComponent 8/Width 160/Height 120/Filter/FlateDecode", raw)));
        using (doc) Assert.Equal(0, replaced);
        _ = why;
    }

    [Theory]
    [InlineData("/Width 0/Height 120/Filter/FlateDecode")]
    [InlineData("/Width 160/Height 0/Filter/FlateDecode")]
    [InlineData("/Width 160/Height 120")]                                                              // no filter at all
    [InlineData("/Width 160/Height 120/Filter[/ASCIIHexDecode/FlateDecode]")]                          // a filter chain
    [InlineData("/Width 160/Height 120/Filter/LZWDecode")]
    [InlineData("/Width 160/Height 120/Filter/FlateDecode/DecodeParms/Weird")]                          // parameters we cannot interpret
    [InlineData("/Width 160/Height 120/Filter/FlateDecode/DecodeParms<</Predictor 2/Colors 3/Columns 160>>")] // TIFF predictor
    [InlineData("/Width 160/Height 120/Filter/FlateDecode/DecodeParms<</Predictor 15/Colors 1/Columns 160>>")]
    [InlineData("/Width 160/Height 120/Filter/FlateDecode/DecodeParms<</Predictor 15/Colors 3/Columns 99>>")]
    [InlineData("/Width 160/Height 120/Filter/FlateDecode/DecodeParms<</Predictor 15/Colors 3/Columns 160/BitsPerComponent 16>>")]
    public async Task Streams_with_unsupported_filters_parameters_or_geometry_are_left_alone(string tail)
    {
        var raw = Rgb(Photo(160, 120));
        var data = tail.Contains("Predictor") ? Flate(PngPredict(raw, 160, 120, 3)) : Flate(raw);
        if (!tail.Contains("Filter")) data = raw;
        var (replaced, doc) = await Recompress(BuildPdf(($"{RgbImage}{tail}", data)));
        using (doc) Assert.Equal(0, replaced);
    }

    [Fact]
    public async Task Corrupt_or_short_flate_data_and_bad_predictor_rows_are_survived()
    {
        var raw = Rgb(Photo(160, 120));
        var junkStream = Fixtures.Noise(20_000, seed: 8);
        var shortFlate = Flate(raw[..(raw.Length / 2)]);                      // fewer rows than the dictionary claims
        var predicted = PngPredict(raw, 160, 120, 3);
        predicted[0] = 9;                                                      // not a PNG filter type
        var badRow = Flate(predicted);
        var truncatedPredicted = Flate(PngPredict(raw, 160, 120, 3)[..40000]);  // predictor data shorter than the image
        var predictor = "/DecodeParms<</Predictor 15/Colors 3/Columns 160/BitsPerComponent 8>>";
        var (replaced, doc) = await Recompress(BuildPdf(
            ($"{RgbImage}/Width 160/Height 120/Filter/FlateDecode", junkStream),
            ($"{RgbImage}/Width 160/Height 120/Filter/FlateDecode", shortFlate),
            ($"{RgbImage}/Width 160/Height 120/Filter/FlateDecode{predictor}", badRow),
            ($"{RgbImage}/Width 160/Height 120/Filter/FlateDecode{predictor}", truncatedPredicted)));
        using (doc) Assert.Equal(0, replaced);
    }

    [Fact]
    public async Task Jpeg_images_are_recompressed_downscaled_and_skipped_by_the_rules()
    {
        // High quality, ordinary size: re-encoding at the lower quality saves well over 10 %.
        var highQuality = Fixtures.PhotoJpeg(400, 300, quality: 98);
        // Wider than the 2400 px cap: scaled down while recompressing.
        var wide = Fixtures.PhotoJpeg(2500, 300, quality: 97);
        // Already low quality: a second pass would grow it.
        var lowQuality = Fixtures.PhotoJpeg(1600, 1200, quality: 25);
        // The dictionary disagrees with the real picture size: not touched.
        var mismatched = Fixtures.PhotoJpeg(400, 300, quality: 98);
        Assert.True(lowQuality.Length >= 16 * 1024, $"low quality fixture is only {lowQuality.Length} bytes");

        var (replaced, doc) = await Recompress(BuildPdf(
            ("/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/BitsPerComponent 8/Width 400/Height 300/Filter/DCTDecode", highQuality),
            ("/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/BitsPerComponent 8/Width 2500/Height 300/Filter/DCTDecode", wide),
            ("/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/BitsPerComponent 8/Width 1600/Height 1200/Filter/DCTDecode", lowQuality),
            ("/Type/XObject/Subtype/Image/ColorSpace/DeviceRGB/BitsPerComponent 8/Width 100/Height 100/Filter/DCTDecode", mismatched)));
        using (doc)
        {
            Assert.Equal(2, replaced);
            var images = Images(doc);
            Assert.True(images[0].Stream.Value.Length < highQuality.Length * 0.9);
            Assert.Equal((2400, 288), (images[1].Elements.GetInteger("/Width"), images[1].Elements.GetInteger("/Height")));
            Assert.Equal(lowQuality, images[2].Stream.Value);
            Assert.Equal(mismatched, images[3].Stream.Value);
        }
    }

    [Fact]
    public async Task Recompression_stops_when_cancelled_and_ignores_pdfs_without_images()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var doc = PdfReader.Open(new MemoryStream(Fixtures.PdfText(1)), PdfDocumentOpenMode.Modify);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfImageCompressor.RecompressAsync(doc, cts.Token));
        Assert.Equal(0, await PdfImageCompressor.RecompressAsync(doc, default));
    }
}
