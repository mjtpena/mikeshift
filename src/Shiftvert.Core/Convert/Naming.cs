using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Shiftvert.Core;

/// <summary>Where an output should go: <c>{Directory}\{BaseName}{Extension}</c> (or a folder named <c>{BaseName}</c> when <see cref="Extension"/> is empty).</summary>
/// <param name="Directory">The preferred output directory (normally the input's directory).</param>
/// <param name="BaseName">File or folder name without extension, including any suffix (e.g. "photo compressed").</param>
/// <param name="Extension">Extension with leading dot (".png"), or "" for a folder output.</param>
public readonly record struct OutputSpec(string Directory, string BaseName, string Extension)
{
    /// <summary>True if this spec names a folder rather than a file.</summary>
    public bool IsFolder => Extension.Length == 0;
}

/// <summary>The outcome of <see cref="Naming.CommitAsync"/>.</summary>
/// <param name="Path">Final path of the file or folder.</param>
/// <param name="UsedDownloadsFallback">True if the preferred directory was not writable and the output went to Downloads.</param>
public readonly record struct CommitResult(string Path, bool UsedDownloadsFallback);

/// <summary>
/// Output naming and atomic output writing (PLAN §1.1 naming, §3.4 Naming).
/// <list type="bullet">
/// <item><see cref="Output"/> / <see cref="Unique"/> compute names (<c>photo.png</c>, <c>photo 2.png</c>, <c>photo 3.png</c>...).</item>
/// <item><see cref="CommitAsync"/> is the ONLY way converters create outputs: write to a temp name in the target
/// directory, then move to a unique final name without ever overwriting. A failed job never leaves a partial file.</item>
/// </list>
/// </summary>
public static class Naming
{
    private const int MaxUniqueAttempts = 10_000;

    private static readonly AsyncLocal<string?> s_downloadsOverride = new();

    /// <summary>
    /// Test hook: when set (per async flow), replaces the real Downloads folder used for the not-writable fallback.
    /// Tests must set this so they never write into the developer's real Downloads folder.
    /// </summary>
    public static string? DownloadsOverride
    {
        get => s_downloadsOverride.Value;
        set => s_downloadsOverride.Value = value;
    }

    /// <summary>The user's Downloads folder (<c>FOLDERID_Downloads</c>), or <see cref="DownloadsOverride"/> if set.</summary>
    public static string DownloadsDirectory()
    {
        if (s_downloadsOverride.Value is { } o) return o;
        try
        {
            PInvoke.SHGetKnownFolderPath(in PInvoke.FOLDERID_Downloads, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out var handle).ThrowOnFailure();
            using (handle)
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(handle.DangerousGetHandle())
                       ?? throw new InvalidOperationException("No Downloads folder.");
        }
        catch (Exception)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    // ---------------------------------------------------------------- names

    /// <summary>Normalises an extension to ".ext" (lower case); "" stays "" (folder).</summary>
    public static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return "";
        return (ext[0] == '.' ? ext : "." + ext).ToLowerInvariant();
    }

    /// <summary>The spec for "same directory as <paramref name="input"/>, base name + <paramref name="suffix"/>, new extension".</summary>
    /// <param name="input">The source file (or folder: its parent directory and own name are used).</param>
    /// <param name="newExt">New extension, with or without dot. "" means a folder output.</param>
    /// <param name="suffix">Literal text appended to the base name, including its leading space (" compressed", " 50%", " pages").</param>
    public static OutputSpec Plan(string input, string newExt, string suffix = "")
    {
        var full = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(full) ?? Path.GetPathRoot(full) ?? ".";
        var name = Directory.Exists(full) ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
        return new OutputSpec(dir, name + suffix, NormalizeExtension(newExt));
    }

    /// <summary>
    /// The path a conversion of <paramref name="input"/> would be written to right now: same directory,
    /// <c>base + suffix + newExt</c>, uniquified. (<see cref="CommitAsync"/> re-checks at commit time; this is for previews/tests.)
    /// </summary>
    public static string Output(string input, string newExt, string suffix = "")
    {
        var spec = Plan(input, newExt, suffix);
        return Unique(spec.Directory, spec.BaseName, spec.Extension);
    }

    /// <summary>
    /// The first free name in <paramref name="dir"/>: <c>base.ext</c>, then <c>base 2.ext</c>, <c>base 3.ext</c>...
    /// An empty <paramref name="ext"/> is the folder variant (<c>name pages</c>, <c>name pages 2</c>...).
    /// "Free" means neither a file nor a folder of that name exists.
    /// </summary>
    public static string Unique(string dir, string baseName, string ext)
    {
        ext = NormalizeExtension(ext);
        for (var n = 1; n <= MaxUniqueAttempts; n++)
        {
            var candidate = Path.Combine(dir, n == 1 ? baseName + ext : $"{baseName} {n}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new ConversionException("Could not find a free file name in " + dir + ".");
    }

    // ---------------------------------------------------------------- safe write

    /// <summary>
    /// Writes an output atomically and returns its final location.
    /// <para>File outputs: <paramref name="write"/> receives a temp path (does not exist yet) in the target directory
    /// and must create the file there. Folder outputs (<c>spec.Extension == ""</c>): <paramref name="write"/> receives a
    /// temp directory (already created) and must create the content inside it; its final name is
    /// <c>{BaseName}</c>, <c>{BaseName} 2</c>...</para>
    /// <para>If the target directory is not writable the output goes to <see cref="DownloadsDirectory"/> instead
    /// (<see cref="CommitResult.UsedDownloadsFallback"/> = true). The final move never overwrites: on a name race
    /// the next free name is used. On any failure or cancellation the temp item is removed.</para>
    /// </summary>
    public static async Task<CommitResult> CommitAsync(
        OutputSpec spec, Func<string, CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();

        var fallback = false;
        var dir = spec.Directory;
        var temp = TryReserveTemp(dir, spec);
        if (temp is null)
        {
            dir = DownloadsDirectory();
            fallback = true;
            try { Directory.CreateDirectory(dir); } catch (Exception) { /* reported below */ }
            temp = TryReserveTemp(dir, spec)
                   ?? throw new ConversionException(
                       $"Can't save next to the original, and Downloads isn't writable either ({dir}).");
        }

        try
        {
            await write(temp, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (spec.IsFolder ? !Directory.Exists(temp) : !File.Exists(temp))
                throw new ConversionException("The converter did not produce an output.");

            var final = MoveWithoutOverwrite(temp, dir, spec);
            temp = null;
            return new CommitResult(final, fallback);
        }
        finally
        {
            if (temp is not null) TryDelete(temp, spec.IsFolder);
        }
    }

    /// <summary>
    /// Convenience over <see cref="CommitAsync"/> for streams: <paramref name="write"/> receives a writable stream on the temp file.
    /// </summary>
    public static Task<CommitResult> CommitStreamAsync(
        OutputSpec spec, Func<Stream, CancellationToken, Task> write, CancellationToken ct) =>
        CommitAsync(spec, async (path, token) =>
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await write(fs, token).ConfigureAwait(false);
        }, ct);

    private static string TempName(OutputSpec spec) =>
        $".{(spec.BaseName.Length > 40 ? spec.BaseName[..40] : spec.BaseName)}.{Guid.NewGuid():N}.shiftvert-tmp";

    /// <summary>Creates (and for files, removes again) a temp item in <paramref name="dir"/> to prove writability. Null = not writable.</summary>
    private static string? TryReserveTemp(string dir, OutputSpec spec)
    {
        var temp = Path.Combine(dir, TempName(spec));
        try
        {
            if (spec.IsFolder)
            {
                Directory.CreateDirectory(temp);
            }
            else
            {
                // Prove we can create files here, then hand the converter a fresh, non-existent name.
                using (new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                File.Delete(temp);
            }
            return temp;
        }
        catch (Exception ex) when (IsNotWritable(ex))
        {
            return null;
        }
    }

    private static bool IsNotWritable(Exception ex) =>
        ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException
        || (ex is IOException io && (io.HResult & 0xFFFF) is 5 /*ACCESS_DENIED*/ or 19 /*WRITE_PROTECT*/ or 32 /*SHARING*/ or 21 /*NOT_READY*/);

    private static string MoveWithoutOverwrite(string temp, string dir, OutputSpec spec)
    {
        for (var attempt = 0; attempt < MaxUniqueAttempts; attempt++)
        {
            var target = Unique(dir, spec.BaseName, spec.Extension);
            try
            {
                if (spec.IsFolder) Directory.Move(temp, target);
                else File.Move(temp, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                // Lost a race for this name; pick the next one.
            }
        }
        throw new ConversionException("Could not find a free file name in " + dir + ".");
    }

    private static void TryDelete(string path, bool folder)
    {
        try
        {
            if (folder) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort; a leftover ".shiftvert-tmp" is harmless.
        }
    }
}
