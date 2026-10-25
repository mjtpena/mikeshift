using PdfSharp.Drawing;
using PdfSharp.Pdf;
using MikeShift.Core.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// PDF fixtures: everything is generated at test time with PDFsharp and WIC; nothing binary is checked in.
/// Image generators are prefixed <c>PdfSource</c> so they never collide with the image fixtures.
/// </summary>
public static partial class Fixtures
{
    /// <summary>Password for <see cref="PdfEncrypted"/>.</summary>
    public const string PdfUserPassword = "user-secret";

    // Hebrew written with escapes so the source encoding never matters. Every word ends in a final-form letter or has none.
    public const string HebrewShalomOlam = "שלום עולם";           // shalom olam
    public const string HebrewLogicalText = HebrewShalomOlam + " אמן מלך ספרים"; // + amen melech sfarim

    static partial void WritePdfFixtures(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "text-3-pages.pdf"), PdfText(3));
        File.WriteAllBytes(Path.Combine(dir, "landscape.pdf"), PdfLandscape());
        File.WriteAllBytes(Path.Combine(dir, "image-heavy.pdf"), PdfImageHeavy());
        File.WriteAllBytes(Path.Combine(dir, "scan.pdf"), PdfScan());
        File.WriteAllBytes(Path.Combine(dir, "encrypted.pdf"), PdfEncrypted());
        File.WriteAllBytes(Path.Combine(dir, "hebrew.pdf"), PdfHebrew(reversed: false));
        File.WriteAllBytes(Path.Combine(dir, "hebrew-reversed.pdf"), PdfHebrew(reversed: true));
        File.WriteAllBytes(Path.Combine(dir, "corrupt.pdf"), PdfCorrupt());
        File.WriteAllBytes(Path.Combine(dir, "pdf-source-photo.jpg"), PdfSourceJpeg(1600, 1200));
        File.WriteAllBytes(Path.Combine(dir, "pdf-source-portrait.jpg"), PdfSourceJpeg(900, 1400));
        File.WriteAllBytes(Path.Combine(dir, "pdf-source-multi.tif"), PdfSourceTiff(3, 400, 300));
    }

    // ------------------------------------------------------------------ PDFs

    /// <summary>A text PDF: page <c>i</c> carries "Page i of n" and two more lines.</summary>
    public static byte[] PdfText(int pages = 3)
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var font = new XFont("Arial", 14);
        for (var p = 1; p <= pages; p++)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {p} of {pages}", font, XBrushes.Black, 60, 80);
            gfx.DrawString("The quick brown fox jumps over the lazy dog.", font, XBrushes.Black, 60, 120);
            gfx.DrawString($"Second line on page {p}.", font, XBrushes.Black, 60, 150);
        }
        return Save(doc);
    }

    /// <summary>A single landscape page of text.</summary>
    public static byte[] PdfLandscape()
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Orientation = PdfSharp.PageOrientation.Landscape;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("A landscape page.", new XFont("Arial", 20), XBrushes.Black, 60, 80);
        return Save(doc);
    }

    /// <summary>One very wide page (4000 x 2000 pt) so that rendering at 2x would exceed the 8000 px cap.</summary>
    public static byte[] PdfHugePage()
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(4000);
        page.Height = XUnit.FromPoint(2000);
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("Big page", new XFont("Arial", 120), XBrushes.Black, 200, 600);
        return Save(doc);
    }

    /// <summary>Three pages, each one large noisy JPEG (a photo-like scan), so image recompression pays off.</summary>
    public static byte[] PdfImageHeavy(int images = 3)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < images; i++)
        {
            var jpeg = PdfSourceJpeg(2800, 2100, quality: 0.95, seed: 100 + i);
            using var ms = new MemoryStream(jpeg);
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            using var img = XImage.FromStream(ms);
            gfx.DrawImage(img, 20, 20, page.Width.Point - 40, (page.Width.Point - 40) * 0.75);
        }
        return Save(doc);
    }

    /// <summary>One page holding a photo-like opaque PNG, which PDFsharp stores as a FlateDecode image XObject.</summary>
    public static byte[] PdfFlateImage()
    {
        using var doc = new PdfDocument();
        using var ms = new MemoryStream(PdfSourcePng(1200, 900, seed: 9));
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        using var img = XImage.FromStream(ms);
        gfx.DrawImage(img, 20, 20, 555, 416);
        return Save(doc);
    }

    /// <summary>A PDF with a single image and no text: what a scan looks like.</summary>
    public static byte[] PdfScan()
    {
        using var doc = new PdfDocument();
        using var ms = new MemoryStream(PdfSourceJpeg(600, 800, seed: 5));
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        using var img = XImage.FromStream(ms);
        gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
        return Save(doc);
    }

    /// <summary>A text PDF encrypted with <see cref="PdfUserPassword"/> (128-bit); needs a password to open.</summary>
    public static byte[] PdfEncrypted()
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("Top secret.", new XFont("Arial", 14), XBrushes.Black, 60, 80);
        doc.SecuritySettings.UserPassword = PdfUserPassword;
        doc.SecuritySettings.OwnerPassword = "owner-secret";
        return Save(doc);
    }

    /// <summary>A text PDF with an owner password only (print/copy restrictions): opens with an empty user password.</summary>
    public static byte[] PdfOwnerPasswordOnly()
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("Restricted but readable.", new XFont("Arial", 14), XBrushes.Black, 60, 80);
        doc.SecuritySettings.OwnerPassword = "owner-secret";
        doc.SecuritySettings.PermitPrint = false;
        doc.SecuritySettings.PermitExtractContent = false;
        return Save(doc);
    }

    /// <summary>
    /// Hebrew text. <paramref name="reversed"/> = false stores it in logical order (final letters at word ends, extractable);
    /// true stores the visual order (every word and the line reversed: final letters at word starts), as broken producers do.
    /// </summary>
    public static byte[] PdfHebrew(bool reversed)
    {
        PdfFonts.EnsureInitialized();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var text = reversed ? Reverse(HebrewLogicalText) : HebrewLogicalText;
            gfx.DrawString(text, new XFont("Arial", 16), XBrushes.Black, 60, 100);
        }
        return Save(doc);
    }

    /// <summary>Starts with <c>%PDF</c> but is otherwise garbage.</summary>
    public static byte[] PdfCorrupt() =>
        [.. System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n"), .. Noise(2000, seed: 3)[1..]];

    private static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    private static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ source images (WIC)

    /// <summary>Photo-like pixels: smooth colour gradients plus noise (deterministic per <paramref name="seed"/>).</summary>
    public static byte[] PdfSourcePixels(int width, int height, int seed = 1, bool alpha = false)
    {
        var rng = new Random(seed);
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                data[o] = (byte)Math.Clamp(255 * x / Math.Max(1, width - 1) + rng.Next(-20, 21), 0, 255);       // B
                data[o + 1] = (byte)Math.Clamp(255 * y / Math.Max(1, height - 1) + rng.Next(-20, 21), 0, 255);  // G
                data[o + 2] = (byte)Math.Clamp(128 + rng.Next(-60, 61), 0, 255);                                  // R
                data[o + 3] = alpha ? (byte)(x < width / 2 ? 255 : 90) : (byte)255;
            }
        }
        return data;
    }

    public static byte[] PdfSourceJpeg(int width, int height, double quality = 0.9, int seed = 1) =>
        PdfEncodeWic(BitmapEncoder.JpegEncoderId, width, height, PdfSourcePixels(width, height, seed), quality);

    public static byte[] PdfSourcePng(int width, int height, bool alpha = false, int seed = 1) =>
        PdfEncodeWic(BitmapEncoder.PngEncoderId, width, height, PdfSourcePixels(width, height, seed, alpha), null, alpha);

    /// <summary>A multi-frame TIFF with <paramref name="frames"/> frames (each its own colours).</summary>
    public static byte[] PdfSourceTiff(int frames, int width, int height)
    {
        return Task.Run(async () =>
        {
            using var ms = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.TiffEncoderId, ms);
            for (var f = 0; f < frames; f++)
            {
                if (f > 0) await encoder.GoToNextFrameAsync();
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96,
                    PdfSourcePixels(width, height, seed: f + 1));
            }
            await encoder.FlushAsync();
            return PdfRead(ms);
        }).GetAwaiter().GetResult();
    }

    private static byte[] PdfEncodeWic(Guid encoderId, int width, int height, byte[] bgra, double? quality, bool alpha = false)
    {
        return Task.Run(async () =>
        {
            using var ms = new InMemoryRandomAccessStream();
            BitmapEncoder encoder;
            if (quality is { } q)
            {
                var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue((float)q, Windows.Foundation.PropertyType.Single) } };
                encoder = await BitmapEncoder.CreateAsync(encoderId, ms, props);
            }
            else
            {
                encoder = await BitmapEncoder.CreateAsync(encoderId, ms);
            }
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
                (uint)width, (uint)height, 96, 96, bgra);
            await encoder.FlushAsync();
            return PdfRead(ms);
        }).GetAwaiter().GetResult();
    }

    private static byte[] PdfRead(InMemoryRandomAccessStream ms)
    {
        ms.Seek(0);
        using var output = new MemoryStream();
        ms.AsStreamForRead().CopyTo(output);
        return output.ToArray();
    }
}
