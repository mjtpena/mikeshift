using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;

namespace MikeShift.Core.Pdf;

/// <summary>
/// Decodes an image file (every TIFF frame; first frame for everything else) into upright, opaque JPEG pages for the PDF
/// writer. Own small WIC helper so the PDF package does not depend on the image package.
/// </summary>
internal static class PdfImageSource
{
    /// <summary>Long side limit: A4 at 300 dpi.</summary>
    public const int MaxLongSide = 3508;

    public const double JpegQuality = 0.85;

    private const long MaxPixels = 200_000_000;

    /// <summary>One page worth of image: baseline JPEG bytes and their pixel size.</summary>
    internal readonly record struct PageImage(byte[] Jpeg, int Width, int Height);

    public static async Task<IReadOnlyList<PageImage>> LoadAsync(string path, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ras = fs.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);

            var name = Path.GetFileName(path);
            if ((long)decoder.PixelWidth * decoder.PixelHeight > MaxPixels)
                throw new ConversionException($"{name} is too large to convert (over 200 megapixels).");

            var isTiff = decoder.DecoderInformation.CodecId == BitmapDecoder.TiffDecoderId;
            var count = isTiff ? (int)decoder.FrameCount : 1;
            var pages = new List<PageImage>(count);

            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var frame = await decoder.GetFrameAsync((uint)i);
                if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
                    throw new ConversionException($"{name} is too large to convert (over 200 megapixels).");

                var raster = await PdfPixels.DecodeFrameAsync(frame, channels: 4, MaxLongSide, colorManage: true);
                var orientation = await PdfPixels.ReadOrientationAsync(frame);
                raster = PdfPixels.ApplyOrientation(raster, orientation);
                PdfPixels.FlattenOnWhite(raster.Data);
                var jpeg = await PdfPixels.EncodeJpegAsync(raster, JpegQuality);
                pages.Add(new PageImage(jpeg, raster.Width, raster.Height));
            }
            return pages;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException
                                       or ArgumentException or InvalidDataException or OutOfMemoryException)
        {
            throw new ConversionException($"{Path.GetFileName(path)} could not be read as an image.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConversionException($"{Path.GetFileName(path)} could not be opened: {ex.Message}", ex);
        }
    }
}
