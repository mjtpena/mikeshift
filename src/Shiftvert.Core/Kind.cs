using System.Collections.Concurrent;
using Shiftvert.Core.Images;
using Shiftvert.Core.Media;

namespace Shiftvert.Core;

/// <summary>
/// Category detection (PLAN §1.2 "Category detection"). <see cref="Of"/> decides, per path, which action list applies.
/// </summary>
/// <remarks>
/// Order of evaluation: directory -> Other; cloud placeholder (extension verdict only, never reads content);
/// <c>%PDF</c> magic (also for misnamed files); extension lists; <c>.ts/.mts/.m2ts</c> need MPEG-TS sync bytes;
/// images must pass <see cref="ImageProbe.CanDecode"/>; audio/video must pass <see cref="MediaProbe.CanOpen"/>.
/// Verdicts are cached per (path, size, mtime) for the life of the process; call <see cref="ClearCache"/> when a
/// drag session ends.
/// </remarks>
public static class Kind
{
    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c>: a OneDrive-style placeholder; reading it would download it.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary><c>FILE_ATTRIBUTE_RECALL_ON_OPEN</c>: also a placeholder; same rule.</summary>
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>Image extensions (lower case, no dot).</summary>
    public static IReadOnlySet<string> ImageExtensions { get; } = Set(
        "png", "jpg", "jpeg", "jpe", "jfif", "heic", "heif", "tif", "tiff", "gif", "webp", "bmp", "dib", "avif", "ico", "jxr", "wdp");

    /// <summary>Video extensions (lower case, no dot). <c>ts</c>, <c>mts</c> and <c>m2ts</c> additionally need MPEG-TS sync bytes.</summary>
    public static IReadOnlySet<string> VideoExtensions { get; } = Set(
        "mp4", "m4v", "mov", "avi", "wmv", "asf", "mkv", "webm", "mpg", "mpeg", "3gp", "3g2", "ts", "mts", "m2ts");

    /// <summary>Audio extensions (lower case, no dot).</summary>
    public static IReadOnlySet<string> AudioExtensions { get; } = Set(
        "mp3", "wav", "m4a", "aac", "flac", "wma", "alac", "aif", "aiff", "aifc");

    /// <summary>Document extensions (lower case, no dot).</summary>
    public static IReadOnlySet<string> DocumentExtensions { get; } = Set(
        "docx", "doc", "rtf", "odt", "html", "htm", "txt", "md", "markdown");

    /// <summary>PDF extension (lower case, no dot). A PDF is only a PDF if the <c>%PDF</c> magic is present.</summary>
    public const string PdfExtension = "pdf";

    private static readonly ConcurrentDictionary<(string Path, long Length, long Ticks), Category> s_cache =
        new(new CacheKeyComparer());

    /// <summary>Forgets all cached verdicts.</summary>
    public static void ClearCache() => s_cache.Clear();

    /// <summary>The lower-case extension of <paramref name="path"/> without the dot ("" if none).</summary>
    public static string ExtensionOf(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length > 1 ? ext[1..].ToLowerInvariant() : "";
    }

    /// <summary>Classifies <paramref name="path"/>. Never throws: an unreadable or vanished path is <see cref="Category.Other"/>.</summary>
    public static Category Of(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0)
                return Category.Other;

            var info = new FileInfo(path);
            var key = (info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
            if (s_cache.TryGetValue(key, out var cached))
                return cached;

            var verdict = Classify(path, attrs);
            s_cache[key] = verdict;
            return verdict;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return Category.Other;
        }
    }

    /// <summary>Selections up to this size are classified exactly, file by file (a misnamed file is always noticed).</summary>
    internal const int ExactSelectionLimit = 16;

    /// <summary>How many files of a large, extension-uniform selection are sniffed to confirm the extensions.</summary>
    private const int SampleSize = 12;

    /// <summary>
    /// The single category shared by every path, or null if they are mixed, include a folder or an unknown/unreadable file.
    /// Up to <see cref="ExactSelectionLimit"/> files every file goes through <see cref="Of"/>. A larger selection whose extensions all
    /// point at one category (the common "500 photos" drop) is confirmed by sniffing a bounded, evenly spread sample of
    /// <see cref="SampleSize"/> files, so the wheel gets its tiles in milliseconds instead of decoding 500 headers; a misnamed
    /// file outside the sample then fails with the converter's own clean error. Any other large selection is sniffed in
    /// full, in parallel, stopping at the first file that disagrees.
    /// </summary>
    public static Category? CommonOf(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0) return null;
        if (paths.Count <= ExactSelectionLimit) return Sequential(paths);

        var hint = ExtensionVerdict(paths[0]);
        if (hint is { } h and not Category.Other && AllShare(paths, h))
        {
            var step = Math.Max(1, paths.Count / SampleSize);
            var confirmed = true;
            for (var i = 0; i < paths.Count && confirmed; i += step)
                confirmed = Of(paths[i]) == h;
            if (confirmed && Of(paths[^1]) == h) return h;
        }
        return Parallel(paths);
    }

    private static Category? Sequential(IReadOnlyList<string> paths)
    {
        Category? common = null;
        foreach (var p in paths)
        {
            var k = Of(p);
            if (k == Category.Other) return null;
            if (common is null) common = k;
            else if (common != k) return null;
        }
        return common;
    }

    private static Category? Parallel(IReadOnlyList<string> paths)
    {
        var first = Of(paths[0]);
        if (first == Category.Other) return null;
        var agree = true;
        System.Threading.Tasks.Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            (p, state) =>
            {
                if (Of(p) != first) { agree = false; state.Stop(); }
            });
        return agree ? first : null;
    }

    /// <summary>True if every path is an existing file (no folder, nothing vanished) whose extension verdict is <paramref name="category"/>.</summary>
    private static bool AllShare(IReadOnlyList<string> paths, Category category)
    {
        foreach (var p in paths)
        {
            if (ExtensionVerdict(p) != category) return false;
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.Directory) != 0) return false;
            }
            catch (Exception) { return false; }
        }
        return true;
    }

    /// <summary>The category an extension implies, or null if the extension alone cannot say (<c>.ts</c> family needs sync bytes).</summary>
    private static Category? ExtensionVerdict(string path)
    {
        var ext = ExtensionOf(path);
        if (ext is "ts" or "mts" or "m2ts") return null;
        return ClassifyByExtensionOnly(ext);
    }

    internal static Category Classify(string path, FileAttributes attrs)
    {
        var ext = ExtensionOf(path);
        var placeholder = (attrs & (RecallOnDataAccess | RecallOnOpen)) != 0;

        if (placeholder)
            return ClassifyByExtensionOnly(ext);

        // PDF by magic: authoritative in both directions (a misnamed PDF is a PDF; a readable ".pdf" without the magic is not).
        if (!IsKnownTextOnlyExtension(ext))
        {
            var magic = StartsWithPdfMagic(path);
            if (magic == true) return Category.Pdf;
            if (ext == PdfExtension) return magic is null ? Category.Pdf : Category.Other; // unreadable: trust the extension
        }

        if (ImageExtensions.Contains(ext))
            return ImageProbe.CanDecode(path) ? Category.Image : Category.Other;

        if (VideoExtensions.Contains(ext))
        {
            if (!PassesTransportStreamCheck(path, ext)) return Category.Other;
            return MediaProbe.CanOpen(path) ? Category.Video : Category.Other;
        }

        if (AudioExtensions.Contains(ext))
            return MediaProbe.CanOpen(path) ? Category.Audio : Category.Other;

        if (DocumentExtensions.Contains(ext))
            return Category.Document;

        return Category.Other;
    }

    /// <summary>Placeholder rule: extension verdict only; ambiguous TypeScript-like extensions stay Other.</summary>
    private static Category ClassifyByExtensionOnly(string ext)
    {
        if (ext == PdfExtension) return Category.Pdf;
        if (ext is "ts" or "mts") return Category.Other; // cannot verify sync bytes without hydrating the file
        if (ImageExtensions.Contains(ext)) return Category.Image;
        if (VideoExtensions.Contains(ext)) return Category.Video;
        if (AudioExtensions.Contains(ext)) return Category.Audio;
        if (DocumentExtensions.Contains(ext)) return Category.Document;
        return Category.Other;
    }

    // Documents we never need to sniff for a PDF header (saves a read for the common text files).
    private static bool IsKnownTextOnlyExtension(string ext) => ext is "txt" or "md" or "markdown" or "html" or "htm";

    /// <summary>True/false for the <c>%PDF</c> magic at offset 0; null if the file could not be read.</summary>
    private static bool? StartsWithPdfMagic(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16);
            Span<byte> buf = stackalloc byte[4];
            var n = fs.Read(buf);
            return n == 4 && buf[0] == (byte)'%' && buf[1] == (byte)'P' && buf[2] == (byte)'D' && buf[3] == (byte)'F';
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool PassesTransportStreamCheck(string path, string ext)
    {
        if (ext is not ("ts" or "mts" or "m2ts")) return true;

        // 188-byte packets (".m2ts" uses 192-byte packets with a 4-byte timestamp prefix, so sync bytes sit at 4 + n*192).
        var (offset, stride) = ext == "m2ts" ? (4, 192) : (0, 188);
        const int maxChecked = 4;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            // Need at least two whole packets; check the first (up to) four sync bytes.
            if (fs.Length < offset + stride + 1) return false;
            for (var i = 0; i < maxChecked; i++)
            {
                var pos = offset + (long)stride * i;
                if (pos >= fs.Length) break;
                fs.Seek(pos, SeekOrigin.Begin);
                if (fs.ReadByte() != 0x47) return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.Ordinal);

    private sealed class CacheKeyComparer : IEqualityComparer<(string Path, long Length, long Ticks)>
    {
        public bool Equals((string Path, long Length, long Ticks) x, (string Path, long Length, long Ticks) y) =>
            x.Length == y.Length && x.Ticks == y.Ticks && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, long Length, long Ticks) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Path), k.Length, k.Ticks);
    }
}
