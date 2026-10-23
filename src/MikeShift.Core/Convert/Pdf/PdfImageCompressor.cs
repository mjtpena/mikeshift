using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace MikeShift.Core.Pdf;

/// <summary>
/// Image recompression for PDF Compress: re-encodes DCT / Flate image XObjects as JPEG at a moderate quality and downscales
/// very large ones. Anything unusual (masks, soft masks, odd colour spaces or bit depths, line-art, tiny images) is left alone.
/// </summary>
internal static class PdfImageCompressor
{
    /// <summary>Images whose long side exceeds this are downscaled to it.</summary>
    public const int MaxLongSide = 2400;

    public const double JpegQuality = 0.65;

    private const int MinStreamBytes = 16 * 1024;

    /// <summary>Recompresses what it safely can. Returns the number of images replaced.</summary>
    public static async Task<int> RecompressAsync(PdfDocument doc, CancellationToken ct)
    {
        var replaced = 0;
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            ct.ThrowIfCancellationRequested();
            if (obj is not PdfDictionary dict || dict.Stream is null) continue;
            if (dict.Elements.GetName("/Subtype") != "/Image") continue;

            try
            {
                if (await TryRecompressAsync(dict).ConfigureAwait(false)) replaced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything we cannot handle stays exactly as it was.
            }
        }
        return replaced;
    }

    private static async Task<bool> TryRecompressAsync(PdfDictionary dict)
    {
        var el = dict.Elements;
        if (el.ContainsKey("/SMask") || el.ContainsKey("/Mask") || el.ContainsKey("/Decode")) return false;
        if (el.GetBoolean("/ImageMask")) return false;
        if (el.ContainsKey("/BitsPerComponent") && el.GetInteger("/BitsPerComponent") != 8) return false;

        int width = el.GetInteger("/Width"), height = el.GetInteger("/Height");
        if (width <= 0 || height <= 0) return false;

        var compsOrNull = Components(dict);
        if (compsOrNull is not (1 or 3)) return false;
        var comps = compsOrNull.Value;

        var filter = SingleFilter(dict);
        var original = dict.Stream!.Value;
        if (original is null || original.Length < MinStreamBytes) return false;

        var (targetW, targetH) = PdfPixels.FitLongSide(width, height, MaxLongSide);
        var channels = comps == 1 ? 1 : 4;
        PdfPixels.Raster raster;
        double keepRatio;

        if (filter == "/DCTDecode")
        {
            raster = await PdfPixels.DecodeBytesAsync(original, channels, MaxLongSide, colorManage: false).ConfigureAwait(false);
            if (raster.Width != targetW || raster.Height != targetH) return false;
            keepRatio = 0.9;
        }
        else if (filter == "/FlateDecode")
        {
            var pixels = DecodeFlate(dict, width, height, comps);
            if (pixels is null || LooksLikeLineArt(pixels, comps)) return false;
            raster = ToRaster(pixels, width, height, comps);
            raster = PdfPixels.Downscale(raster, targetW, targetH);
            keepRatio = 0.7;
        }
        else
        {
            return false;
        }

        var jpeg = await PdfPixels.EncodeJpegAsync(raster, JpegQuality).ConfigureAwait(false);
        if (jpeg.Length >= original.Length * keepRatio) return false;

        dict.Stream.Value = jpeg;
        el.SetName("/Filter", "/DCTDecode");
        el.Remove("/DecodeParms");
        el.SetInteger("/Width", raster.Width);
        el.SetInteger("/Height", raster.Height);
        el.SetInteger("/BitsPerComponent", 8);
        return true;
    }

    /// <summary>1 or 3 for DeviceGray / DeviceRGB / ICCBased(N); null for anything else (Indexed, Cal*, Lab, Separation, CMYK...).</summary>
    private static int? Components(PdfDictionary dict)
    {
        var cs = dict.Elements.GetValue("/ColorSpace");
        switch (cs)
        {
            case PdfName n:
                return n.Value switch { "/DeviceRGB" => 3, "/DeviceGray" => 1, _ => null };
            case PdfArray a when a.Elements.Count >= 2 && a.Elements.GetName(0) == "/ICCBased":
                if (a.Elements.GetObject(1) is PdfDictionary icc)
                {
                    var n = icc.Elements.GetInteger("/N");
                    if (n is 1 or 3) return n;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>The single filter name, or null for none / a filter chain.</summary>
    private static string? SingleFilter(PdfDictionary dict)
    {
        var f = dict.Elements.GetValue("/Filter");
        return f switch
        {
            PdfName n => n.Value,
            PdfArray { Elements.Count: 1 } a => a.Elements.GetName(0),
            _ => null,
        };
    }

    /// <summary>Inflates a FlateDecode image to raw interleaved 8-bit samples, undoing PNG predictors. Null if unsupported.</summary>
    private static byte[]? DecodeFlate(PdfDictionary dict, int width, int height, int comps)
    {
        var predictor = 1;
        var columns = 1;
        var colors = 1;
        var bpc = 8;

        // Not Elements.GetValue: it throws a NullReferenceException inside PDFsharp when the value is a direct dictionary (the usual form).
        var parmsItem = Resolve(dict.Elements["/DecodeParms"]);
        if (parmsItem is PdfArray { Elements.Count: 1 } arr) parmsItem = Resolve(arr.Elements[0]);
        if (parmsItem is PdfDictionary parms)
        {
            if (parms.Elements.ContainsKey("/Predictor")) predictor = parms.Elements.GetInteger("/Predictor");
            if (parms.Elements.ContainsKey("/Columns")) columns = parms.Elements.GetInteger("/Columns");
            if (parms.Elements.ContainsKey("/Colors")) colors = parms.Elements.GetInteger("/Colors");
            if (parms.Elements.ContainsKey("/BitsPerComponent")) bpc = parms.Elements.GetInteger("/BitsPerComponent");
        }
        else if (parmsItem is not null && parmsItem is not PdfNull)
        {
            return null;
        }

        byte[] inflated;
        try
        {
            using var input = new MemoryStream(dict.Stream!.Value, writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            inflated = output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var rowBytes = width * comps;
        if (predictor == 1)
            return inflated.Length >= (long)rowBytes * height ? inflated : null;

        if (predictor < 10 || colors != comps || columns != width || bpc != 8) return null;
        return UndoPngPredictor(inflated, rowBytes, height, comps);
    }

    /// <summary>Follows an indirect reference; direct objects (and null) pass through.</summary>
    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;

    private static byte[]? UndoPngPredictor(byte[] data, int rowBytes, int height, int bytesPerPixel)
    {
        if (data.Length < (long)(rowBytes + 1) * height) return null;

        var result = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var src = y * (rowBytes + 1);
            var filter = data[src++];
            var dst = y * rowBytes;
            var prev = dst - rowBytes; // row above (valid when y > 0)

            for (var x = 0; x < rowBytes; x++)
            {
                int raw = data[src + x];
                int left = x >= bytesPerPixel ? result[dst + x - bytesPerPixel] : 0;
                int up = y > 0 ? result[prev + x] : 0;
                int upLeft = y > 0 && x >= bytesPerPixel ? result[prev + x - bytesPerPixel] : 0;
                int value = filter switch
                {
                    0 => raw,
                    1 => raw + left,
                    2 => raw + up,
                    3 => raw + ((left + up) >> 1),
                    4 => raw + Paeth(left, up, upLeft),
                    _ => -1,
                };
                if (value < 0) return null;
                result[dst + x] = (byte)value;
            }
        }
        return result;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static PdfPixels.Raster ToRaster(byte[] samples, int width, int height, int comps)
    {
        if (comps == 1)
        {
            var gray = new byte[width * height];
            System.Buffer.BlockCopy(samples, 0, gray, 0, gray.Length);
            return new PdfPixels.Raster(gray, width, height, 1);
        }

        var bgra = new byte[width * height * 4];
        for (int i = 0, s = 0, d = 0; i < width * height; i++, s += 3, d += 4)
        {
            bgra[d] = samples[s + 2];
            bgra[d + 1] = samples[s + 1];
            bgra[d + 2] = samples[s];
            bgra[d + 3] = 255;
        }
        return new PdfPixels.Raster(bgra, width, height, 4);
    }

    /// <summary>Screenshots, charts and logos (few distinct colours) look worse as JPEG and compress well as Flate: leave them.</summary>
    private static bool LooksLikeLineArt(byte[] samples, int comps)
    {
        var pixels = samples.Length / comps;
        if (pixels == 0) return true;
        var step = Math.Max(1, pixels / 4096);
        // A gray image has at most 256 distinct values, so "many colours" must be a lower bar than for RGB.
        var richEnough = comps == 1 ? 64 : 256;
        var seen = new HashSet<int>();
        for (var p = 0; p < pixels; p += step)
        {
            var o = p * comps;
            var key = comps == 1 ? samples[o] : samples[o] | samples[o + 1] << 8 | samples[o + 2] << 16;
            seen.Add(key);
            if (seen.Count >= richEnough) return false;
        }
        return true;
    }
}
