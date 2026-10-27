using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Pdf;

/// <summary>Page rendering through <c>Windows.Data.Pdf</c>.</summary>
internal static class PdfRender
{
    /// <summary>Render scale relative to the page's natural size (1/96 inch units).</summary>
    public const double MaxScale = 2.0;

    /// <summary>Longest side of a rendered page, in pixels.</summary>
    public const int MaxLongSide = 8000;

    public const double JpegQuality = 0.9;

    /// <summary>Opens the document for rendering; maps load failures to clear messages.</summary>
    public static async Task<PdfDocument> OpenAsync(byte[] bytes)
    {
        var ras = new InMemoryRandomAccessStream();
        try
        {
            await ras.WriteAsync(bytes.AsBuffer());
            ras.Seek(0);
            var doc = await PdfDocument.LoadFromStreamAsync(ras);
            if (doc.IsPasswordProtected) throw new ConversionException(PdfGuard.EncryptedMessage);
            if (doc.PageCount == 0) throw new ConversionException("This PDF has no pages.");
            return doc;
        }
        catch (ConversionException)
        {
            ras.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            ras.Dispose();
            if (PdfGuard.IsEncryptedFile(bytes)) throw new ConversionException(PdfGuard.EncryptedMessage, ex);
            throw new ConversionException(PdfGuard.DamagedMessage, ex);
        }
    }

    /// <summary>
    /// Render scale for reading a page's text (300 dpi, where 1 = 96 dpi): small print in a scan needs more pixels than a picture of the page.
    /// Still capped by <see cref="MaxLongSide"/>.
    /// </summary>
    public const double TextScale = 300.0 / 96.0;

    /// <summary>Pixel size a page is rendered at: 2x its natural size (or <paramref name="scale"/>), capped so the long side is at most 8000 px.</summary>
    public static (int Width, int Height) TargetSize(double widthDip, double heightDip, double scale = MaxScale)
    {
        var w = Math.Max(1.0, widthDip * scale);
        var h = Math.Max(1.0, heightDip * scale);
        var longSide = Math.Max(w, h);
        if (longSide > MaxLongSide)
        {
            var s = MaxLongSide / longSide;
            w *= s;
            h *= s;
        }
        return (Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
    }

    /// <summary>Renders page <paramref name="index"/> on a white background and returns the encoded image.</summary>
    public static async Task<byte[]> RenderPageAsync(PdfDocument doc, uint index, PdfPageFormat format, double jpegQuality = JpegQuality)
    {
        try
        {
            using var page = doc.GetPage(index);
            var (w, h) = TargetSize(page.Size.Width, page.Size.Height);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)w,
                DestinationHeight = (uint)h,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };

            using var ms = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ms, options);
            var png = await PdfPixels.ReadAllAsync(ms);
            if (format == PdfPageFormat.Png) return png;

            // JPG: decode the PNG render and re-encode at the JPEG quality (already opaque on white).
            var raster = await PdfPixels.DecodeBytesAsync(png, channels: 4, maxLongSide: 0, colorManage: false);
            PdfPixels.FlattenOnWhite(raster.Data);
            return await PdfPixels.EncodeJpegAsync(raster, jpegQuality);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            throw new ConversionException($"Page {index + 1} could not be rendered.", ex);
        }
    }

    /// <summary>Renders page <paramref name="index"/> on a white background at <see cref="TextScale"/>, as pixels for text recognition.</summary>
    public static async Task<Images.Raster> RenderPageForTextAsync(PdfDocument doc, uint index)
    {
        try
        {
            using var page = doc.GetPage(index);
            var (w, h) = TargetSize(page.Size.Width, page.Size.Height, TextScale);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)w,
                DestinationHeight = (uint)h,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                BitmapEncoderId = BitmapEncoder.BmpEncoderId, // never written to disk: skip PNG compression
            };

            using var ms = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(ms, options);
            var raster = await PdfPixels.DecodeBytesAsync(await PdfPixels.ReadAllAsync(ms), channels: 4, maxLongSide: 0, colorManage: false);
            return new Images.Raster(raster.Width, raster.Height, raster.Data);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            throw new ConversionException($"Page {index + 1} could not be rendered.", ex);
        }
    }
}
