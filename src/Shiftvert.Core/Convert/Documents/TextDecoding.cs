using System.Text;
using System.Text.RegularExpressions;

namespace Shiftvert.Core.Documents;

/// <summary>
/// Encoding detection for plain text and HTML (PLAN §3.4): BOM, then strict UTF-8, then a heuristic
/// (runs of at least three bytes in 0xC0-0xFA are Hebrew in Windows-1255, otherwise Windows-1252).
/// </summary>
public static partial class TextDecoding
{
    static TextDecoding() => EnsureProviders();

    /// <summary>Registers the legacy code pages (Windows-125x, Shift-JIS...) with .NET. Idempotent.</summary>
    public static void EnsureProviders() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The encoding <see cref="Decode"/> would use for <paramref name="bytes"/>, and the BOM length to skip.</summary>
    public static (Encoding Encoding, int BomLength) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false), 3);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, false), 4);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, false), 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, false), 2);

        if (IsValidUtf8(bytes))
            return (new UTF8Encoding(false), 0);

        return (LegacyEncoding(bytes), 0);
    }

    /// <summary>Decodes text bytes using <see cref="Detect"/>.</summary>
    public static string Decode(byte[] bytes)
    {
        EnsureProviders();
        var (enc, skip) = Detect(bytes);
        return enc.GetString(bytes, skip, bytes.Length - skip);
    }

    /// <summary>
    /// Decodes an HTML file: BOM, then a <c>charset</c> declared in the first 2 KB, then the plain-text rules.
    /// </summary>
    public static string DecodeHtml(byte[] bytes)
    {
        EnsureProviders();
        var (enc, skip) = Detect(bytes);
        // No BOM and not valid UTF-8: a charset declared by the page beats the byte heuristic.
        if (skip == 0 && !IsValidUtf8(bytes) && DeclaredCharset(bytes) is { } declared)
        {
            try { enc = Encoding.GetEncoding(declared); }
            catch (ArgumentException) { /* unknown label: keep the heuristic */ }
        }
        return enc.GetString(bytes, skip, bytes.Length - skip);
    }

    /// <summary>The Windows-1255 / Windows-1252 heuristic for bytes that are not valid UTF-8.</summary>
    public static Encoding LegacyEncoding(ReadOnlySpan<byte> bytes)
    {
        EnsureProviders();
        var run = 0;
        foreach (var b in bytes)
        {
            if (b is >= 0xC0 and <= 0xFA)
            {
                if (++run >= 3) return Encoding.GetEncoding(1255);
            }
            else
            {
                run = 0;
            }
        }
        return Encoding.GetEncoding(1252);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes) =>
        System.Text.Unicode.Utf8.IsValid(bytes);

    private static string? DeclaredCharset(byte[] bytes)
    {
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var m = CharsetRegex().Match(head);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([A-Za-z0-9_\-:.]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CharsetRegex();
}
