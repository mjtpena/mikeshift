using System.Collections.Concurrent;
using System.Globalization;

namespace MikeShift.Core.Documents;

/// <summary>
/// Output formats of the Document category. (Named <c>DocFormat</c>, not <c>DocumentFormat</c>, because
/// <c>DocumentFormat.OpenXml</c> is a namespace and a type of that name would shadow it.)
/// </summary>
public enum DocFormat
{
    Pdf,
    Docx,
    Rtf,
    Txt,
    Html,
    Odt,
}

/// <summary>
/// Document conversions via the DocModel pivot.
/// <para>Returns the path written via <c>Naming.CommitAsync</c>; throws <see cref="ConversionException"/> for expected failures.</para>
/// <para>Word is optional and opt-in (<see cref="Capabilities.WordCom"/>: installed and allowed by the user's setting or <c>--use-word</c>).
/// With it, legacy <c>.doc</c> is converted by Word, and DOCX to PDF uses Word's own exporter for fidelity (falling back to the pivot
/// path if Word fails). Without it Word is never started, DOCX to PDF uses the built-in writer, and <c>.doc</c> is refused.
/// Everything else uses the pivot model.</para>
/// </summary>
public static class DocumentConverter
{
    /// <summary>How to allow Word, for messages: the app setting and the CLI flag.</summary>
    public const string WordOffHint = "Turn on Microsoft Word in Settings (command line: --use-word).";

    /// <summary>Test hook: decides A4 (true) or Letter (false). Defaults to <c>RegionInfo.CurrentRegion.IsMetric</c>.</summary>
    internal static Func<bool> UseA4 { get; set; } = () => RegionInfo.CurrentRegion.IsMetric;

    /// <summary>The output extension, with dot, for a format.</summary>
    public static string ExtensionFor(DocFormat format) => format switch
    {
        DocFormat.Pdf => ".pdf",
        DocFormat.Docx => ".docx",
        DocFormat.Rtf => ".rtf",
        DocFormat.Txt => ".txt",
        DocFormat.Html => ".html",
        DocFormat.Odt => ".odt",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Converts <paramref name="input"/> to <paramref name="format"/>; output <c>name.ext</c>.
    /// <paramref name="caps"/> tells the converter whether it may use Word (<see cref="Capabilities.WordCom"/>: legacy .doc, high-fidelity PDF).
    /// </summary>
    public static async Task<string> ToFormatAsync(string input, DocFormat format, Capabilities caps, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(input))
            throw new ConversionException("The file no longer exists.");

        var ext = Kind.ExtensionOf(input);
        var spec = Naming.Plan(input, ExtensionFor(format));

        if (ext == "doc")
        {
            if (!caps.WordCom)
                throw new ConversionException(caps.WordInstalled
                    ? "Legacy .doc files need Microsoft Word, and MikeShift is not allowed to use it. " + WordOffHint
                    : "Legacy .doc files need Microsoft Word to be installed.");
            // Word would happily "open" random bytes as text and hand back a garbage document; only real Word files go through.
            if (!LooksLikeWordFile(input))
                throw new ConversionException("This isn't a valid Word document (.doc); it may be damaged.");
            return await ViaWordAsync(input, format, spec, ct).ConfigureAwait(false);
        }

        if (ext is "docx" or "odt" && !DocReaders.StartsWithPk(input))
            throw new ConversionException($"This isn't a valid {ext.ToUpperInvariant()} file (it may be password-protected or damaged).");

        if (caps.WordCom && ext == "docx" && format == DocFormat.Pdf)
        {
            try
            {
                return await ViaWordAsync(input, format, spec, ct).ConfigureAwait(false);
            }
            catch (ConversionException)
            {
                // Word failed (busy, repair prompt, ...): the pivot path below still works for DOCX.
                ct.ThrowIfCancellationRequested();
            }
        }

        var model = await Task.Run(() => DocReaders.Read(input, ct), ct).ConfigureAwait(false);
        if (model.HasPictures && format != DocFormat.Txt)
            throw new ConversionException("This document contains pictures, so it can only be converted to TXT.");

        var title = Path.GetFileNameWithoutExtension(input);
        var a4 = UseA4();
        var result = await Naming.CommitStreamAsync(spec,
            (stream, token) => Task.Run(() => Write(model, format, stream, title, a4, token), token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>
    /// What Word itself accepts under a <c>.doc</c> name: an OLE2 compound file (the real thing), a ZIP package (a DOCX renamed), RTF, or
    /// plain text (no NUL bytes). Anything else (random or truncated-to-nothing data) is refused before Word is started.
    /// </summary>
    private static bool LooksLikeWordFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[4096];
            var n = fs.Read(head);
            if (n == 0) return false;
            head = head[..n];
            ReadOnlySpan<byte> ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
            if (head.StartsWith(ole)) return true;
            if (head.StartsWith("PK"u8) || head.StartsWith("{\\rtf"u8)) return true;
            return !head.Contains((byte)0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // let Word report the real problem
        }
    }

    private static async Task<string> ViaWordAsync(string input, DocFormat format, OutputSpec spec, CancellationToken ct)
    {
        var result = await Naming.CommitAsync(spec,
            (tmp, token) => WordInterop.ConvertAsync(input, format, tmp, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>Writes <paramref name="model"/> in <paramref name="format"/> to <paramref name="stream"/>.</summary>
    public static void Write(DocModel model, DocFormat format, Stream stream, string title, bool a4, CancellationToken ct = default)
    {
        switch (format)
        {
            case DocFormat.Pdf: PdfDocWriter.Write(model, stream, a4, title, ct); break;
            case DocFormat.Docx: DocxWriter.Write(model, stream, a4); break;
            case DocFormat.Rtf: RtfWriter.Write(model, stream, a4); break;
            case DocFormat.Txt: TxtWriter.Write(model, stream); break;
            case DocFormat.Html: HtmlWriter.Write(model, stream, title); break;
            case DocFormat.Odt: OdtWriter.Write(model, stream, a4, title); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }
}

/// <summary>
/// Selection-level document checks used by <c>DocumentActions</c> to hide tiles. Verdicts are cached per (path, size, mtime);
/// files that can't be read (or are cloud placeholders, which must not be hydrated) never hide a tile.
/// </summary>
public static class DocumentProbe
{
    private sealed record Info(bool ValidPackage, bool HasPictures);

    private static readonly ConcurrentDictionary<(string Path, long Length, long Ticks), Info?> s_cache =
        new(new KeyComparer());

    /// <summary>Test hook: reads file attributes (lets tests simulate cloud placeholders).</summary>
    internal static Func<string, FileAttributes> GetAttributes { get; set; } = File.GetAttributes;

    /// <summary>Forgets cached verdicts.</summary>
    public static void ClearCache() => s_cache.Clear();

    /// <summary>
    /// True if converting every file in <paramref name="paths"/> to <paramref name="target"/> is worth offering:
    /// files with embedded pictures allow TXT only; legacy .doc requires <see cref="Capabilities.WordCom"/>;
    /// DOCX/ODT must start with the <c>PK</c> signature. Files already in the target format are ignored (the action skips them).
    /// Must be cheap (called while the wheel is populated).
    /// </summary>
    public static bool CanConvertTo(IReadOnlyList<string> paths, DocFormat target, Capabilities caps)
    {
        foreach (var path in paths)
        {
            var ext = Kind.ExtensionOf(path);
            if (IsTargetExtension(ext, target)) continue;

            if (ext == "doc")
            {
                if (!caps.WordCom) return false;
                continue;
            }

            if (GetInfo(path, ext) is not { } info) continue; // unreadable or placeholder: don't hide on a guess
            if (!info.ValidPackage) return false;
            if (info.HasPictures && target != DocFormat.Txt) return false;
        }
        return true;
    }

    private static bool IsTargetExtension(string ext, DocFormat target) => target switch
    {
        DocFormat.Pdf => ext == "pdf",
        DocFormat.Docx => ext == "docx",
        DocFormat.Rtf => ext == "rtf",
        DocFormat.Txt => ext == "txt",
        DocFormat.Html => ext is "html" or "htm",
        DocFormat.Odt => ext == "odt",
        _ => false,
    };

    private static Info? GetInfo(string path, string ext)
    {
        try
        {
            var attrs = GetAttributes(path);
            if ((attrs & (Kind.RecallOnDataAccess | Kind.RecallOnOpen)) != 0) return null;
            var fi = new FileInfo(path);
            var key = (fi.FullName, fi.Length, fi.LastWriteTimeUtc.Ticks);
            if (s_cache.TryGetValue(key, out var cached)) return cached;

            var info = Compute(path, ext);
            if (s_cache.Count > 2000) s_cache.Clear();
            s_cache[key] = info;
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Info Compute(string path, string ext)
    {
        switch (ext)
        {
            case "docx" or "odt":
                if (!DocReaders.StartsWithPk(path)) return new Info(false, false);
                try { return new Info(true, PictureScan.ZipHasPictures(path, docx: ext == "docx")); }
                catch (InvalidDataException) { return new Info(true, false); } // damaged zip: the converter reports it
            case "rtf":
                return new Info(true, PictureScan.RtfHasPictures(path));
            case "html" or "htm":
                return new Info(true, PictureScan.HtmlHasPictures(path));
            default:
                return new Info(true, false);
        }
    }

    private sealed class KeyComparer : IEqualityComparer<(string Path, long Length, long Ticks)>
    {
        public bool Equals((string Path, long Length, long Ticks) x, (string Path, long Length, long Ticks) y) =>
            x.Length == y.Length && x.Ticks == y.Ticks && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, long Length, long Ticks) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Path), k.Length, k.Ticks);
    }
}
