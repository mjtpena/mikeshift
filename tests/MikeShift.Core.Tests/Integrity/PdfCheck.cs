using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using UglyToad.PdfPig;
using Windows.Storage.Streams;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Independent PDF readers for the integrity suite: PdfPig (a pure managed parser; the converters write with PDFsharp) for structure
/// and text, and Windows.Data.Pdf (the OS renderer) for "does it actually draw".
/// </summary>
internal static class PdfCheck
{
    public sealed record PageInfo(double Width, double Height, string Text, int Images);

    /// <summary>Opens with PdfPig and returns one record per page. Throws if PdfPig cannot parse the file.</summary>
    public static IReadOnlyList<PageInfo> Read(string path)
    {
        using var doc = PdfDocument.Open(path);
        var pages = new List<PageInfo>();
        foreach (var p in doc.GetPages())
            pages.Add(new PageInfo(p.Width, p.Height, WordsOf(p), p.GetImages().Count()));
        Assert.Equal(doc.NumberOfPages, pages.Count);
        return pages;
    }

    /// <summary>The words of a page in content-stream order joined by single spaces (PdfPig's <c>Page.Text</c> runs lines together).</summary>
    private static string WordsOf(UglyToad.PdfPig.Content.Page p) => string.Join(' ', p.GetWords().Select(w => w.Text));

    /// <summary>Page texts as PdfPig extracts them with the same words/lines reading order.</summary>
    public static IReadOnlyList<string> Texts(string path)
    {
        using var doc = PdfDocument.Open(path);
        return doc.GetPages().Select(WordsOf).ToList();
    }

    /// <summary>
    /// Page count according to Windows.Data.Pdf (the renderer Edge and Photos use). Fails if Windows cannot load the file or any
    /// page cannot be rendered to a bitmap; returns the rendered pages (width capped at <paramref name="maxWidth"/> px) when asked.
    /// </summary>
    public static async Task<IReadOnlyList<Px>> RenderAllAsync(string path, int maxWidth = 600)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(bytes.AsBuffer());
        ras.Seek(0);
        var doc = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(ras);
        var pages = new List<Px>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage(i);
            var scale = Math.Min(2.0, maxWidth / page.Size.Width);
            var w = (uint)Math.Max(1, Math.Round(page.Size.Width * scale));
            var h = (uint)Math.Max(1, Math.Round(page.Size.Height * scale));
            using var ms = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = w,
                DestinationHeight = h,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            });
            ms.Seek(0);
            using var s = ms.AsStreamForRead();
            using var bmp = SKBitmap.Decode(s) ?? throw new Xunit.Sdk.XunitException($"page {i + 1} did not render to a bitmap");
            pages.Add(ToPx(bmp));
        }
        return pages;
    }

    private static Px ToPx(SKBitmap bmp)
    {
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        var h = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var img = SKImage.FromBitmap(bmp);
            Assert.True(img.ReadPixels(info, h.AddrOfPinnedObject(), bmp.Width * 4, 0, 0));
        }
        finally { h.Free(); }
        return new Px(bmp.Width, bmp.Height, bytes);
    }

    /// <summary>The bounding box of everything that is not (near) white on a rendered page, or null for a blank page.</summary>
    public static (int X, int Y, int W, int H)? InkBounds(Px page, int threshold = 24)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < page.H; y++)
            for (var x = 0; x < page.W; x++)
            {
                var p = page.At(x, y);
                if (255 - p.B > threshold || 255 - p.G > threshold || 255 - p.R > threshold)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Fraction of pixels that are not white (a cheap "something was drawn" measure).</summary>
    public static double InkFraction(Px page, int threshold = 24)
    {
        long ink = 0;
        for (var i = 0; i < page.Bgra.Length; i += 4)
            if (255 - page.Bgra[i] > threshold || 255 - page.Bgra[i + 1] > threshold || 255 - page.Bgra[i + 2] > threshold) ink++;
        return (double)ink / (page.W * page.H);
    }
}
