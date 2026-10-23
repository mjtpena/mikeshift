using System.Security.AccessControl;
using System.Security.Principal;
using MikeShift.Core;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// A unique scratch directory under the system temp folder, deleted on dispose. Also redirects the Downloads fallback
/// (<see cref="Naming.DownloadsOverride"/>) into a sub-folder so tests never touch the real Downloads folder.
/// <code>using var tmp = new TempDir(); var f = tmp.WriteFile("a.txt", "hi");</code>
/// </summary>
public sealed class TempDir : IDisposable
{
    private readonly string? _previousDownloads;

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mikeshift-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Downloads = System.IO.Path.Combine(Path, "__downloads");
        Directory.CreateDirectory(Downloads);
        _previousDownloads = Naming.DownloadsOverride;
        Naming.DownloadsOverride = Downloads;
    }

    /// <summary>Absolute path of the directory.</summary>
    public string Path { get; }

    /// <summary>The fake Downloads folder used by the fallback while this <see cref="TempDir"/> is alive.</summary>
    public string Downloads { get; }

    /// <summary>Combines <paramref name="parts"/> onto <see cref="Path"/>.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Creates a sub-directory (and parents) and returns its path.</summary>
    public string CreateDir(string relative)
    {
        var p = Combine(relative);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Writes bytes to <paramref name="relative"/> (creating directories) and returns the full path.</summary>
    public string WriteFile(string relative, byte[] content)
    {
        var p = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, content);
        return p;
    }

    /// <summary>Writes UTF-8 text (no BOM) to <paramref name="relative"/> and returns the full path.</summary>
    public string WriteFile(string relative, string content) =>
        WriteFile(relative, new System.Text.UTF8Encoding(false).GetBytes(content));

    /// <summary>
    /// Makes <paramref name="directory"/> refuse file and sub-directory creation for the current user (deny ACE) until the
    /// returned object is disposed. Used to test the "not writable, fall back to Downloads" path.
    /// </summary>
    public static IDisposable DenyWrites(string directory) => new DenyWritesScope(directory);

    public void Dispose()
    {
        Naming.DownloadsOverride = _previousDownloads;
        try
        {
            ClearReadOnly(new DirectoryInfo(Path));
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception)
        {
            // Best effort: a leftover temp folder must not fail a test run.
        }
    }

    private static void ClearReadOnly(DirectoryInfo dir)
    {
        foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
    }

    private sealed class DenyWritesScope : IDisposable
    {
        private readonly DirectoryInfo _dir;
        private readonly FileSystemAccessRule _rule;

        public DenyWritesScope(string directory)
        {
            _dir = new DirectoryInfo(directory);
            var user = WindowsIdentity.GetCurrent().User!;
            _rule = new FileSystemAccessRule(
                user,
                FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.WriteData | FileSystemRights.AppendData,
                AccessControlType.Deny);
            var sec = _dir.GetAccessControl();
            sec.AddAccessRule(_rule);
            _dir.SetAccessControl(sec);
        }

        public void Dispose()
        {
            var sec = _dir.GetAccessControl();
            sec.RemoveAccessRule(_rule);
            _dir.SetAccessControl(sec);
        }
    }
}
