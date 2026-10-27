using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using MikeShift.Core;
using MikeShift.Core.Images;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MikeShift.App.Editors;

/// <summary>A downscaled upright preview and the size of the full image (also upright).</summary>
internal sealed record PreviewImage(SoftwareBitmapSource Source, int ImageWidth, int ImageHeight, int PreviewWidth, int PreviewHeight);

/// <summary>
/// Decodes an image for the crop editor: EXIF orientation applied, long side at most 1600 px. WIC first; a picture WIC has no decoder
/// for (WebP without the WebP Image Extension) goes through <see cref="ImagePreview"/>, which decodes the way the converters do.
/// </summary>
internal static class PreviewLoader
{
    public const int MaxPreviewSide = 1600;

    public static async Task<PreviewImage> LoadAsync(string path)
    {
        if (CapabilityOverrides.HidesWicDecoder(path)) return await LoadWithoutWicDecoderAsync(path);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        BitmapDecoder decoder;
        try
        {
            decoder = await BitmapDecoder.CreateAsync(stream);
        }
        catch (COMException)
        {
            return await LoadWithoutWicDecoderAsync(path);
        }

        // The upright size is what CropChoice speaks in (see CropChoice in Interaction.cs).
        var width = (int)decoder.OrientedPixelWidth;
        var height = (int)decoder.OrientedPixelHeight;
        var shrink = Math.Min(1.0, (double)MaxPreviewSide / Math.Max(width, height));
        var targetW = Math.Max(1, (int)Math.Round(width * shrink));
        var targetH = Math.Max(1, (int)Math.Round(height * shrink));

        var bitmap = await DecodeAsync(decoder, targetW, targetH);
        if (bitmap.PixelWidth != targetW || bitmap.PixelHeight != targetH)
        {
            // Some decoders take the scaled size before the rotation, some after: try the transposed request once.
            if (bitmap.PixelWidth == targetH && bitmap.PixelHeight == targetW && targetW != targetH)
            {
                bitmap.Dispose();
                bitmap = await DecodeAsync(decoder, targetH, targetW);
            }
        }

        return await ToPreviewAsync(bitmap, width, height);
    }

    private static async Task<PreviewImage> LoadWithoutWicDecoderAsync(string path)
    {
        var px = await ImagePreview.DecodeAsync(path, MaxPreviewSide);
        using var straight = SoftwareBitmap.CreateCopyFromBuffer(
            px.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, px.Width, px.Height, BitmapAlphaMode.Straight);
        // SoftwareBitmapSource only takes premultiplied BGRA.
        return await ToPreviewAsync(SoftwareBitmap.Convert(straight, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied),
            px.ImageWidth, px.ImageHeight);
    }

    /// <summary>Wraps <paramref name="bitmap"/> (premultiplied BGRA) for display and disposes it.</summary>
    private static async Task<PreviewImage> ToPreviewAsync(SoftwareBitmap bitmap, int imageWidth, int imageHeight)
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        var result = new PreviewImage(source, imageWidth, imageHeight, bitmap.PixelWidth, bitmap.PixelHeight);
        bitmap.Dispose();
        return result;
    }

    private static async Task<SoftwareBitmap> DecodeAsync(BitmapDecoder decoder, int scaledWidth, int scaledHeight)
    {
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)scaledWidth,
            ScaledHeight = (uint)scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
    }
}
