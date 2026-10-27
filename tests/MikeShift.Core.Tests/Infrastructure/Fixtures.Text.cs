using PdfSharp.Drawing;
using PdfSharp.Pdf;
using MikeShift.Core.Pdf;
using SkiaSharp;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Fixtures for reading text from pictures: pictures of known text (drawn with SkiaSharp in Arial, black on white or on transparent) and
/// PDFs whose pages are only such pictures, the way a scanner makes them (no text layer). Generated at test time.
/// </summary>
public static partial class Fixtures
{
    /// <summary>Two lines that text recognition reads reliably at the default size.</summary>
    public static readonly string[] OcrLines = ["The quick brown fox jumps over the lazy dog.", "Invoice number 4821 is due on Friday."];

    /// <summary>Where <see cref="TextPicture"/> starts drawing (the left edge and the top of the first line), in pixels.</summary>
    public const int TextPictureMargin = 40;

    /// <summary>
    /// A PNG with <paramref name="lines"/> drawn in black Arial at <paramref name="fontSize"/> px, one line under the other, on white (or on a
    /// fully transparent background). The picture is sized to the text unless <paramref name="width"/> is given.
    /// </summary>
    public static byte[] TextPicture(IReadOnlyList<string> lines, float fontSize = 40, bool transparent = false, int? width = null)
    {
        using var typeface = SKTypeface.FromFamilyName("Arial");
        using var font = new SKFont(typeface, fontSize);
        var lineHeight = fontSize * 1.6f;
        var w = width ?? (int)Math.Ceiling(lines.Max(l => font.MeasureText(l)) + 2 * TextPictureMargin);
        var h = (int)Math.Ceiling(lines.Count * lineHeight + 2 * TextPictureMargin);

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        for (var i = 0; i < lines.Count; i++)
            canvas.DrawText(lines[i], TextPictureMargin, TextPictureMargin + fontSize + i * lineHeight, SKTextAlign.Left, font, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// A PDF with one page per entry of <paramref name="pages"/>. A page given as text-layer lines is written with real text (PdfPig reads it);
    /// a page given as a picture is only that picture, placed across the top of an A4 page, like a scan.
    /// </summary>
    public static byte[] PdfOfPages(params PdfFixturePage[] pages)
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var streams = new List<MemoryStream>();
        try
        {
            foreach (var p in pages)
            {
                var page = doc.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                using var gfx = XGraphics.FromPdfPage(page);
                if (p.Picture is { } png)
                {
                    var ms = new MemoryStream(png, writable: false);
                    streams.Add(ms);
                    using var img = XImage.FromStream(ms);
                    var w = page.Width.Point - 80;
                    gfx.DrawImage(img, 40, 40, w, w * img.PixelHeight / img.PixelWidth);
                }
                else
                {
                    var font = new XFont("Arial", 14);
                    for (var i = 0; i < p.TextLayer!.Count; i++)
                        gfx.DrawString(p.TextLayer[i], font, XBrushes.Black, 60, 80 + i * 30);
                }
            }
            return Save(doc);
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    /// <summary>A scanned PDF: every page is a picture of <paramref name="pageLines"/>' lines and nothing else.</summary>
    public static byte[] PdfScanOfText(params string[][] pageLines) =>
        PdfOfPages(pageLines.Select(l => PdfFixturePage.Scan(l)).ToArray());
}

/// <summary>One page of <see cref="Fixtures.PdfOfPages"/>: real text, or a picture with no text layer.</summary>
public sealed record PdfFixturePage(IReadOnlyList<string>? TextLayer, byte[]? Picture)
{
    /// <summary>A page whose lines are real PDF text.</summary>
    public static PdfFixturePage Text(params string[] lines) => new(lines, null);

    /// <summary>A page that is a picture of <paramref name="lines"/> (drawn large enough to read at the page's rendered size).</summary>
    public static PdfFixturePage Scan(params string[] lines) => new(null, Fixtures.TextPicture(lines, fontSize: 48));

    /// <summary>A page that is a picture with no text at all.</summary>
    public static PdfFixturePage Blank(byte[] picture) => new(null, picture);
}
