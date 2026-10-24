using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace MikeShift.Core;

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
/// Output naming and atomic output writing.
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

    private static readonly AsyncLocal<string?> s_outputFolder = new();

    /// <summary>
    /// "Where converted files go" for the actions running in this async flow: when set, <see cref="Plan"/> puts every output in this folder instead
    /// of beside its input. <c>ActionDef.Run</c> sets it from <c>ActionContext.Options.OutputFolder</c> and restores it afterwards, so converters
    /// stay unaware of it. A folder that does not exist or is not writable sends the output to Downloads (<see cref="CommitResult.UsedDownloadsFallback"/>).
    /// </summary>
    public static string? OutputFolder
    {
        get => s_outputFolder.Value;
        set => s_outputFolder.Value = value;
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

    /// <summary>
    /// The spec for "same directory as <paramref name="input"/> (or <see cref="OutputFolder"/> when one is set), base name + <paramref name="suffix"/>, new extension".
    /// </summary>
    /// <param name="input">The source file (or folder: its parent directory and own name are used).</param>
    /// <param name="newExt">New extension, with or without dot. "" means a folder output.</param>
    /// <param name="suffix">Literal text appended to the base name, including its leading space (" compressed", " 50%", " pages").</param>
    public static OutputSpec Plan(string input, string newExt, string suffix = "")
    {
        var full = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = OutputFolder ?? Path.GetDirectoryName(full) ?? Path.GetPathRoot(full) ?? ".";
        var name = Directory.Exists(full) ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
        return new OutputSpec(dir, name + suffix, NormalizeExtension(newExt));
    }

    /// <summary>
    /// The spec for an output with a name of its own ("Merged.pdf", "Archive.zip") that belongs with <paramref name="firstInput"/>: its folder, or
    /// <see cref="OutputFolder"/> when one is set.
    /// </summary>
    public static OutputSpec PlanNamed(string firstInput, string baseName, string newExt)
    {
        var full = Path.GetFullPath(firstInput).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = OutputFolder ?? Path.GetDirectoryName(full) ?? Path.GetPathRoot(full) ?? ".";
        return new OutputSpec(dir, baseName, NormalizeExtension(newExt));
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
    /// <para>File outputs: <paramref name="write"/> receives a temp path (does not exist yet) inside a hidden temp folder in the
    /// target directory and must create the file there. Folder outputs (<c>spec.Extension == ""</c>): <paramref name="write"/> receives a
    /// temp directory (already created, hidden until committed) and must create the content inside it; its final name is
    /// <c>{BaseName}</c>, <c>{BaseName} 2</c>...</para>
    /// <para>If the target directory is not writable the output goes to <see cref="DownloadsDirectory"/> instead
    /// (<see cref="CommitResult.UsedDownloadsFallback"/> = true). Transient sharing violations (antivirus, cloud sync) are
    /// retried when reserving the temp and on the final move, which also retries "access denied" (a scanner holding a just-written file). The final move never overwrites: on a name race
    /// the next free name is used. On any failure or cancellation the temp item is removed.</para>
    /// </summary>
    public static async Task<CommitResult> CommitAsync(
        OutputSpec spec, Func<string, CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();

        var fallback = false;
        var dir = spec.Directory;
        // A folder the user chose that has since gone (a removed drive, a deleted "Converted" folder) is not recreated behind their back: Downloads
        // instead, as for a folder that cannot be written to. (Any other missing directory is still created, as always.)
        var temp = IsMissingChosenFolder(dir) ? null : await TryReserveTempAsync(dir, spec, ct).ConfigureAwait(false);
        if (temp is null)
        {
            dir = DownloadsDirectory();
            fallback = true;
            try { Directory.CreateDirectory(dir); } catch (Exception) { /* reported below */ }
            temp = await TryReserveTempAsync(dir, spec, ct).ConfigureAwait(false)
                   ?? throw new ConversionException(
                       $"Can't save next to the original, and Downloads isn't writable either ({dir}).");
        }

        var (container, path) = temp.Value;
        try
        {
            await write(path, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (spec.IsFolder ? !Directory.Exists(path) : !File.Exists(path))
                throw new ConversionException("The converter did not produce an output.");

            var final = await MoveWithoutOverwriteAsync(path, dir, spec, ct).ConfigureAwait(false);
            if (spec.IsFolder) container = null; // the container IS the moved folder
            return new CommitResult(final, fallback);
        }
        finally
        {
            if (container is not null) TryDelete(container, folder: true);
        }
    }

    private static bool IsMissingChosenFolder(string dir) =>
        OutputFolder is { } chosen
        && string.Equals(Path.TrimEndingDirectorySeparator(chosen), Path.TrimEndingDirectorySeparator(dir), StringComparison.OrdinalIgnoreCase)
        && !Directory.Exists(dir);

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
        $".{(spec.BaseName.Length > 40 ? spec.BaseName[..40] : spec.BaseName)}.{Guid.NewGuid():N}.mikeshift-tmp";

    private static readonly int[] s_retryDelaysMs = [100, 250, 500, 1000];

    private static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException io && (io.HResult & 0xFFFF) == 5 /*ACCESS_DENIED*/);

    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 /*SHARING*/ or 33 /*LOCK*/;

    /// <summary>
    /// Runs <paramref name="op"/>, retrying transient sharing violations (antivirus, indexers, cloud sync) after 100/250/500/1000 ms.
    /// <para>With <paramref name="retryAccessDenied"/> an "access denied" is retried too. Renaming a folder (or a file) that something has
    /// open without delete sharing, which is what a scanner does to a file that was written a moment ago, does not fail with a sharing
    /// violation but with ERROR_ACCESS_DENIED, which <see cref="Directory.Move(string, string)"/> reports as a plain <see cref="IOException"/>
    /// ("Access to the path ... is denied.") and the file APIs as <see cref="UnauthorizedAccessException"/>. Only the final move asks
    /// for this: the folder was created and written a moment earlier, so it is writable and a denial there can only be a passing one. When
    /// the retries run out the original exception propagates, as before.</para>
    /// </summary>
    private static async Task<T> RetryOnSharingViolationAsync<T>(Func<T> op, CancellationToken ct, bool retryAccessDenied = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return op(); }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < s_retryDelaysMs.Length)
            {
                await Task.Delay(s_retryDelaysMs[attempt], ct).ConfigureAwait(false);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                throw new ConversionException("The output couldn't be saved because another program is using the file or folder.", ex);
            }
            catch (Exception ex) when (retryAccessDenied && IsAccessDenied(ex) && attempt < s_retryDelaysMs.Length)
            {
                await Task.Delay(s_retryDelaysMs[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reserves the temp location in <paramref name="dir"/> by creating a hidden container folder
    /// (<c>.name.guid.mikeshift-tmp</c>) there, so a crash leaves nothing visible. Folder outputs use the container itself as the
    /// temp folder; file outputs get a not-yet-existing file path inside it (writers may use any file mode, which a pre-created
    /// hidden file would break). Null = the directory is not writable. There is no separate create/delete probe.
    /// </summary>
    private static async Task<(string Container, string Path)?> TryReserveTempAsync(string dir, OutputSpec spec, CancellationToken ct)
    {
        var container = Path.Combine(dir, TempName(spec));
        try
        {
            await RetryOnSharingViolationAsync(() =>
            {
                Directory.CreateDirectory(container);
                File.SetAttributes(container, FileAttributes.Directory | FileAttributes.Hidden);
                return true;
            }, ct).ConfigureAwait(false);
            return (container, spec.IsFolder ? container : Path.Combine(container, "output.mikeshift-tmp"));
        }
        catch (Exception ex) when (IsNotWritable(ex))
        {
            TryDelete(container, folder: true);
            return null;
        }
    }

    private static bool IsNotWritable(Exception ex) =>
        ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException
        || (ex is IOException io && (io.HResult & 0xFFFF) is 5 /*ACCESS_DENIED*/ or 19 /*WRITE_PROTECT*/ or 21 /*NOT_READY*/);

    private static async Task<string> MoveWithoutOverwriteAsync(string temp, string dir, OutputSpec spec, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxUniqueAttempts; attempt++)
        {
            var target = Unique(dir, spec.BaseName, spec.Extension);
            try
            {
                await RetryOnSharingViolationAsync(() =>
                {
                    if (spec.IsFolder) Directory.Move(temp, target);
                    else File.Move(temp, target, overwrite: false);
                    return true;
                }, ct, retryAccessDenied: true).ConfigureAwait(false);
                if (spec.IsFolder)
                {
                    try { File.SetAttributes(target, FileAttributes.Directory); } catch (Exception) { /* stays hidden: harmless */ }
                }
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
            // Best effort; a leftover ".mikeshift-tmp" is harmless.
        }
    }
}
