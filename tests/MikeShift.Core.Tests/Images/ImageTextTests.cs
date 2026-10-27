using System.Text;
using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Cli;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;

namespace MikeShift.Core.Tests.Images;

/// <summary>
/// Picture to text (the Image TXT tile): pictures of known text are generated here and read back by the Windows OCR engine, so most tests
/// need a text recognition language and are skipped on a PC without one (<see cref="Capabilities.Ocr"/>).
/// </summary>
public class ImageTextTests
{
    private static ActionDef Txt => ActionCatalog.Find(ImageActions.All, "TXT")!;

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_picture_of_known_text_becomes_a_txt_with_that_text()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("photo.png", Fixtures.TextPicture(Fixtures.OcrLines));
        using var guard = OriginalsGuard.Watch(png);

        var output = await ImageText.ToTextAsync(png, default);

        Assert.Equal(tmp.Combine("photo.txt"), output);
        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "UTF-8 without a byte order mark");
        Assert.Equal(Fixtures.OcrLines, Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n'));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Dark_text_on_a_transparent_picture_is_read_as_if_on_white()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("screenshot.png", Fixtures.TextPicture([Fixtures.OcrLines[0]], fontSize: 16, transparent: true));

        var output = await ImageText.ToTextAsync(png, default);

        Assert.Equal(Fixtures.OcrLines[0], File.ReadAllText(output));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_picture_without_text_fails_with_a_message_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("colours.png", Fixtures.PngRgb(400, 300));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageText.ToTextAsync(png, default));

        Assert.Equal(OcrCopy.NoTextInPicture, ex.Message);
        Assert.Equal(["colours.png"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task Word_boxes_are_in_the_pixels_of_the_picture_that_was_read()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("photo.png", Fixtures.TextPicture(Fixtures.OcrLines));
        var img = await ImageCodec.DecodeAsync(png, ImageCodec.MaxPixels, default);

        var text = await TextRecognition.RecognizeAsync(img.Pixels, default);

        Assert.Equal((img.Width, img.Height), (text.Width, text.Height));
        var first = text.Lines[0].Words[0];
        Assert.Equal("The", first.Text);
        Assert.InRange(first.X, Fixtures.TextPictureMargin - 6, Fixtures.TextPictureMargin + 6);
        Assert.True(text.Lines[1].Words[0].Y > first.Y + first.Height, "the second line is below the first");
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_picture_larger_than_the_engine_allows_is_scaled_down_and_its_boxes_scaled_back()
    {
        using var tmp = new TempDir();
        var line = "Wide banner text";
        var png = tmp.WriteFile("banner.png", Fixtures.TextPicture([line], fontSize: 400, width: 12_000));
        var img = await ImageCodec.DecodeAsync(png, ImageCodec.MaxPixels, default);
        Assert.True(img.Width > Windows.Media.Ocr.OcrEngine.MaxImageDimension);

        var text = await TextRecognition.RecognizeAsync(img.Pixels, default);

        Assert.Equal(line, text.PlainText);
        var words = text.Lines.Single().Words;
        Assert.InRange(words[0].X, Fixtures.TextPictureMargin - 30, Fixtures.TextPictureMargin + 30);
        Assert.True(words[^1].X + words[^1].Width > Windows.Media.Ocr.OcrEngine.MaxImageDimension / 4.0);
    }

    [Fact]
    public async Task A_multi_page_tiff_is_refused_with_a_way_forward()
    {
        using var tmp = new TempDir();
        var tif = tmp.WriteFile("fax.tif", Fixtures.TiffMultiFrame(64, 48, frames: 3));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageText.ToTextAsync(tif, default));

        Assert.Equal(OcrCopy.MultiFrame(3, animated: false), ex.Message);
        Assert.Contains("PDF", ex.Message);
        Assert.Equal(["fax.tif"], FileNames(tmp));
    }

    [Fact]
    public async Task An_animated_gif_is_refused()
    {
        using var tmp = new TempDir();
        var gif = tmp.WriteFile("anim.gif", Fixtures.Gif(64, 48, animated: true));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ImageText.ToTextAsync(gif, default));

        Assert.Equal(OcrCopy.MultiFrame(2, animated: true), ex.Message);
    }

    [Fact]
    public async Task A_cancelled_job_writes_nothing()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("photo.png", Fixtures.TextPicture(Fixtures.OcrLines));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageText.ToTextAsync(png, cts.Token));

        Assert.Equal(["photo.png"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task A_batch_keeps_the_text_it_read_when_one_picture_has_none()
    {
        using var tmp = new TempDir();
        var text = tmp.WriteFile("text.png", Fixtures.TextPicture(Fixtures.OcrLines));
        var blank = tmp.WriteFile("blank.png", Fixtures.PngRgb(400, 300));
        using var guard = OriginalsGuard.Watch(text, blank);

        var result = await Txt.Run([text, blank], new ActionContext(Capabilities.All), default);

        Assert.True(result.PartialFailure);
        Assert.Equal(tmp.Combine("text.txt"), Assert.Single(result.Outputs).Path);
        Assert.Equal(OcrCopy.NoTextInPicture, Assert.Single(result.Failures).Message);
        Assert.Equal(["blank.png", "text.png", "text.txt"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [SkipIfNoCapability(nameof(Capabilities.Ocr))]
    public async Task The_cli_runs_TXT_on_a_picture()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("scan.png", Fixtures.TextPicture(Fixtures.OcrLines));
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliRunner.RunAsync(["--run", "TXT", png], stdout, stderr, Capabilities.Current);

        Assert.Equal(CliRunner.ExitOk, exit);
        Assert.Equal(tmp.Combine("scan.txt"), stdout.ToString().Trim());
        Assert.Contains(Fixtures.OcrLines[1], File.ReadAllText(tmp.Combine("scan.txt")));
    }

    // ---------------------------------------------------------------- the tile and the capability

    [Fact]
    public void The_tile_is_on_the_format_page_after_PDF()
    {
        var formats = ImageActions.All.Where(a => a.Page == WheelPage.Format).Select(a => a.Title).ToArray();
        Assert.Equal("TXT", formats[^1]);
        Assert.Equal("PDF", formats[^2]);
    }

    [Fact]
    public void The_wheel_for_a_picture_shows_TXT_on_its_formats_page()
    {
        var wheel = new MikeShift.Core.Interaction.WheelModel();
        wheel.SetActions(ActionCatalog.For([@"C:\x\a.png"], Capabilities.All, _ => Category.Image));
        Assert.Contains("TXT", wheel.Formats.Select(a => a.Title));
    }

    [Fact]
    public void The_right_click_menu_keeps_its_five_picture_lines_and_ranks_TXT_after_the_formats()
    {
        var plan = MikeShift.Core.Shell.ContextMenuPlanner.Build([@"C:\x\a.png"], Capabilities.All, _ => Category.Image);
        Assert.Equal(["JPG", "PDF", "Compress", "Resize 50%", "WebP", "Zip"], plan.Entries.Select(e => e.ActionTitle));

        var ranking = MikeShift.Core.Shell.ContextMenuPlanner.Ranking[Category.Image];
        Assert.Equal(Array.IndexOf(ranking, "BMP") + 1, Array.IndexOf(ranking, "TXT"));
    }

    [Fact]
    public void Without_a_text_recognition_language_the_tile_is_hidden()
    {
        var png = new[] { @"C:\x\a.png" };
        Assert.DoesNotContain(Txt, ActionCatalog.For(png, Capabilities.All with { Ocr = false }, _ => Category.Image));
        Assert.Contains(Txt, ActionCatalog.For(png, Capabilities.None with { Ocr = true }, _ => Category.Image));
    }

    [Fact]
    public async Task Without_a_text_recognition_language_list_does_not_show_TXT_for_a_picture()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb());

        async Task<string[]> List(Capabilities caps)
        {
            var stdout = new StringWriter();
            Assert.Equal(CliRunner.ExitOk, await CliRunner.RunAsync(["--list", png], stdout, new StringWriter(), caps));
            return stdout.ToString().Trim().Split(", ");
        }

        Assert.DoesNotContain("TXT", await List(Capabilities.All with { Ocr = false }));
        Assert.Contains("TXT", await List(Capabilities.All));
    }

    [Fact]
    public async Task Without_a_text_recognition_language_run_TXT_on_a_picture_says_it_is_not_available()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb());
        var stderr = new StringWriter();

        var exit = await CliRunner.RunAsync(["--run", "TXT", png], new StringWriter(), stderr, Capabilities.All with { Ocr = false });

        Assert.Equal(CliRunner.ExitError, exit);
        Assert.Contains("error:", stderr.ToString());
        Assert.Equal(["a.png"], FileNames(tmp));
    }

    // ---------------------------------------------------------------- right-to-left lines

    private static RecognizedWord W(string text, double x) => new(text, x, 0, 50, 20);

    [Fact]
    public void A_right_to_left_line_is_read_from_the_right()
    {
        // Visual order, left to right: עולם שלום (the line reads שלום then עולם).
        var words = new[] { W("עולם", 0), W("שלום", 100) };
        Assert.Equal(["שלום", "עולם"], TextRecognition.RightToLeftReadingOrder(words).Select(w => w.Text));
    }

    [Fact]
    public void A_run_of_latin_words_in_a_right_to_left_line_keeps_its_own_order()
    {
        // Visual: [Windows 11] שלום — reading order is שלום, then "Windows 11" left to right.
        var words = new[] { W("Windows", 0), W("11", 60), W("שלום", 200) };
        Assert.Equal(["שלום", "Windows", "11"], TextRecognition.RightToLeftReadingOrder(words).Select(w => w.Text));
    }
}
