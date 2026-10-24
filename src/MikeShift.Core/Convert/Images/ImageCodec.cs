using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Images;

/// <summary>What the bytes of a file say it is, regardless of its extension.</summary>
internal enum ImageContainer
{
    Unknown, Png, Jpeg, Gif, Bmp, Tiff, Webp, Heic, Avif, Ico, JpegXr,
}

/// <summary>A decoded image: upright pixels plus what the converters need to know about where they came from.</summary>
internal sealed class DecodedImage
{
    public required Raster Pixels { get; init; }
    public required ImageContainer Container { get; init; }

    /// <summary>Frames/pages in the file (animated GIF/APNG/WebP count as more than one).</summary>
    public int FrameCount { get; init; } = 1;

    /// <summary>True for animated GIF, APNG and animated WebP (also multi-page TIFF is <see cref="FrameCount"/> &gt; 1).</summary>
    public bool IsAnimated { get; init; }

    /// <summary>The source's Exif payload (<c>Exif\0\0</c> + TIFF) when it is a JPEG that has one; otherwise null.</summary>
    public byte[]? JpegExif { get; init; }

    /// <summary>
    /// A few descriptive properties read through WIC (date taken, camera make/model) so they can follow the picture into
    /// formats where the raw Exif block cannot be copied (HEIC to JPG, JPG to TIFF...). Null if none.
    /// </summary>
    public IReadOnlyDictionary<string, BitmapTypedValue>? Properties { get; init; }

    public int Width => Pixels.Width;
    public int Height => Pixels.Height;
    public bool HasRealAlpha => Pixels.HasRealAlpha;
}

/// <summary>
/// Decoding and encoding. Decode: Windows Imaging Component (WinRT <c>BitmapDecoder</c>) with SkiaSharp as fallback
/// (e.g. WebP when the free WebP extension is missing). Encode: WIC for PNG/JPEG/TIFF/GIF/BMP/HEIF, SkiaSharp for WebP.
/// Everything goes through memory so the caller can post-process bytes and commit them atomically via <c>Naming</c>.
/// </summary>
internal static class ImageCodec
{
    /// <summary>The 200-megapixel guard: larger pictures are refused instead of exhausting memory.</summary>
    public const long MaxPixels = 200_000_000;

    // ------------------------------------------------------------------ sniffing

    public static ImageContainer Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ImageContainer.Png;
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ImageContainer.Jpeg;
        if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return ImageContainer.Gif;
        if (h.Length >= 2 && h[0] == 'B' && h[1] == 'M') return ImageContainer.Bmp;
        if (h.Length >= 4 && ((h[0] == 'I' && h[1] == 'I' && h[2] == 42 && h[3] == 0) || (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && h[3] == 42)))
            return ImageContainer.Tiff;
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
            return ImageContainer.Webp;
        if (h.Length >= 4 && h[0] == 0 && h[1] == 0 && h[2] == 1 && h[3] == 0) return ImageContainer.Ico;
        if (h.Length >= 4 && h[0] == 0x49 && h[1] == 0x49 && h[2] == 0xBC) return ImageContainer.JpegXr;
        if (h.Length >= 12 && h[4] == 'f' && h[5] == 't' && h[6] == 'y' && h[7] == 'p')
        {
            // ISO base media: major brand then compatible brands.
            var end = (int)Math.Min(h.Length, Math.Max(16, BinaryPrimitives.ReadUInt32BigEndian(h)));
            for (var i = 8; i + 4 <= end; i += 4)
            {
                if (i == 12) continue; // minor version
                var b = h.Slice(i, 4);
                if (b.SequenceEqual("avif"u8) || b.SequenceEqual("avis"u8)) return ImageContainer.Avif;
            }
            for (var i = 8; i + 4 <= end; i += 4)
            {
                if (i == 12) continue;
                var b = h.Slice(i, 4);
                if (b.SequenceEqual("heic"u8) || b.SequenceEqual("heix"u8) || b.SequenceEqual("hevc"u8) || b.SequenceEqual("heim"u8)
                    || b.SequenceEqual("heis"u8) || b.SequenceEqual("mif1"u8) || b.SequenceEqual("msf1"u8))
                    return ImageContainer.Heic;
            }
        }
        return ImageContainer.Unknown;
    }

    public static ImageContainer SniffFile(string path)
    {
        var head = ReadHead(path, 64);
        var sniffed = Sniff(head);
        if (sniffed != ImageContainer.Unknown) return sniffed;
        return Kind.ExtensionOf(path) switch
        {
            "png" => ImageContainer.Png,
            "jpg" or "jpeg" or "jpe" or "jfif" => ImageContainer.Jpeg,
            "gif" => ImageContainer.Gif,
            "bmp" or "dib" => ImageContainer.Bmp,
            "tif" or "tiff" => ImageContainer.Tiff,
            "webp" => ImageContainer.Webp,
            "heic" or "heif" => ImageContainer.Heic,
            "avif" => ImageContainer.Avif,
            "ico" => ImageContainer.Ico,
            "jxr" or "wdp" => ImageContainer.JpegXr,
            _ => ImageContainer.Unknown,
        };
    }

    public static byte[] ReadHead(string path, int count)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        var buf = new byte[(int)Math.Min(count, fs.Length)];
        var read = 0;
        while (read < buf.Length)
        {
            var n = fs.Read(buf, read, buf.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return read == buf.Length ? buf : buf[..read];
    }

    /// <summary>PNG with an <c>acTL</c> chunk before the first <c>IDAT</c> (APNG).</summary>
    public static bool IsApng(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            Span<byte> hdr = stackalloc byte[8];
            fs.Seek(8, SeekOrigin.Begin);
            while (fs.Position + 8 <= fs.Length)
            {
                if (fs.Read(hdr) < 8) return false;
                var len = BinaryPrimitives.ReadUInt32BigEndian(hdr);
                var type = hdr.Slice(4, 4);
                if (type.SequenceEqual("acTL"u8)) return true;
                if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;
                fs.Seek(len + 4L, SeekOrigin.Current); // data + CRC
            }
        }
        catch (Exception)
        {
            // not decidable: treat as a normal PNG
        }
        return false;
    }

    internal static bool IsAnimatedWebp(ReadOnlySpan<byte> head) =>
        head.Length >= 21 && head.Slice(12, 4).SequenceEqual("VP8X"u8) && (head[20] & 0x02) != 0;

    // ------------------------------------------------------------------ decode

    /// <summary>
    /// Decodes <paramref name="path"/> to upright BGRA pixels. Throws <see cref="ConversionException"/> for unreadable
    /// files, files over <paramref name="maxPixels"/> and blank HEIC/AVIF decodes.
    /// </summary>
    public static async Task<DecodedImage> DecodeAsync(string path, long maxPixels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var container = SniffFile(path);

        Raster? pixels = null;
        var frames = 1;
        var animated = false;
        IReadOnlyDictionary<string, BitmapTypedValue>? props = null;

        try
        {
            (pixels, frames, props) = await DecodeWicAsync(path, maxPixels, ct).ConfigureAwait(false);
        }
        catch (ConversionException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            pixels = null; // fall back to SkiaSharp below
        }

        if (pixels is null)
        {
            try
            {
                (pixels, frames) = DecodeSkia(path, maxPixels);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                // A HEIC/AVIF photo on a PC without the Store extension is not damaged: say which extension to install.
                var missing = container is ImageContainer.Heic or ImageContainer.Avif ? MissingCodecMessage(path) : null;
                throw new ConversionException(missing ?? "Couldn't read this image. The file may be damaged or in a format Windows can't open.", ex);
            }
        }

        switch (container)
        {
            case ImageContainer.Gif:
                animated = frames > 1;
                break;
            case ImageContainer.Png:
                animated = IsApng(path);
                break;
            case ImageContainer.Webp:
                animated = IsAnimatedWebp(ReadHead(path, 32));
                break;
        }

        RejectBlankDecode(container, pixels);

        byte[]? exif = null;
        if (container == ImageContainer.Jpeg)
        {
            // Exif lives in the first segments; 1 MB is plenty (an APP1 block is at most 64 KB).
            exif = JpegMeta.ExtractExif(ReadHead(path, 1 << 20));
        }

        return new DecodedImage
        {
            Pixels = pixels,
            Container = container,
            FrameCount = frames,
            IsAnimated = animated,
            JpegExif = exif,
            Properties = props,
        };
    }

    /// <summary>
    /// "Windows can't open HEIC photos yet. Install the free ..." when <paramref name="path"/> is a HEIC/HEIF/AVIF whose Windows add-on is
    /// missing (live checks of the WIC decoder list and the HEVC decoder, no full capability probe); null when the add-ons are there.
    /// </summary>
    private static string? MissingCodecMessage(string path)
    {
        try
        {
            var container = SniffFile(path);
            var ext = container == ImageContainer.Avif ? "avif" : "heic";
            return Feedback.CodecHelp.DecodeFailureMessage(ext, CanDecodeHeif(), Media.MediaProbe.HasHevcDecoder, CanDecodeAvif());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A truncated or unsupported HEIC/AVIF can "decode" to an all-black (or all-zero) picture instead of failing.
    /// Other formats may legitimately be black.
    /// </summary>
    internal static void RejectBlankDecode(ImageContainer container, Raster pixels)
    {
        if (container is ImageContainer.Heic or ImageContainer.Avif && pixels.IsAllBlack())
            throw new ConversionException("Couldn't read this image. It decoded as blank, so the file may be damaged or a codec is missing.");
    }

    private static void CheckSize(long w, long h, long maxPixels)
    {
        if (w * h > maxPixels)
            throw new ConversionException(
                $"This image is too large ({w * h / 1_000_000} megapixels). MikeShift handles up to {maxPixels / 1_000_000} megapixels.");
    }

    /// <summary>WIC property-system names carried across formats (no GPS on purpose: it only travels inside a same-format JPEG).</summary>
    private static readonly string[] CarriedProperties =
        ["System.Photo.DateTaken", "System.Photo.CameraManufacturer", "System.Photo.CameraModel"];

    private static async Task<(Raster Pixels, int Frames, IReadOnlyDictionary<string, BitmapTypedValue>? Props)> DecodeWicAsync(string path, long maxPixels, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
        using var ras = fs.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct).ConfigureAwait(false);
        CheckSize(decoder.PixelWidth, decoder.PixelHeight, maxPixels);

        using var sb = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)
            .AsTask(ct).ConfigureAwait(false);

        var bytes = new byte[checked(sb.PixelWidth * sb.PixelHeight * 4)];
        sb.CopyToBuffer(bytes.AsBuffer());

        IReadOnlyDictionary<string, BitmapTypedValue>? props = null;
        try
        {
            var read = await decoder.BitmapProperties.GetPropertiesAsync(CarriedProperties).AsTask(ct).ConfigureAwait(false);
            if (read.Count > 0) props = new Dictionary<string, BitmapTypedValue>(read);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadata is a courtesy; a file without readable properties still converts.
        }
        return (new Raster(sb.PixelWidth, sb.PixelHeight, bytes), (int)decoder.FrameCount, props);
    }

    private static (Raster Pixels, int Frames) DecodeSkia(string path, long maxPixels)
    {
        using var codec = SKCodec.Create(path) ?? throw new ConversionException("Couldn't read this image.");
        CheckSize(codec.Info.Width, codec.Info.Height, maxPixels);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result != SKCodecResult.Success)
            throw new ConversionException("Couldn't read this image. The file may be damaged.");
        var raster = Raster.FromSkia(bmp).ApplyOrigin(codec.EncodedOrigin);
        return (raster, Math.Max(1, codec.FrameCount));
    }

    // ------------------------------------------------------------------ probe helpers

    /// <summary>
    /// Header-only frame information (no pixel decode): how many frames the file has and whether it is an animation
    /// (animated GIF, APNG, animated WebP). Unknown or unreadable files report one still frame; the real decode decides later.
    /// </summary>
    public static async Task<(int Frames, bool Animated)> FrameInfoAsync(string path, CancellationToken ct)
    {
        try
        {
            var container = SniffFile(path);
            switch (container)
            {
                case ImageContainer.Png:
                    return (1, IsApng(path));
                case ImageContainer.Webp:
                    return (1, IsAnimatedWebp(ReadHead(path, 32)));
                case ImageContainer.Gif or ImageContainer.Tiff or ImageContainer.Ico:
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
                    using (var ras = fs.AsRandomAccessStream())
                    {
                        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct).ConfigureAwait(false);
                        var frames = (int)decoder.FrameCount;
                        return (frames, container == ImageContainer.Gif && frames > 1);
                    }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // not decidable from the header
        }
        return (1, false);
    }

    /// <summary>Header-only check that some decoder accepts the file.</summary>
    public static bool CanDecodeHeader(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            using var ras = fs.AsRandomAccessStream();
            var decoder = BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
            if (decoder.PixelWidth > 0 && decoder.PixelHeight > 0) return true;
        }
        catch (Exception)
        {
            // try SkiaSharp
        }

        try
        {
            using var codec = SKCodec.Create(path);
            return codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool CanEncodeHeif()
    {
        try
        {
            var px = new byte[16 * 16 * 4];
            Array.Fill(px, (byte)0x80);
            EncodeAsync(new Raster(16, 16, px), ImageFormat.Heic, 0.9, null, null, CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool CanEncodeWebp()
    {
        try
        {
            var px = new byte[8 * 8 * 4];
            Array.Fill(px, (byte)0x80);
            return new Raster(8, 8, px).EncodeWebp(80).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool CanDecodeAvif() => HasDecoderFor(".avif", "image/avif");

    /// <summary>A WIC decoder claims <c>.heic</c> (the Store "HEIF Image Extensions"). Says nothing about the HEVC decoder HEIC pixels also need.</summary>
    public static bool CanDecodeHeif() => HasDecoderFor(".heic", "image/heic");

    private static bool HasDecoderFor(string extension, string mimeType)
    {
        try
        {
            foreach (var info in BitmapDecoder.GetDecoderInformationEnumerator())
            {
                if (info.FileExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase))
                    || info.MimeTypes.Any(m => m.Equals(mimeType, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch (Exception)
        {
            // not available
        }
        return false;
    }

    // ------------------------------------------------------------------ encode

    /// <summary>
    /// Encodes <paramref name="pixels"/> in memory. <paramref name="quality"/> is 0-1 (JPEG, HEIC, WebP); <paramref name="exif"/>
    /// is a ready-to-insert Exif payload for JPEG output (already made upright), or null for none; <paramref name="properties"/>
    /// are descriptive WIC properties to write when the format supports them (best effort, dropped if the encoder refuses).
    /// </summary>
    public static async Task<byte[]> EncodeAsync(
        Raster pixels, ImageFormat format, double quality, byte[]? exif,
        IReadOnlyDictionary<string, BitmapTypedValue>? properties, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        quality = Math.Clamp(quality, 0.01, 1.0);

        if (format == ImageFormat.Webp)
            return pixels.EncodeWebp((int)Math.Round(quality * 100));

        // JPEG and BMP have no usable alpha: flatten on white. HEIC is the same in practice: Windows' HEIF encoder drops the alpha
        // channel (the decoder reports alpha=Ignore) and would leave whatever colour sits under the transparent pixels, so a
        // transparent PNG would come out with a solid coloured (often black) block. GIF keeps 1-bit transparency from WIC's own quantiser.
        var flatten = format is ImageFormat.Jpg or ImageFormat.Bmp or ImageFormat.Heic;
        var raster = flatten ? (pixels.HasRealAlpha ? pixels.FlattenOnWhite() : pixels) : pixels;
        var alphaMode = flatten ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight;

        var encoderId = format switch
        {
            ImageFormat.Png => BitmapEncoder.PngEncoderId,
            ImageFormat.Jpg => BitmapEncoder.JpegEncoderId,
            ImageFormat.Tiff => BitmapEncoder.TiffEncoderId,
            ImageFormat.Gif => BitmapEncoder.GifEncoderId,
            ImageFormat.Bmp => BitmapEncoder.BmpEncoderId,
            ImageFormat.Heic => BitmapEncoder.HeifEncoderId,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        var options = EncoderOptions(format, quality);
        var writeProps = properties is { Count: > 0 } && format is ImageFormat.Jpg or ImageFormat.Tiff or ImageFormat.Heic && exif is null
            ? properties : null;

        // Most to least ambitious: options + properties, options only, plain defaults. A setting the installed encoder
        // rejects must not fail the whole conversion.
        var attempts = new List<(BitmapPropertySet? Options, IReadOnlyDictionary<string, BitmapTypedValue>? Props)>();
        if (writeProps is not null) attempts.Add((options, writeProps));
        if (options is not null) attempts.Add((options, null));
        attempts.Add((null, null));

        byte[]? result = null;
        Exception? failure = null;
        foreach (var (attemptOptions, attemptProps) in attempts)
        {
            try
            {
                result = await WicEncodeAsync(raster, encoderId, alphaMode, attemptOptions, attemptProps, ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                failure = ex;
            }
        }

        var bytes = result ?? throw new ConversionException(
            format == ImageFormat.Heic
                ? "This PC can't save HEIC images. Install the HEIF and HEVC extensions from the Microsoft Store."
                : $"Couldn't save the image as {format.ToString().ToUpperInvariant()}.", failure ?? new InvalidOperationException("No encoder attempt ran."));

        if (format == ImageFormat.Jpg && exif is not null)
            bytes = JpegMeta.InsertExif(bytes, exif);
        return bytes;
    }

    private static BitmapPropertySet? EncoderOptions(ImageFormat format, double quality)
    {
        switch (format)
        {
            case ImageFormat.Jpg:
            case ImageFormat.Heic:
                return new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue((float)quality, PropertyType.Single) } };
            case ImageFormat.Tiff:
                return new BitmapPropertySet { { "TiffCompressionMethod", new BitmapTypedValue((byte)4, PropertyType.UInt8) } }; // LZW
            default:
                return null;
        }
    }

    private static async Task<byte[]> WicEncodeAsync(
        Raster raster, Guid encoderId, BitmapAlphaMode alphaMode, BitmapPropertySet? options,
        IReadOnlyDictionary<string, BitmapTypedValue>? properties, CancellationToken ct)
    {
        using var sb = SoftwareBitmap.CreateCopyFromBuffer(
            raster.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, raster.Width, raster.Height, alphaMode);
        using var ms = new InMemoryRandomAccessStream();
        var encoder = options is null
            ? await BitmapEncoder.CreateAsync(encoderId, ms).AsTask(ct).ConfigureAwait(false)
            : await BitmapEncoder.CreateAsync(encoderId, ms, options).AsTask(ct).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(sb);
        if (properties is not null)
        {
            var set = new BitmapPropertySet();
            foreach (var (key, value) in properties) set.Add(key, value);
            await encoder.BitmapProperties.SetPropertiesAsync(set).AsTask(ct).ConfigureAwait(false);
        }
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);

        var size = checked((int)ms.Size);
        var buffer = new Windows.Storage.Streams.Buffer((uint)size);
        ms.Seek(0);
        await ms.ReadAsync(buffer, (uint)size, InputStreamOptions.None).AsTask(ct).ConfigureAwait(false);
        return buffer.ToArray(0, size);
    }
}
