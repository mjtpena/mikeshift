using System.Text;
using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using SkiaSharp;

namespace MikeShift.Core.Tests.Images;

/// <summary>
/// Read QR code (a picture to <c>name qr.txt</c>) and QR code (a <c>.url</c> or short <c>.txt</c> to <c>name qr.png</c>). Codes are generated here
/// and read back by the same library, so the round trip is what is tested, not a reader against a particular phone.
/// </summary>
public class QrCodeTests
{
    private const string Link = "https://example.com/menu?table=12&lang=en";

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    private static string Shortcut(string url) => $"[InternetShortcut]\r\nURL={url}\r\nIconIndex=0\r\n";

    private static string[] Lines(string path) => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

    // ------------------------------------------------------------------ the round trip

    [Fact]
    public async Task A_generated_code_reads_back_as_the_same_text()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("note.txt", Link);

        var png = await QrCodes.WriteAsync(txt, default);
        var (read, first) = await QrCodes.ReadAsync(png, default);

        Assert.Equal(tmp.Combine("note qr.png"), png);
        Assert.Equal(tmp.Combine("note qr qr.txt"), read);
        Assert.Equal(Link, File.ReadAllText(read));
        Assert.Equal(Link, first);
    }

    [Fact]
    public async Task The_QR_code_and_Read_QR_code_actions_round_trip_a_shortcut()
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", Shortcut(Link));
        using var guard = OriginalsGuard.Watch(url);

        var made = await LinkActions.All.Single().Run([url], Context(), default);
        var png = Assert.Single(made.Outputs).Path;
        var read = await ActionCatalog.Find(ImageActions.All, "Read QR code")!.Run([png], Context(), default);

        Assert.True(made.Succeeded);
        Assert.Equal(tmp.Combine("site qr.png"), png);
        var output = Assert.Single(read.Outputs);
        Assert.Equal(Link, File.ReadAllText(output.Path));
        Assert.Equal(Link, output.Preview);
        guard.AssertUnchanged();
    }

    [Theory]
    [InlineData("Zürich – 東京 ☕")]
    [InlineData("Wi-Fi: office\nPassword: correct horse")]
    [InlineData("a")]
    public async Task Text_beyond_ASCII_and_with_line_breaks_survives(string text)
    {
        var png = await QrCodes.EncodePngAsync(text, default);

        var img = await DecodeAsync(png);

        Assert.Equal([text], QrCodes.Decode(img, default));
    }

    // ------------------------------------------------------------------ what is encoded

    [Fact]
    public async Task A_url_file_encodes_its_URL_line_and_not_the_whole_file()
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", Shortcut(Link));

        var png = await LinkActions.All.Single().Run([url], Context(), default);

        var text = QrCodes.Decode(await DecodeAsync(File.ReadAllBytes(Assert.Single(png.Outputs).Path)), default);
        Assert.Equal([Link], text);
    }

    [Theory]
    [InlineData("[InternetShortcut]\r\nurl = x\r\n")]
    [InlineData("[InternetShortcut]\r\nIconIndex=0\r\n")]
    [InlineData("[InternetShortcut]\r\nURL=\r\n")]
    [InlineData("")]
    public async Task A_shortcut_without_an_address_is_refused_and_writes_nothing(string content)
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", content);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(url, default));

        Assert.Equal(QrCopy.NoAddress, ex.Message);
        Assert.Equal(["site.url"], FileNames(tmp));
    }

    [Fact]
    public async Task The_URL_key_is_found_whatever_its_case_and_the_first_one_wins()
    {
        using var tmp = new TempDir();
        var url = tmp.WriteFile("site.url", "[InternetShortcut]\nurl=https://first.example/\nURL=https://second.example/\n");

        var png = await QrCodes.WriteAsync(url, default);

        Assert.Equal(["https://first.example/"], QrCodes.Decode(await DecodeAsync(File.ReadAllBytes(png)), default));
    }

    [Theory]
    [MemberData(nameof(TextFiles))]
    public async Task Text_files_in_the_usual_encodings_are_read(byte[] bytes)
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("note.txt", bytes);

        var png = await QrCodes.WriteAsync(txt, default);

        Assert.Equal(["Caf\u00E9 menu"], QrCodes.Decode(await DecodeAsync(File.ReadAllBytes(png)), default));
    }

    public static TheoryData<byte[]> TextFiles() =>
    [
        Encoding.UTF8.GetBytes("Caf\u00E9 menu\r\n"),
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("Caf\u00E9 menu")],
        [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Caf\u00E9 menu\r\n")],
        [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("Caf\u00E9 menu")],
    ];

    [Fact]
    public async Task An_empty_text_file_is_refused()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("note.txt", "  \r\n");

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(txt, default));

        Assert.Equal(QrCopy.NothingToEncode, ex.Message);
        Assert.Equal(["note.txt"], FileNames(tmp));
    }

    [Fact]
    public async Task A_file_of_NUL_characters_is_not_text()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("blank.txt", new byte[128]);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(txt, default));

        Assert.Equal(QrCopy.NotText, ex.Message);
        Assert.Equal(["blank.txt"], FileNames(tmp));
    }

    [Fact]
    public async Task A_file_that_is_not_text_is_refused()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("note.txt", new byte[] { 0x80, 0x81, 0xFE, 0x41, 0xC3 });

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(txt, default));

        Assert.Equal(QrCopy.NotText, ex.Message);
        Assert.Equal(["note.txt"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ the picture

    [Theory]
    [InlineData(Link)]
    [InlineData("1")]
    [InlineData("A much longer piece of text to push the code to a bigger version than a short link would need, with a few more words so the grid has many modules per side.")]
    public async Task The_picture_is_black_on_white_square_with_a_quiet_zone_and_at_least_1024_pixels(string text)
    {
        var png = await QrCodes.EncodePngAsync(text, default);

        var raster = await DecodeAsync(png);

        Assert.Equal(raster.Width, raster.Height);
        Assert.True(raster.Width >= QrCodes.MinimumPixels, $"{raster.Width} px");
        var bgra = raster.Bgra;
        Assert.All(Enumerable.Range(0, bgra.Length / 4), i =>
        {
            var v = bgra[i * 4];
            Assert.True(v is 0 or 255 && bgra[i * 4 + 1] == v && bgra[i * 4 + 2] == v && bgra[i * 4 + 3] == 255, "only black and white");
        });
        // Four modules of white all round: the first and last four module-rows and columns hold no black pixel.
        var margin = 4 * ModuleSize(raster);
        for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
                if (y < margin || y >= raster.Height - margin || x < margin || x >= raster.Width - margin)
                    Assert.Equal(255, bgra[(y * raster.Width + x) * 4]);
    }

    /// <summary>The width of one module in pixels: the first black run on the first row that has any is the top of a 7-module finder pattern.</summary>
    private static int ModuleSize(Raster r)
    {
        for (var y = 0; y < r.Height; y++)
        {
            var run = 0;
            for (var x = 0; x < r.Width; x++)
            {
                if (r.Bgra[(y * r.Width + x) * 4] == 0) run++;
                else if (run > 0) return run / 7;
            }
        }
        throw new InvalidOperationException("no black pixel");
    }

    [Fact]
    public async Task The_error_correction_is_M()
    {
        // The most the biggest code holds at level M is 2,331 bytes; level L would take 2,953, which is not used.
        var png = await QrCodes.EncodePngAsync(new string('a', 2331), default);
        Assert.Equal([new string('a', 2331)], QrCodes.Decode(await DecodeAsync(png), default));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.EncodePngAsync(new string('a', 2332), default));
        Assert.Equal(QrCopy.TooLong(2332), ex.Message);
    }

    // ------------------------------------------------------------------ capacity

    [Fact]
    public async Task Text_over_capacity_is_refused_before_anything_is_written()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("long.txt", new string('x', 3000));
        using var guard = OriginalsGuard.Watch(txt);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(txt, default));

        Assert.Equal(QrCopy.TooLong(3000), ex.Message);
        Assert.Equal(["long.txt"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task A_huge_file_is_refused_without_being_read()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("huge.txt", new byte[200_000]);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.WriteAsync(txt, default));

        Assert.Equal(QrCopy.TooLong(200_000), ex.Message);
        Assert.Equal(["huge.txt"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task Several_codes_are_one_line_each_top_to_bottom()
    {
        using var tmp = new TempDir();
        var first = await QrCodes.EncodePngAsync("https://example.com/one", default);
        var second = await QrCodes.EncodePngAsync("https://example.com/two", default);
        var poster = tmp.WriteFile("poster.png", Poster(second, first)); // drawn in the "wrong" order: position decides, not the order drawn

        var (output, firstLine) = await QrCodes.ReadAsync(poster, default);

        Assert.Equal(tmp.Combine("poster qr.txt"), output);
        Assert.Equal(["https://example.com/one", "https://example.com/two"], Lines(output));
        Assert.Equal("https://example.com/one", firstLine);
    }

    [Fact]
    public async Task Reading_order_follows_the_picture_even_when_it_is_turned_around()
    {
        var one = await QrCodes.EncodePngAsync("https://example.com/one", default);
        var two = await QrCodes.EncodePngAsync("https://example.com/two", default);
        var raster = await DecodeAsync(Poster(two, one));
        var turned = new byte[raster.Bgra.Length];
        var pixels = raster.Width * raster.Height;
        for (var i = 0; i < pixels; i++)
            Array.Copy(raster.Bgra, i * 4, turned, (pixels - 1 - i) * 4, 4);

        // Upside down, "two" is now the upper code.
        Assert.Equal(["https://example.com/two", "https://example.com/one"], QrCodes.Decode(new Raster(raster.Width, raster.Height, turned), default));
    }

    [Fact]
    public async Task Two_separate_codes_with_the_same_text_are_two_lines()
    {
        using var tmp = new TempDir();
        var code = await QrCodes.EncodePngAsync(Link, default);
        var poster = tmp.WriteFile("twice.png", Poster(code, code));

        var (output, _) = await QrCodes.ReadAsync(poster, default);

        Assert.Equal([Link, Link], Lines(output));
    }

    [Fact]
    public void The_same_code_reported_twice_in_one_place_counts_once_but_a_code_elsewhere_does_not()
    {
        var here = new QrCodes.Found("x", new QrCodes.Box(100, 100, 200, 200));
        Assert.True(QrCodes.SameCode(here, new QrCodes.Found("x", new QrCodes.Box(105, 98, 205, 203))));
        Assert.True(QrCodes.SameCode(here, new QrCodes.Found("x", new QrCodes.Box(120, 120, 180, 180))));
        Assert.False(QrCodes.SameCode(here, new QrCodes.Found("x", new QrCodes.Box(400, 100, 500, 200))));
        Assert.False(QrCodes.SameCode(here, new QrCodes.Found("y", new QrCodes.Box(105, 98, 205, 203))));
        Assert.False(QrCodes.SameCode(here, new QrCodes.Found("x", null)));
    }

    [Fact]
    public async Task A_small_code_in_a_large_screenshot_is_found()
    {
        using var tmp = new TempDir();
        var shot = tmp.WriteFile("screenshot.png", Poster(await QrCodes.EncodePngAsync(Link, default), codeSize: 260, width: 1600, height: 900));

        var (output, _) = await QrCodes.ReadAsync(shot, default);

        Assert.Equal(Link, File.ReadAllText(output));
    }

    [Fact]
    public async Task A_light_on_dark_code_is_read()
    {
        var raster = await DecodeAsync(await QrCodes.EncodePngAsync(Link, default));
        var inverted = raster.Bgra.ToArray();
        for (var i = 0; i < inverted.Length; i += 4)
            (inverted[i], inverted[i + 1], inverted[i + 2]) = ((byte)(255 - inverted[i]), (byte)(255 - inverted[i + 1]), (byte)(255 - inverted[i + 2]));

        Assert.Equal([Link], QrCodes.Decode(new Raster(raster.Width, raster.Height, inverted), default));
    }

    [Fact]
    public async Task A_code_turned_a_quarter_is_read()
    {
        var raster = await DecodeAsync(await QrCodes.EncodePngAsync(Link, default));
        var n = raster.Width;
        var turned = new byte[raster.Bgra.Length];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                Array.Copy(raster.Bgra, (y * n + x) * 4, turned, (x * n + (n - 1 - y)) * 4, 4);

        Assert.Equal([Link], QrCodes.Decode(new Raster(n, n, turned), default));
    }

    [Fact]
    public async Task A_picture_without_a_code_fails_with_a_message_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("colours.png", Fixtures.PngRgb(400, 300));
        using var guard = OriginalsGuard.Watch(png);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => QrCodes.ReadAsync(png, default));

        Assert.Equal(QrCopy.NoCodeInPicture, ex.Message);
        Assert.Equal(["colours.png"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Reading_a_cancelled_job_writes_nothing()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("code.png", await QrCodes.EncodePngAsync(Link, default));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QrCodes.ReadAsync(png, cts.Token));

        Assert.Equal(["code.png"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ batches and the catalog

    [Fact]
    public async Task A_batch_keeps_the_outputs_already_written_when_one_picture_has_no_code()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("a.png", await QrCodes.EncodePngAsync(Link, default));
        var bad = tmp.WriteFile("b.png", Fixtures.PngRgb(200, 200));
        var good2 = tmp.WriteFile("c.png", await QrCodes.EncodePngAsync("second", default));

        var result = await ActionCatalog.Find(ImageActions.All, "Read QR code")!.Run([good, bad, good2], Context(), default);

        Assert.Equal(2, result.Outputs.Count);
        Assert.Equal(bad, Assert.Single(result.Failures).Input);
        Assert.Equal(new[] { "a qr.txt", "a.png", "b.png", "c qr.txt", "c.png" }.Order(), FileNames(tmp).Order());
    }

    [Fact]
    public void Read_QR_code_is_a_tool_for_pictures_and_QR_code_one_for_links_and_text_files()
    {
        var read = ActionCatalog.Find(ImageActions.All, "Read QR code")!;
        Assert.Equal(WheelPage.Tool, read.Page);
        Assert.False(string.IsNullOrEmpty(read.Glyph));

        var make = ActionCatalog.Find(LinkActions.All, "QR code")!;
        Assert.Equal(WheelPage.Tool, make.Page);
        Assert.Same(make.Glyph, ActionCatalog.Find(DocumentActions.All, "QR code")!.Glyph);

        var forText = ActionCatalog.Find(DocumentActions.All, "QR code")!;
        Assert.True(forText.IsVisibleFor([@"C:\x\a.txt"], Capabilities.None));
        Assert.False(forText.IsVisibleFor([@"C:\x\a.docx"], Capabilities.None));
        Assert.False(forText.IsVisibleFor([@"C:\x\a.txt", @"C:\x\b.md"], Capabilities.None));
    }

    [Fact]
    public void The_toast_for_a_read_code_shows_what_it_says()
    {
        var result = new ActionResult
        {
            Total = 1,
            Outputs = [new ActionOutput(@"C:\x\photo.png", @"C:\x\photo qr.txt", false) { Preview = Link }],
        };

        var toast = FeedbackPlan.ToastFor(result);

        Assert.Equal(Link, toast.Title);
        Assert.Equal("Saved photo qr.txt · Next to the original", toast.Detail);
        Assert.Equal(ToastKind.Success, toast.Kind);
        Assert.Equal(@"C:\x\photo qr.txt", toast.RevealPath);
    }

    [Fact]
    public void A_long_first_line_is_cut_in_the_toast()
    {
        var result = new ActionResult { Total = 1, Outputs = [new ActionOutput("a.png", "a qr.txt", false) { Preview = new string('x', 300) }] };

        Assert.Equal(new string('x', 80) + "…", FeedbackPlan.ToastFor(result).Title);
    }

    [Fact]
    public void Several_outputs_are_counted_as_usual()
    {
        var result = new ActionResult
        {
            Total = 2,
            Outputs = [new ActionOutput("a.png", "a qr.txt", false) { Preview = "x" }, new ActionOutput("b.png", "b qr.txt", false) { Preview = "y" }],
        };

        Assert.Equal("Saved 2 files", FeedbackPlan.ToastFor(result).Title);
    }

    // ------------------------------------------------------------------ helpers

    private static ActionContext Context() => new(Capabilities.All);

    private static async Task<Raster> DecodeAsync(byte[] png)
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("code.png", png);
        return (await ImageCodec.DecodeAsync(path, ImageCodec.MaxPixels, default)).Pixels;
    }

    /// <summary>Two codes one above the other on white (the first drawn at the bottom, so that drawing order and position differ).</summary>
    private static byte[] Poster(byte[] bottom, byte[] top)
    {
        using var a = SKImage.FromEncodedData(top);
        using var b = SKImage.FromEncodedData(bottom);
        using var surface = SKSurface.Create(new SKImageInfo(a.Width + 200, a.Height + b.Height + 300, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(b, 100, a.Height + 200, SKSamplingOptions.Default);
        surface.Canvas.DrawImage(a, 100, 100, SKSamplingOptions.Default);
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>One code scaled to <paramref name="codeSize"/> pixels, in the middle of a picture.</summary>
    private static byte[] Poster(byte[] code, int codeSize, int width, int height)
    {
        using var image = SKImage.FromEncodedData(code);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(0xD0, 0xE0, 0xF0));
        var target = new SKRect((width - codeSize) / 2f, (height - codeSize) / 2f, (width + codeSize) / 2f, (height + codeSize) / 2f);
        surface.Canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
