using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MikeShift.Core.Actions;
using MikeShift.Core.Documents;
using MikeShift.Core.Feedback;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MikeShift.Core.Images;

/// <summary>A recognised word and where it is, in pixels of the picture that was read (top-left origin).</summary>
internal readonly record struct RecognizedWord(string Text, double X, double Y, double Width, double Height);

/// <summary>One recognised line: the text written for it and its words in reading order.</summary>
internal sealed record RecognizedLine(string Text, IReadOnlyList<RecognizedWord> Words);

/// <summary>
/// What text recognition found in one picture or PDF page. The word boxes are in the pixels of the picture that was passed in (not of the
/// smaller copy the engine may have read), so a caller that rendered a PDF page can map them back onto the page.
/// </summary>
internal sealed record RecognizedText(int Width, int Height, IReadOnlyList<RecognizedLine> Lines)
{
    /// <summary>One line of output per recognised line, top to bottom; empty when nothing was found.</summary>
    public string PlainText => string.Join(Environment.NewLine, Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
}

/// <summary>
/// Text recognition (OCR) through <c>Windows.Media.Ocr</c>: built into Windows, offline, no extra package. It reads the languages Windows has an
/// OCR pack for (they come with the language packs of the user's profile languages); on a PC with none, <see cref="IsAvailable"/> is false and
/// the catalog hides the tiles that need it (<see cref="Capabilities.Ocr"/>).
/// </summary>
internal static class TextRecognition
{
    /// <summary>True if Windows can create a recognizer for one of the user's profile languages. Never throws.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages() is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the text in <paramref name="pixels"/>. Transparent areas are read as white (dark text on a transparent screenshot would otherwise
    /// vanish into black), and a picture larger than <see cref="OcrEngine.MaxImageDimension"/> is scaled down first. Throws
    /// <see cref="ConversionException"/> when no recognizer exists or the engine fails; an empty result means there was no text.
    /// </summary>
    public static async Task<RecognizedText> RecognizeAsync(Raster pixels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new ConversionException(OcrCopy.NoLanguage);

        var input = FitWithin(pixels.FlattenOnWhite(), (int)OcrEngine.MaxImageDimension);
        var scale = (double)pixels.Width / input.Width;

        OcrResult result;
        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                input.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, input.Width, input.Height, BitmapAlphaMode.Premultiplied);
            result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            throw new ConversionException(OcrCopy.Unreadable, ex);
        }

        var lines = result.Lines.Select(l => ToLine(l, scale)).Where(l => l.Words.Count > 0).ToList();
        return new RecognizedText(pixels.Width, pixels.Height, lines);
    }

    private static Raster FitWithin(Raster pixels, int maxSide)
    {
        var longSide = Math.Max(pixels.Width, pixels.Height);
        if (longSide <= maxSide) return pixels;
        var s = (double)maxSide / longSide;
        var w = Math.Clamp((int)Math.Floor(pixels.Width * s), 1, maxSide);
        var h = Math.Clamp((int)Math.Floor(pixels.Height * s), 1, maxSide);
        return pixels.CropScale(new RectPx(0, 0, pixels.Width, pixels.Height), w, h);
    }

    private static RecognizedLine ToLine(OcrLine line, double scale)
    {
        var words = line.Words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => new RecognizedWord(w.Text,
                w.BoundingRect.X * scale, w.BoundingRect.Y * scale, w.BoundingRect.Width * scale, w.BoundingRect.Height * scale))
            .ToList();
        if (!words.Any(w => w.Text.Any(TextUtil.IsRtlChar))) return new RecognizedLine(line.Text, words);

        var ordered = RightToLeftReadingOrder(words);
        return new RecognizedLine(string.Join(' ', ordered.Select(w => w.Text)), ordered);
    }

    /// <summary>
    /// The words of a right-to-left line (Hebrew, Arabic) in reading order, decided from where they are rather than from the engine's order:
    /// right to left, except that a run of left-to-right words (a Latin name, a number) keeps its own left-to-right order, the same rule
    /// <see cref="Bidi"/> applies to characters.
    /// </summary>
    internal static List<RecognizedWord> RightToLeftReadingOrder(IReadOnlyList<RecognizedWord> words)
    {
        var byRightEdge = words.OrderByDescending(w => w.X + w.Width).ToList();
        var ordered = new List<RecognizedWord>(byRightEdge.Count);
        for (var i = 0; i < byRightEdge.Count;)
        {
            if (byRightEdge[i].Text.Any(TextUtil.IsRtlChar))
            {
                ordered.Add(byRightEdge[i++]);
                continue;
            }
            var j = i;
            while (j < byRightEdge.Count && !byRightEdge[j].Text.Any(TextUtil.IsRtlChar)) j++;
            for (var k = j - 1; k >= i; k--) ordered.Add(byRightEdge[k]);
            i = j;
        }
        return ordered;
    }
}
