using System.Text;
using MikeShift.Core.Feedback;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace MikeShift.Core.Images;

/// <summary>
/// QR codes, both ways, with ZXing.Net (Apache-2.0, managed, no network): Read QR code (a picture to <c>name qr.txt</c>) and QR code (a
/// <c>.url</c> shortcut or a short <c>.txt</c> to <c>name qr.png</c>). The code holds the text itself, so it never depends on a service.
/// </summary>
internal static class QrCodes
{
    /// <summary>Smallest side of the picture written: sharp when printed or put on a slide.</summary>
    public const int MinimumPixels = 1024;

    /// <summary>Modules of white around the code; the QR standard asks for four.</summary>
    private const int QuietZone = 4;

    /// <summary>A text or link file bigger than this is refused without being read; no code could hold it anyway.</summary>
    private const long MaxInputBytes = 64 * 1024;

    // ------------------------------------------------------------------ read

    /// <summary>
    /// Writes the text of every QR code in <paramref name="input"/> to <c>name qr.txt</c> (UTF-8, one line per code, top to bottom) and returns
    /// the path with the first code's first line. Refuses a picture with no code (nothing is written).
    /// </summary>
    public static async Task<(string Path, string First)> ReadAsync(string input, CancellationToken ct)
    {
        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        var texts = await Task.Run(() => Decode(img.Pixels, ct), ct).ConfigureAwait(false);
        if (texts.Count == 0) throw new ConversionException(QrCopy.NoCodeInPicture);

        var spec = Naming.Plan(input, ".txt", " qr");
        var result = await Naming.CommitAsync(spec,
            (temp, token) => File.WriteAllTextAsync(temp, string.Join(Environment.NewLine, texts), new UTF8Encoding(false), token), ct)
            .ConfigureAwait(false);
        return (result.Path, FirstLine(texts[0]));
    }

    /// <summary>The distinct texts of the QR codes in <paramref name="pixels"/>, top to bottom then left to right.</summary>
    internal static List<string> Decode(Raster pixels, CancellationToken ct)
    {
        var flat = pixels.HasRealAlpha ? pixels.FlattenOnWhite() : pixels;
        var source = new RGBLuminanceSource(flat.Bgra, flat.Width, flat.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] },
        };
        ct.ThrowIfCancellationRequested();

        var found = reader.DecodeMultiple(source);
        if (found is null || found.Length == 0)
        {
            ct.ThrowIfCancellationRequested();
            found = reader.Decode(source) is { } one ? [one] : [];
        }

        return found
            .Where(r => !string.IsNullOrEmpty(r.Text))
            .OrderBy(r => r.ResultPoints is { Length: > 0 } p ? Math.Round(p[0].Y / 20) : 0)
            .ThenBy(r => r.ResultPoints is { Length: > 0 } p ? p[0].X : 0)
            .Select(r => r.Text)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string FirstLine(string text)
    {
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return end < 0 ? text : text[..end];
    }

    // ------------------------------------------------------------------ write

    /// <summary>
    /// Writes a QR code for the web address in a <c>.url</c> file, or for the text of a <c>.txt</c> file, to <c>name qr.png</c> and returns its
    /// path. Text that does not fit is refused before anything is written.
    /// </summary>
    public static async Task<string> WriteAsync(string input, CancellationToken ct)
    {
        var text = ReadContent(input);
        var png = await EncodePngAsync(text, ct).ConfigureAwait(false);

        var spec = Naming.Plan(input, ".png", " qr");
        var result = await Naming.CommitAsync(spec, (temp, token) => File.WriteAllBytesAsync(temp, png, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>What to put in the code: the <c>URL=</c> line of a <c>.url</c> file, else the whole text (without its trailing line breaks).</summary>
    internal static string ReadContent(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxInputBytes) throw new ConversionException(QrCopy.TooLong(length));
        var text = DecodeText(File.ReadAllBytes(path));

        if (Kind.ExtensionOf(path) == Kind.LinkExtension)
        {
            var address = UrlOf(text);
            return address.Length > 0 ? address : throw new ConversionException(QrCopy.NoAddress);
        }

        text = text.TrimEnd('\r', '\n');
        return text.Trim().Length > 0 ? text : throw new ConversionException(QrCopy.NothingToEncode);
    }

    /// <summary>The value of the first <c>URL=</c> line of an Internet shortcut, or "".</summary>
    internal static string UrlOf(string shortcut)
    {
        foreach (var raw in shortcut.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 4 && line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                return line[4..].Trim();
        }
        return "";
    }

    /// <summary>UTF-8 (with or without a byte order mark) or UTF-16 with one; anything else is not text we can read reliably.</summary>
    private static string DecodeText(byte[] bytes)
    {
        try
        {
            return bytes switch
            {
                [0xEF, 0xBB, 0xBF, ..] => new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3),
                [0xFF, 0xFE, ..] => new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2),
                [0xFE, 0xFF, ..] => new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2),
                _ => new UTF8Encoding(false, true).GetString(bytes),
            };
        }
        catch (ArgumentException)
        {
            throw new ConversionException(QrCopy.NotText);
        }
    }

    /// <summary>The PNG of a QR code for <paramref name="text"/>: black on white, quiet zone, error correction M, at least <see cref="MinimumPixels"/> a side.</summary>
    internal static async Task<byte[]> EncodePngAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var matrix = Encode(text);

        var modules = matrix.Width + 2 * QuietZone;
        var scale = (MinimumPixels + modules - 1) / modules; // whole pixels per module keeps the edges crisp
        var size = modules * scale;
        var bgra = new byte[checked(size * size * 4)];
        Array.Fill(bgra, (byte)0xFF);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y] != 1) continue;
                for (var dy = 0; dy < scale; dy++)
                {
                    var start = ((QuietZone + y) * scale + dy) * size + (QuietZone + x) * scale;
                    for (var dx = 0; dx < scale; dx++)
                        bgra.AsSpan((start + dx) * 4, 3).Clear(); // blue, green, red to 0; alpha stays 255
                }
            }
        }

        return await ImageCodec.EncodeAsync(new Raster(size, size, bgra), ImageFormat.Png, 1, null, null, ct).ConfigureAwait(false);
    }

    private static ByteMatrix Encode(string text)
    {
        var hints = new Dictionary<EncodeHintType, object>();
        // The encoder's default is Latin-1; only text beyond ASCII is declared UTF-8, so plain links stay readable by every reader.
        if (text.Any(c => c > 0x7F)) hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        try
        {
            return ZXing.QrCode.Internal.Encoder.encode(text, ErrorCorrectionLevel.M, hints).Matrix;
        }
        catch (WriterException)
        {
            throw new ConversionException(QrCopy.TooLong(Encoding.UTF8.GetByteCount(text)));
        }
    }
}
