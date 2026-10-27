using System.Globalization;
using MikeShift.Core.Documents;
using MikeShift.Core.Images;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace MikeShift.Core.Pdf;

/// <summary>
/// The invisible text layer of a searchable PDF: every word text recognition found is drawn as text in render mode 3 (neither filled nor
/// stroked), scaled to the word's box on the page, so the page looks exactly as before while selecting, searching and copying work.
/// The picture or page content underneath is never touched; the text goes into a new content stream appended to the page.
/// </summary>
internal static class PdfSearchable
{
    /// <summary>How much of the box of a word with descenders lies below its baseline, as a fraction of the box's height.</summary>
    private const double DescenderFraction = 0.23;

    /// <summary>Text state "render mode 3" (invisible), put before everything else in the appended stream.</summary>
    private static readonly XStringFormat Baseline = new() { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.BaseLine };

    private static readonly byte[] InvisibleMode = "3 Tr\n"u8.ToArray();

    /// <summary>
    /// Draws the words of <paramref name="text"/> invisibly on <paramref name="page"/>. <paramref name="area"/> is where the recognised picture
    /// sits in the page, in points from its top-left corner as the viewer shows it (the whole page for a scan, the picture's rectangle for an
    /// image page); <paramref name="rotation"/> is the page's <c>/Rotate</c> and <paramref name="visible"/> the part of the unrotated page the
    /// viewer shows (its crop box; default the whole page), in points from the media box's top-left corner. Returns how many words were added. Words in a script no installed
    /// font covers (see <see cref="FamilyOf"/>) are left out.
    /// </summary>
    public static int AddInvisibleText(PdfPage page, RecognizedText text, XRect area, int rotation = 0, XRect? visible = null)
    {
        if (text.Lines.Count == 0) return 0;
        PdfFonts.EnsureInitialized();

        var added = 0;
        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            // The page is drawn upright in the viewer; PDFsharp's coordinates are those of the unrotated page. Map one onto the other.
            var region = visible ?? new XRect(0, 0, page.Width.Point, page.Height.Point);
            gfx.TranslateTransform(region.X, region.Y);
            var sideways = rotation is 90 or 270;
            var shownWidth = sideways ? region.Height : region.Width;
            var shownHeight = sideways ? region.Width : region.Height;
            switch (rotation)
            {
                case 90: gfx.MultiplyTransform(new XMatrix(0, -1, 1, 0, 0, shownWidth)); break;
                case 180: gfx.MultiplyTransform(new XMatrix(-1, 0, 0, -1, shownWidth, shownHeight)); break;
                case 270: gfx.MultiplyTransform(new XMatrix(0, 1, -1, 0, shownHeight, 0)); break;
            }

            var sx = area.Width / text.Width;
            var sy = area.Height / text.Height;
            var fonts = new Dictionary<string, XFont?>();
            foreach (var line in text.Lines)
            {
                // One baseline and one size for the whole line: the boxes of words with descenders (g, p, y) are taller, and drawing each
                // word from its own box would stagger the line, which readers then take for several lines.
                var words = line.Words.Where(w => w.Width > 0 && w.Height > 0).ToList();
                if (words.Count == 0) continue;
                var baseline = Median(words.Select(w => (w.Y + w.Height * (HasDescender(w.Text) ? 1 - DescenderFraction : 1)) * sy));
                var size = Math.Clamp(Median(words.Select(w => w.Height * sy)) * 0.85, 1, 400);

                foreach (var word in words)
                {
                    var family = FamilyOf(word.Text);
                    if (family is null) continue;
                    var key = family + "|" + size.ToString("F2", CultureInfo.InvariantCulture);
                    if (!fonts.TryGetValue(key, out var font))
                    {
                        try { font = new XFont(family, size, XFontStyleEx.Regular); }
                        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException) { font = null; }
                        fonts[key] = font;
                    }
                    if (font is null) continue;

                    var natural = gfx.MeasureString(word.Text, font).Width;
                    if (natural <= 0) continue;

                    var state = gfx.Save();
                    gfx.TranslateTransform(area.X + word.X * sx, area.Y + baseline);
                    gfx.ScaleTransform(word.Width * sx / natural, 1); // stretch or squeeze the word to the box the engine found
                    gfx.DrawString(word.Text, font, XBrushes.Black, new XPoint(0, 0), Baseline);
                    gfx.Restore(state);
                    added++;
                }
            }
        }

        if (added > 0) MakeInvisible(page);
        return added;
    }

    /// <summary>
    /// The part of the page a viewer shows, in points from the media box's top-left corner (where PDFsharp's drawing starts): the crop box
    /// cut to the media box, or the whole page when it has none.
    /// </summary>
    internal static XRect VisibleRegion(PdfPage page)
    {
        var media = page.MediaBox;
        // Reading CropBox on a page without one would write an empty entry into it, so look first.
        if (!page.Elements.ContainsKey("/CropBox")) return new XRect(0, 0, media.Width, media.Height);
        var crop = page.CropBox;
        if (crop.IsZero) return new XRect(0, 0, media.Width, media.Height);
        double x1 = Math.Max(crop.X1, media.X1), x2 = Math.Min(crop.X2, media.X2);
        double y1 = Math.Max(crop.Y1, media.Y1), y2 = Math.Min(crop.Y2, media.Y2);
        if (x2 <= x1 || y2 <= y1) return new XRect(0, 0, media.Width, media.Height);
        return new XRect(x1 - media.X1, media.Y2 - y2, x2 - x1, y2 - y1);
    }

    private static bool HasDescender(string word) => word.AsSpan().IndexOfAny("gjpqy,;()[]{}QJ/|@$") >= 0;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>
    /// The installed font family that draws <paramref name="word"/>: Arial for Latin, Greek, Cyrillic, Hebrew and Arabic, the per-script
    /// fallbacks of the document writer for CJK, Hangul, Indic, Thai and Myanmar; null when no font for its script is installed.
    /// </summary>
    internal static string? FamilyOf(string word)
    {
        var face = PdfDocWriter.Face.Default;
        foreach (var rune in word.EnumerateRunes())
        {
            var f = PdfDocWriter.FaceOf(rune.Value);
            if (f != PdfDocWriter.Face.Default) { face = f; break; }
        }
        if (face != PdfDocWriter.Face.Default) return PdfDocWriter.FamilyFor(face);
        return new[] { "Arial", "Segoe UI", "Tahoma", "Verdana" }.FirstOrDefault(PdfFonts.IsInstalled);
    }

    /// <summary>Puts render mode 3 at the start of the stream just appended to <paramref name="page"/> (the last one in its contents).</summary>
    private static void MakeInvisible(PdfPage page)
    {
        var items = page.Contents.Elements.Items;
        if (items.Length == 0) return;
        var last = items[^1] is PdfReference reference ? reference.Value as PdfDictionary : items[^1] as PdfDictionary;
        var stream = last?.Stream;
        if (stream?.Value is not { } raw) return;

        var patched = new byte[InvisibleMode.Length + raw.Length];
        InvisibleMode.CopyTo(patched, 0);
        raw.CopyTo(patched, InvisibleMode.Length);
        stream.Value = patched;
    }
}
