using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace MikeShift.Core.Pdf;

/// <summary>PDF text extraction (PdfPig) and the Hebrew reversed-text heuristic.</summary>
internal static class PdfText
{
    public const string ReversedMessage = "The text in this PDF is stored in reverse (Hebrew), so it can't be extracted reliably.";

    /// <summary>Extracts the text of every page in reading order (lines top to bottom, words left to right); pages are separated by a blank line.</summary>
    public static string Extract(byte[] pdf, CancellationToken ct) => Join(ExtractPages(pdf, ct));

    /// <summary>The pages' text with a blank line between pages; pages without text are left out.</summary>
    public static string Join(IEnumerable<string> pages) =>
        string.Join(Environment.NewLine + Environment.NewLine, pages.Where(p => p.Length > 0));

    /// <summary>
    /// The text of each page, in page order (the list has one entry per page): reading order as in <see cref="Extract"/>, and "" for a page
    /// with no text layer (a scanned page, or a blank one).
    /// </summary>
    public static List<string> ExtractPages(byte[] pdf, CancellationToken ct)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            var pages = new List<string>();
            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pages.Add(ExtractPage(page));
            }
            return pages;
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new ConversionException(PdfGuard.EncryptedMessage, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
                throw new ConversionException(PdfGuard.EncryptedMessage, ex);
            throw new ConversionException(PdfGuard.DamagedMessage, ex);
        }
    }

    private static string ExtractPage(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0) return "";

        // Group words into lines by vertical centre; PDF Y grows upwards, so higher Y = earlier line.
        var ordered = words.OrderByDescending(w => (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2).ToList();
        var lines = new List<List<Word>>();
        double lineCentre = 0, lineHeight = 0;
        foreach (var w in ordered)
        {
            var centre = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2;
            var height = Math.Max(1, w.BoundingBox.Height);
            if (lines.Count > 0 && Math.Abs(centre - lineCentre) <= Math.Max(lineHeight, height) * 0.5)
            {
                lines[^1].Add(w);
                continue;
            }
            lines.Add([w]);
            lineCentre = centre;
            lineHeight = height;
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendJoin(' ', line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
        }
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------ Hebrew

    private static bool IsHebrewLetter(char c) => c is >= 'א' and <= 'ת';

    private static bool IsFinalForm(char c) => c is 'ך' or 'ם' or 'ן' or 'ף' or 'ץ';

    /// <summary>
    /// Counts final-form Hebrew letters (ך ם ן ף ץ) at the end of a word versus at the start of a word (words of two or more letters).
    /// Correct Hebrew only ever has them at the end; text stored in visual order (reversed) has them at the start.
    /// </summary>
    public static (int AtWordEnd, int AtWordStart) CountFinalForms(string text)
    {
        int atEnd = 0, atStart = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (!IsHebrewLetter(text[i])) { i++; continue; }
            var start = i;
            while (i < text.Length && IsHebrewLetter(text[i])) i++;
            if (i - start < 2) continue; // single-letter words say nothing

            if (IsFinalForm(text[i - 1])) atEnd++;
            if (IsFinalForm(text[start])) atStart++;
        }
        return (atEnd, atStart);
    }

    /// <summary>True if the Hebrew in <paramref name="text"/> looks like it is stored reversed (final forms mostly at word starts).</summary>
    public static bool LooksReversedHebrew(string text)
    {
        var (end, start) = CountFinalForms(text);
        return start >= 2 && start > end;
    }
}
