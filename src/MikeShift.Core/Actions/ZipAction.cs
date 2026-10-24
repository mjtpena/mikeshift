using System.IO.Compression;

namespace MikeShift.Core.Actions;

/// <summary>
/// The Zip tool. Available for every selection and always the last tile.
/// <list type="bullet">
/// <item>One item: <c>name.zip</c> (file: name without extension; folder: the folder name) beside the item.</item>
/// <item>Several items: <c>Archive.zip</c> beside the first item. Folders are stored under their own name.</item>
/// <item>Entry timestamps are the files' UTC last-write times (DOS time cannot carry a zone, so UTC is stored as the clock value).</item>
/// <item>Named pipes, sockets, devices and WSL special files are refused; name-surrogate directories (symlinks, junctions)
/// inside a folder are not followed, so link loops cannot recurse forever. Other reparse points (OneDrive/Dropbox cloud
/// placeholders, dedup, ...) are ordinary folders and are included.</item>
/// </list>
/// </summary>
public static class ZipAction
{
    /// <summary>Title of the action (CLI name).</summary>
    public const string Title = "Zip";

    private static readonly DateTimeOffset MinZipTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaxZipTime = new(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);

    // Already-compressed payloads gain nothing from deflate.
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "gz", "bz2", "xz", "jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "avif",
        "mp3", "m4a", "aac", "flac", "mp4", "m4v", "mov", "mkv", "webm", "wmv", "docx", "xlsx", "pptx", "odt", "pdf",
    };

    /// <summary>The Zip action definition: Tool page, always applicable, never hidden.</summary>
    public static ActionDef Def { get; } = ActionDef.SingleOutput(
        Title, WheelPage.Tool, (paths, _, ct) => CreateAsync(paths, ct), glyph: Glyphs.Zip);

    /// <summary>Creates the archive and returns its path. Throws <see cref="ConversionException"/> for refusals and read errors.</summary>
    public static async Task<string> CreateAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) throw new ConversionException("Nothing to zip.");

        foreach (var p in paths)
        {
            if (SpecialFiles.IsSpecial(p))
                throw new ConversionException($"\"{DisplayName(p)}\" is a pipe, socket or device and can't be zipped.");
            if (!File.Exists(p) && !Directory.Exists(p))
                throw new ConversionException($"\"{DisplayName(p)}\" no longer exists.");
        }

        var first = Path.GetFullPath(paths[0]);
        var spec = paths.Count == 1
            ? Naming.Plan(first, ".zip")
            : Naming.PlanNamed(first, "Archive", ".zip");

        var result = await Naming.CommitAsync(spec, (temp, token) => WriteArchiveAsync(paths, temp, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    private static async Task WriteArchiveAsync(IReadOnlyList<string> paths, string tempPath, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempFull = Path.GetFullPath(tempPath);

        await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(full))
            {
                var root = UniqueEntryName(names, Path.GetFileName(full), isDirectory: true);
                await AddDirectoryAsync(zip, new DirectoryInfo(full), root, tempFull, visited, ct).ConfigureAwait(false);
            }
            else
            {
                var entryName = UniqueEntryName(names, Path.GetFileName(full), isDirectory: false);
                await AddFileAsync(zip, new FileInfo(full), entryName, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task AddDirectoryAsync(ZipArchive zip, DirectoryInfo dir, string entryPrefix, string tempFull, HashSet<string> visited, CancellationToken ct)
    {
        // Loop protection: each real directory is archived once per run (symlinks/junctions are also skipped below).
        var final = FinalPath(dir);
        if (!visited.Add(final)) return;

        // Entry for the directory itself keeps empty folders and their timestamps.
        var dirEntry = zip.CreateEntry(entryPrefix + "/");
        dirEntry.LastWriteTime = ClampTime(dir.LastWriteTimeUtc);

        IEnumerable<FileSystemInfo> children;
        try { children = dir.EnumerateFileSystemInfos().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConversionException($"Couldn't read the folder \"{dir.Name}\": {ex.Message}", ex);
        }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var childEntry = entryPrefix + "/" + child.Name;

            if ((child.Attributes & FileAttributes.Directory) != 0)
            {
                // The archive's own (hidden) temp container may sit inside the folder being zipped.
                if (string.Equals(child.FullName, Path.GetDirectoryName(tempFull), StringComparison.OrdinalIgnoreCase)) continue;
                // Never follow symlinks/junctions below the selected root: that is how link loops happen.
                // Cloud placeholders (OneDrive, Dropbox, ...) are reparse points too but are real folders: keep them.
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 && IsLink(child)) continue;
                await AddDirectoryAsync(zip, (DirectoryInfo)child, childEntry, tempFull, visited, ct).ConfigureAwait(false);
            }
            else
            {
                if (string.Equals(child.FullName, tempFull, StringComparison.OrdinalIgnoreCase)) continue;
                if (SpecialFiles.IsSpecial(child.FullName)) continue;
                await AddFileAsync(zip, (FileInfo)child, childEntry, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsLink(FileSystemInfo child)
    {
        var tag = SpecialFiles.ReparseTag(child.FullName);
        return SpecialFiles.IsNameSurrogate(tag) || (tag == 0 && child.LinkTarget is not null);
    }

    private static string FinalPath(DirectoryInfo dir)
    {
        try { return dir.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? dir.FullName; }
        catch (IOException) { return dir.FullName; }
    }

    private static async Task AddFileAsync(ZipArchive zip, FileInfo file, string entryName, CancellationToken ct)
    {
        var level = StoredExtensions.Contains(Kind.ExtensionOf(file.Name)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
        FileStream input;
        try
        {
            input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConversionException($"Couldn't read \"{file.Name}\": {ex.Message}", ex);
        }

        await using (input.ConfigureAwait(false))
        {
            var entry = zip.CreateEntry(entryName, level);
            entry.LastWriteTime = ClampTime(file.LastWriteTimeUtc);
            var output = entry.Open();
            await using (output.ConfigureAwait(false))
            {
                try { await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false); }
                catch (IOException ex) { throw new ConversionException($"Couldn't read \"{file.Name}\": {ex.Message}", ex); }
            }
        }
    }

    private static DateTimeOffset ClampTime(DateTime utc)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return t < MinZipTime ? MinZipTime : t > MaxZipTime ? MaxZipTime : t;
    }

    /// <summary>Makes a top-level entry name unique within the archive: <c>a.txt</c>, <c>a 2.txt</c>, <c>a 3.txt</c>; folders <c>dir</c>, <c>dir 2</c>.</summary>
    private static string UniqueEntryName(HashSet<string> used, string name, bool isDirectory)
    {
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{stem} {n}{ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static string DisplayName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : path;
}
