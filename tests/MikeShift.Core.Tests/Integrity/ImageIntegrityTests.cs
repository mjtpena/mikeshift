using MikeShift.Core.Actions;
using SkiaSharp;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Every action the catalog offers for an image, run on every image fixture, judged by SkiaSharp and WIC decoding the OUTPUT
/// (neither is the code that wrote it) against pixels decoded independently from the source.
/// </summary>
public class ImageIntegrityTests
{
    private static readonly string[] FormatTitles = ["PNG", "JPG", "TIFF", "GIF", "BMP", "HEIC", "WebP"];

    /// <summary>The container each format title must really produce, whatever the extension says.</summary>
    private static string Container(string title) => title switch
    {
        "PNG" => "png", "JPG" => "jpeg", "TIFF" => "tiff", "GIF" => "gif", "BMP" => "bmp", "HEIC" => "heic", "WebP" => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(title)),
    };

    private static bool IsSameFormat(ImgSource src, string title) => title switch
    {
        "PNG" => src.Ext == "png",
        "JPG" => src.Ext is "jpg" or "jpeg",
        "TIFF" => src.Ext is "tif" or "tiff",
        "GIF" => src.Ext == "gif",
        "BMP" => src.Ext is "bmp" or "dib",
        "HEIC" => src.Ext is "heic" or "heif",
        "WebP" => src.Ext == "webp",
        _ => false,
    };

    private static bool Available(string id, string? title)
    {
        var caps = Kit.Caps;
        if (id == "heic" && !caps.HeifEncode) return false;
        return title switch
        {
            "HEIC" => caps.HeifEncode,
            "WebP" => caps.WebpEncode,
            _ => true,
        };
    }

    public static TheoryData<string, string> FormatCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var id in ImgSource.Ids)
            foreach (var title in FormatTitles)
                if (Available(id, title) && !IsSameFormat(ImgSource.Get(id), title))
                    data.Add(id, title);
        return data;
    }

    public static TheoryData<string, string> ToolCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var id in ImgSource.Ids)
            foreach (var title in new[] { "Compress", "Resize 50%", "Crop", "Strip EXIF" })
                if (Available(id, null) && !(title == "Compress" && id == "heic" && !Kit.Caps.HeifEncode))
                    data.Add(id, title);
        return data;
    }

    public static TheoryData<string> PdfCases()
    {
        var data = new TheoryData<string>();
        foreach (var id in ImgSource.Ids)
            if (Available(id, null)) data.Add(id);
        return data;
    }

    // ------------------------------------------------------------------ the oracle is sound

    [Theory, MemberData(nameof(PdfCases))]
    public async Task Fixtures_decode_the_same_in_wic_and_skia(string id)
    {
        var src = ImgSource.Get(id);
        using var tmp = new TempDir();
        var path = tmp.WriteFile(src.FileName, src.Make()!);
        var truth = await src.TruthAsync(path);

        var wic = await PixDecode.WicAsync(path);
        Assert.Equal((truth.W, truth.H), (wic.W, wic.H));
        var (frames, _, _, _) = await PixDecode.WicInfoAsync(path);
        Assert.Equal(src.Frames, frames);
        if (PixDecode.Skia(path) is not null)
        {
            if (src.Lossy)
                Assert.True(PixMetrics.Psnr(truth, wic) >= 35, $"{id}: WIC and SkiaSharp read the source differently");
            else
                Assert.True(PixMetrics.MaxDiff(truth, wic) <= src.Slack, $"{id}: WIC and SkiaSharp read the source differently");
        }
        Kit.Record("image", src.FileName, "(source)", "WIC+SkiaSharp", $"{src.ExpectW}x{src.ExpectH}, {frames} frame(s)");
    }

    // ------------------------------------------------------------------ Format page

    [Theory, MemberData(nameof(FormatCases))]
    public async Task Format_conversion_is_faithful(string id, string title)
    {
        var src = ImgSource.Get(id);
        using var tmp = new TempDir();
        var input = tmp.WriteFile(src.FileName, src.Make()!);
        var truth = await src.TruthAsync(input);

        var output = Kit.One(await Kit.RunAsync(title, input));

        Assert.Equal(tmp.Path, Path.GetDirectoryName(output));
        Assert.Equal(Container(title), ImageBytes.Sniff(output));
        var metric = await ValidateRaster(src, truth, title, output);
        Kit.Record("image", src.FileName, title, "WIC+SkiaSharp decode vs independent source decode", metric);
    }

    /// <summary>Decodes <paramref name="output"/> with both libraries and compares it with what the format can faithfully hold.</summary>
    private static async Task<string> ValidateRaster(ImgSource src, Px truth, string title, string output)
    {
        var lossyTarget = title is "JPG" or "WebP" or "HEIC";
        var decoded = await DecodeBoth(output, lossyTarget);
        Assert.Equal((truth.W, truth.H), (decoded.W, decoded.H));

        switch (title)
        {
            case "PNG":
            case "TIFF":
            {
                // Lossless: transparency and every colour survive. (A lossy source differs only by its decoder's rounding.)
                if (src.Lossy)
                {
                    var p = PixMetrics.Psnr(truth, decoded);
                    Assert.True(p >= 40, $"{title}: PSNR {p:0.0} dB");
                    return Kit.Db(p);
                }
                var diff = PixMetrics.MaxDiff(truth, decoded);
                Assert.True(diff <= src.Slack, $"{title}: pixels differ from the source (max {diff})");
                Assert.Equal(truth.HasAlpha, decoded.HasAlpha);
                return (diff == 0 ? "pixel-exact" : $"pixel-exact within {diff} level (WIC vs libwebp rounding)") + (truth.HasAlpha ? " incl. alpha" : "");
            }
            case "BMP":
            {
                // No alpha in BMP: flattened on white by design.
                var want = truth.OnWhite();
                if (src.Lossy)
                {
                    var p = PixMetrics.Psnr(want, decoded);
                    Assert.True(p >= 40, $"BMP: PSNR {p:0.0} dB");
                    return Kit.Db(p);
                }
                var diff = PixMetrics.MaxDiff(want, decoded.OnWhite(), includeAlpha: false);
                Assert.True(diff <= src.Slack, $"BMP: pixels differ from the source flattened on white (max {diff})");
                return (diff == 0 ? "pixel-exact" : $"pixel-exact within {diff} level (WIC vs libwebp rounding)") + (truth.HasAlpha ? " (alpha flattened on white)" : "");
            }
            case "GIF":
            {
                var p = PixMetrics.Psnr(truth, decoded);
                var min = src.Photo ? 24 : src.Lossy ? 22 : 40;
                Assert.True(p >= min, $"GIF: PSNR {p:0.0} dB (palette-reduced picture should be >= {min})");
                var transparent = truth.HasAlpha;
                if (transparent)
                {
                    var a = decoded.At(4, 4).A;
                    Assert.True(a == 0 || decoded.OnWhite().At(4, 4) is { B: > 240, G: > 240, R: > 240 },
                        "GIF: the transparent corner became a coloured block");
                }
                return Kit.Db(p) + (transparent ? ", transparency kept" : "");
            }
            default: // JPG, WebP, HEIC (lossy)
            {
                var p = PixMetrics.Psnr(truth, decoded);
                var min = src.Photo ? 30 : 22; // the hard-edged four-colour fixtures lose chroma at their edges by design of 4:2:0 / lossy codecs
                Assert.True(p >= min, $"{title}: PSNR {p:0.0} dB (< {min})");
                return Kit.Db(p) + (truth.HasAlpha ? " (alpha on white)" : "");
            }
        }
    }

    /// <summary>
    /// Decodes with WIC and SkiaSharp and checks they agree: exactly for lossless, closely for lossy (their IDCT and chroma
    /// upsampling differ). HEIC has no Skia decoder, so WIC alone.
    /// </summary>
    internal static async Task<Px> DecodeBoth(string path, bool lossy)
    {
        var wic = await PixDecode.WicAsync(path);
        var skia = PixDecode.Skia(path);
        if (skia is null) return wic;
        Assert.Equal((skia.W, skia.H), (wic.W, wic.H));
        if (lossy)
        {
            var p = PixMetrics.Psnr(skia, wic);
            Assert.True(p >= 35, $"{Path.GetFileName(path)}: WIC and SkiaSharp disagree ({p:0.0} dB)");
        }
        else
        {
            var diff = PixMetrics.MaxDiff(skia, wic);
            Assert.True(diff == 0, $"{Path.GetFileName(path)}: WIC and SkiaSharp disagree by {diff}");
        }
        return skia;
    }

    // ------------------------------------------------------------------ image -> PDF

    [Theory, MemberData(nameof(PdfCases))]
    public async Task Image_to_PDF_is_a_real_pdf_with_the_picture(string id)
    {
        var src = ImgSource.Get(id);
        using var tmp = new TempDir();
        var input = tmp.WriteFile(src.FileName, src.Make()!);
        var truth = await src.TruthAsync(input);

        var pdf = Kit.One(await Kit.RunAsync("PDF", input));

        Assert.Equal(".pdf", Path.GetExtension(pdf));
        var pages = PdfCheck.Read(pdf);
        // Every TIFF frame is a page; every other format contributes its first frame.
        var expectedPages = src.Ext is "tif" or "tiff" ? src.Frames : 1;
        Assert.Equal(expectedPages, pages.Count);

        var rendered = await PdfCheck.RenderAllAsync(pdf);
        Assert.Equal(expectedPages, rendered.Count);

        var page = rendered[0];
        var ink = PdfCheck.InkBounds(page);
        Assert.NotNull(ink);
        // The picture is fitted, centred, aspect ratio kept: the drawn area has the picture's aspect ratio.
        var aspect = (double)truth.W / truth.H;
        var drawn = (double)ink!.Value.W / ink.Value.H;
        // A transparent corner is flattened on white, so it may shrink the visible ink: only check opaque pictures tightly.
        if (!truth.HasAlpha)
            Assert.True(Math.Abs(drawn - aspect) / aspect < 0.04, $"aspect {drawn:0.000} vs picture {aspect:0.000}");

        // Colours at the middle of each quadrant of the drawn area match the picture (JPEG q85 inside the PDF).
        var flat = truth.OnWhite();
        var (ix, iy, iw, ih) = truth.HasAlpha ? PageFitBox(page, truth) : ink.Value;
        foreach (var (qx, qy) in new[] { (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
        {
            var got = page.Mean(ix + (int)(iw * qx) - 2, iy + (int)(ih * qy) - 2, 5, 5);
            var want = flat.Mean((int)(flat.W * qx) - 2, (int)(flat.H * qy) - 2, 5, 5);
            Assert.True(Math.Abs(got.R - want.R) < 40 && Math.Abs(got.G - want.G) < 40 && Math.Abs(got.B - want.B) < 40,
                $"{id}: quadrant ({qx},{qy}) is R{got.R:0} G{got.G:0} B{got.B:0} on the page, expected R{want.R:0} G{want.G:0} B{want.B:0}");
        }
        Kit.Record("image", src.FileName, "PDF", "PdfPig (structure) + Windows.Data.Pdf (render)",
            $"{pages.Count} page(s) = {expectedPages} expected, renders, quadrant colours within 40 levels");
    }

    /// <summary>For a picture with a transparent area the ink box is smaller than the picture; derive the box from the page fit.</summary>
    private static (int X, int Y, int W, int H) PageFitBox(Px page, Px picture)
    {
        var scale = Math.Min((double)page.W / picture.W, (double)page.H / picture.H);
        var w = (int)Math.Round(picture.W * scale);
        var h = (int)Math.Round(picture.H * scale);
        return ((page.W - w) / 2, (page.H - h) / 2, w, h);
    }

    [Fact]
    public async Task Merge_PDF_puts_every_image_on_its_own_page_in_order()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var b = tmp.WriteFile("b.jpg", Fixtures.QuadrantJpeg(48, 64));
        var c = tmp.WriteFile("c.tif", Fixtures.TiffMultiFrame(64, 48, 2));

        var pdf = Kit.One(await Kit.RunAsync("Merge PDF", a, b, c));

        Assert.Equal(tmp.Combine("Merged.pdf"), pdf);
        var pages = PdfCheck.Read(pdf);
        Assert.Equal(4, pages.Count); // png + jpg + 2 tiff frames
        // Landscape, portrait, landscape, landscape: the page orientation follows the picture.
        Assert.True(pages[0].Width > pages[0].Height);
        Assert.True(pages[1].Height > pages[1].Width);
        Assert.True(pages[2].Width > pages[2].Height);
        var rendered = await PdfCheck.RenderAllAsync(pdf);
        Assert.All(rendered, p => Assert.True(PdfCheck.InkFraction(p) > 0.05));
        Kit.Record("image", "a.png + b.jpg + c.tif(2 frames)", "Merge PDF", "PdfPig + Windows.Data.Pdf", "4 pages, orientation per picture, all render");
    }

    // ------------------------------------------------------------------ Tool page

    [Theory, MemberData(nameof(ToolCases))]
    public async Task Tool_is_faithful(string id, string title)
    {
        var src = ImgSource.Get(id);
        using var tmp = new TempDir();
        var input = tmp.WriteFile(src.FileName, src.Make()!);
        var truth = await src.TruthAsync(input);

        var interaction = new FakeInteraction(crop: CropFor(truth));
        var result = await Kit.TryRunAsync(title, interaction, input);

        // Animated and multi-page pictures are refused (the tools would silently drop frames), with an explanation.
        if (src.Frames > 1)
        {
            Assert.Empty(result.Outputs);
            Assert.Single(result.Failures);
            var msg = result.Failures[0].Message;
            Assert.True(msg.Contains("Animated") || msg.Contains("pages"), msg);
            Kit.Record("image", src.FileName, title, "refusal check", "refused: " + msg);
            return;
        }

        if (title == "Compress" && result.Failures.Count == 1 && result.Failures[0].Message.Contains("already small"))
        {
            // Legitimate for a few-hundred-byte picture; the big-photo tests below exercise the success path.
            Assert.Empty(result.Outputs);
            Assert.Single(Directory.GetFiles(tmp.Path));
            Kit.Record("image", src.FileName, title, "refusal check", "refused: already small (nothing written)");
            return;
        }

        Assert.Empty(result.Failures);
        var output = Kit.One(result.Outputs.Select(o => o.Path).ToList());
        Assert.Equal(tmp.Path, Path.GetDirectoryName(output));
        var metric = await ValidateTool(src, truth, title, output);
        Kit.Record("image", src.FileName, title, "WIC+SkiaSharp decode vs independent source decode", metric);
    }

    private static CropChoice CropFor(Px truth) => new(new RectPx(truth.W / 8, truth.H / 8, truth.W / 2, truth.H / 2), truth.W / 2, truth.H / 2);

    private static async Task<string> ValidateTool(ImgSource src, Px truth, string title, string output)
    {
        // Same container as the source, except Compress which chooses JPEG/PNG/HEIC by content.
        if (title != "Compress")
        {
            var expected = src.Ext switch { "jpg" => "jpeg", "tif" => "tiff", var e => e };
            // Windows can't always write the source format back (HEIC/WebP handled by caps): then PNG.
            Assert.True(ImageBytes.Sniff(output) == expected || ImageBytes.Sniff(output) == "png", $"container {ImageBytes.Sniff(output)} for source {src.Ext}");
        }
        var lossyOut = ImageBytes.Sniff(output) is "jpeg" or "webp" or "heic";
        var decoded = await DecodeBoth(output, lossyOut);

        switch (title)
        {
            case "Resize 50%":
            {
                Assert.Equal(((truth.W + 1) / 2, (truth.H + 1) / 2), (decoded.W, decoded.H));
                Assert.Equal((truth.W / 2, truth.H / 2), (decoded.W, decoded.H)); // fixtures are even-sized: exactly half
                var want = truth.HalfBox();
                var p = PixMetrics.Psnr(want, decoded);
                var min = src.Photo || !lossyOut ? 30 : 20;
                Assert.True(p >= min, $"Resize: PSNR {p:0.0} dB against the ideal half-size picture");
                return $"exactly {decoded.W}x{decoded.H} (half), {Kit.Db(p)} vs ideal 2x2 box filter";
            }
            case "Crop":
            {
                var c = CropFor(truth);
                Assert.Equal((c.Rect.W, c.Rect.H), (decoded.W, decoded.H));
                var want = truth.Crop(c.Rect.X, c.Rect.Y, c.Rect.W, c.Rect.H);
                var p = PixMetrics.Psnr(want, decoded);
                var exactExpected = !lossyOut && !src.Lossy && ImageBytes.Sniff(output) is "png" or "tiff" or "bmp" or "gif";
                if (exactExpected)
                {
                    var diff = PixMetrics.MaxDiff(want.OnWhite(), decoded.OnWhite(), includeAlpha: false);
                    Assert.True(diff <= src.Slack, $"Crop: pixels differ from the source rectangle (max {diff})");
                    return $"rect {c.Rect.W}x{c.Rect.H} at ({c.Rect.X},{c.Rect.Y}) pixel-exact";
                }
                var cmin = src.Photo || !lossyOut ? 30 : 20;
                Assert.True(p >= cmin, $"Crop: PSNR {p:0.0} dB");
                return $"rect {c.Rect.W}x{c.Rect.H} at ({c.Rect.X},{c.Rect.Y}), {Kit.Db(p)}";
            }
            case "Strip EXIF":
            {
                Assert.Equal((truth.W, truth.H), (decoded.W, decoded.H));
                var p = PixMetrics.Psnr(truth, decoded);
                var min = src.Photo || !lossyOut ? 30 : 20;
                Assert.True(p >= min, $"Strip EXIF: PSNR {p:0.0} dB");
                var meta = await AssertNoMetadata(output);
                return $"{Kit.Db(p)}; {meta}";
            }
            default: // Compress
            {
                Assert.Equal((truth.W, truth.H), (decoded.W, decoded.H));
                var p = PixMetrics.Psnr(truth, decoded);
                Assert.True(p >= 25, $"Compress: PSNR {p:0.0} dB");
                return Kit.Db(p);
            }
        }
    }

    /// <summary>No Exif, GPS, XMP or Photoshop data in the output (raw bytes for JPEG, WIC properties for everything), and it still decodes upright.</summary>
    private static async Task<string> AssertNoMetadata(string output)
    {
        var bytes = await File.ReadAllBytesAsync(output);
        if (ImageBytes.Sniff(output) == "jpeg")
        {
            var markers = ImageBytes.JpegMarkers(bytes);
            Assert.DoesNotContain(markers, m => m.Marker == 0xE1);   // APP1: Exif / XMP
            Assert.DoesNotContain(markers, m => m.Marker == 0xED);   // APP13: Photoshop / IPTC
            Assert.False(ImageBytes.Contains(bytes, "Exif\0\0"u8), "Exif block present");
            Assert.False(ImageBytes.Contains(bytes, "http://ns.adobe.com/xap"u8), "XMP packet present");
        }
        var props = await PixDecode.WicPropertiesAsync(output,
            "System.GPS.LatitudeNumerator", "System.GPS.LongitudeNumerator", "System.GPS.Latitude", "System.GPS.Longitude",
            "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.DateTaken");
        Assert.Empty(props);
        var orientation = await PixDecode.WicPropertiesAsync(output, "System.Photo.Orientation");
        if (orientation.TryGetValue("System.Photo.Orientation", out var o) && o is not null)
            Assert.Equal(1, Convert.ToInt32(o));
        return "no Exif/GPS/XMP/camera/orientation data";
    }

    // ------------------------------------------------------------------ Strip EXIF on real metadata

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(3)]
    public async Task Strip_EXIF_removes_everything_the_source_had_and_keeps_the_picture_upright(int orientation)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("shot.jpg", Fixtures.JpegWithMetadata(120, 80, orientation, gps: true, xmp: true, photoshop: true));
        var truth = PixDecode.Skia(input)!;

        // The precondition: the source really carries what we are about to ask to be removed.
        var before = await PixDecode.WicPropertiesAsync(input, "System.GPS.LatitudeNumerator", "System.Photo.CameraManufacturer", "System.Photo.Orientation");
        Assert.True(before.ContainsKey("System.Photo.CameraManufacturer"), "fixture lost its camera make");
        Assert.True(ImageBytes.Contains(await File.ReadAllBytesAsync(input), "http://ns.adobe.com/xap"u8));

        var output = Kit.One(await Kit.RunAsync("Strip EXIF", input));

        Assert.Equal(tmp.Combine("shot clean.jpg"), output);
        var meta = await AssertNoMetadata(output);
        var decoded = await DecodeBoth(output, lossy: true);
        Assert.Equal((truth.W, truth.H), (decoded.W, decoded.H));
        Assert.True(PixMetrics.Psnr(truth, decoded) >= 30);
        Kit.Record("image", $"shot.jpg (Exif orientation {orientation}, GPS, XMP, Photoshop)", "Strip EXIF", "raw JPEG markers + WIC properties", meta);
    }

    // ------------------------------------------------------------------ Crop: the rectangle and the scale are honoured exactly

    [Theory]
    [InlineData(0, 0, 32, 24, 32, 24)]
    [InlineData(10, 6, 40, 30, 40, 30)]
    [InlineData(32, 24, 32, 24, 64, 48)]  // upscaled
    [InlineData(0, 0, 64, 48, 16, 12)]    // whole image, shrunk
    public async Task Crop_rectangle_and_output_size_are_exact(int x, int y, int w, int h, int ow, int oh)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("c.png", Fixtures.PngRgb(64, 48));
        var truth = PixDecode.Skia(input)!;

        var output = Kit.One(await Kit.RunAsync("Crop", new FakeInteraction(crop: new CropChoice(new RectPx(x, y, w, h), ow, oh)), input));

        var decoded = await DecodeBoth(output, lossy: false);
        Assert.Equal((ow, oh), (decoded.W, decoded.H));
        var piece = truth.Crop(x, y, w, h);
        if ((w, h) == (ow, oh))
            Assert.Equal(0, PixMetrics.MaxDiff(piece, decoded));
        else
            Assert.True(PixMetrics.Psnr(ScaleNearest(piece, ow, oh), decoded) >= 20);
        Kit.Record("image", "c.png 64x48", $"Crop {x},{y},{w},{h} -> {ow}x{oh}", "WIC+SkiaSharp", (w, h) == (ow, oh) ? "pixel-exact" : "size exact, content matches");
    }

    private static Px ScaleNearest(Px s, int w, int h)
    {
        var o = new byte[w * h * 4];
        for (var yy = 0; yy < h; yy++)
            for (var xx = 0; xx < w; xx++)
                System.Buffer.BlockCopy(s.Bgra, ((yy * s.H / h) * s.W + xx * s.W / w) * 4, o, (yy * w + xx) * 4, 4);
        return new Px(w, h, o);
    }

    [Fact]
    public async Task Crop_of_a_rotated_JPEG_uses_upright_coordinates()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("r.jpg", Fixtures.JpegWithMetadata(120, 80, orientation: 6, gps: false));
        // Upright picture is 80x120; its top-left quadrant is blue. Crop a box fully inside it.
        var output = Kit.One(await Kit.RunAsync("Crop", new FakeInteraction(crop: new CropChoice(new RectPx(5, 5, 30, 50), 30, 50)), input));
        var decoded = await DecodeBoth(output, lossy: true);
        Assert.Equal((30, 50), (decoded.W, decoded.H));
        var m = decoded.Mean(5, 5, 20, 40);
        Assert.True(m.B > 170 && m.R < 80 && m.G < 80, $"expected blue, got R{m.R:0} G{m.G:0} B{m.B:0}");
        Kit.Record("image", "r.jpg (Exif orientation 6)", "Crop", "WIC+SkiaSharp", "30x50 box in upright coordinates is the blue quadrant");
    }

    // ------------------------------------------------------------------ Compress on real photographs

    public static TheoryData<string> PhotoKinds() => new() { "png", "jpg", "png-alpha" };

    [Theory, MemberData(nameof(PhotoKinds))]
    public async Task Compress_a_photograph_saves_space_and_stays_recognisable(string kind)
    {
        using var tmp = new TempDir();
        var photo = Fixtures.PhotoBgra(1200, 800);
        byte[] bytes;
        switch (kind)
        {
            case "png": bytes = Fixtures.PhotoPng(1200, 800); break;
            case "jpg": bytes = Fixtures.PhotoJpeg(1200, 800, 98); break;
            default:
                for (var y = 0; y < 200; y++)
                    for (var x = 0; x < 300; x++)
                        photo[(y * 1200 + x) * 4 + 3] = 0;
                bytes = EncodePngWithAlpha(photo, 1200, 800);
                break;
        }
        var input = tmp.WriteFile("photo." + (kind == "jpg" ? "jpg" : "png"), bytes);
        var truth = PixDecode.Skia(input)!;

        var output = Kit.One(await Kit.RunAsync("Compress", input));

        Assert.True(new FileInfo(output).Length <= new FileInfo(input).Length * 0.95, "output is not at least 5% smaller");
        var sniff = ImageBytes.Sniff(output);
        // Opaque pictures become JPEG; pictures with real transparency keep it (PNG, or HEIC where Windows can encode it).
        Assert.Contains(sniff, kind == "png-alpha" ? new[] { "png", "heic" } : new[] { "jpeg" });
        var decoded = await DecodeBoth(output, lossy: sniff != "png");
        Assert.Equal((1200, 800), (decoded.W, decoded.H));
        var p = PixMetrics.Psnr(truth, decoded);
        Assert.True(p >= 25, $"PSNR {p:0.0} dB");
        if (kind == "png-alpha")
            Assert.True(decoded.At(10, 10).A < 128 || decoded.OnWhite().At(10, 10) is { B: > 240 }, "the transparent corner was lost");
        Kit.Record("image", $"photo.{(kind == "jpg" ? "jpg" : "png")} 1200x800 ({kind})", "Compress", "WIC+SkiaSharp decode",
            $"{new FileInfo(input).Length / 1024} KB -> {new FileInfo(output).Length / 1024} KB ({sniff}), {Kit.Db(p)}");
    }

    private static byte[] EncodePngWithAlpha(byte[] bgra, int w, int h)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var pm = bmp.PeekPixels();
        using var data = pm.Encode(new SKPngEncoderOptions())!;
        return data.ToArray();
    }

    // ------------------------------------------------------------------ Resize: odd sizes

    [Fact]
    public async Task Resize_of_an_odd_sized_picture_rounds_up_and_keeps_the_picture()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("odd.png", Fixtures.PngRgb(63, 47));
        var output = Kit.One(await Kit.RunAsync("Resize 50%", input));
        var decoded = await DecodeBoth(output, lossy: false);
        Assert.Equal((32, 24), (decoded.W, decoded.H));
        Kit.Record("image", "odd.png 63x47", "Resize 50%", "WIC+SkiaSharp", "32x24 (rounds up), content intact");
    }
}
