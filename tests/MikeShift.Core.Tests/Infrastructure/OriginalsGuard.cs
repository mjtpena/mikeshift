using System.Security.Cryptography;
using System.Text;
using Xunit.Sdk;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Proves a conversion never modified, moved or deleted its inputs (PLAN: "originals never touched").
/// Hashes every input (files by content; folders recursively by relative name + content) when created and asserts
/// equality in <see cref="AssertUnchanged"/> / on dispose.
/// <code>
/// using var guard = OriginalsGuard.Watch(input);
/// await Converter.RunAsync(input, ...);
/// // disposing asserts the input is byte-identical
/// </code>
/// </summary>
public sealed class OriginalsGuard : IDisposable
{
    private readonly Dictionary<string, string> _before;
    private bool _checked;

    private OriginalsGuard(Dictionary<string, string> before) => _before = before;

    /// <summary>Takes the "before" snapshot of <paramref name="paths"/> (files and/or folders).</summary>
    public static OriginalsGuard Watch(params string[] paths) =>
        new(paths.ToDictionary(p => p, Snapshot, StringComparer.OrdinalIgnoreCase));

    /// <summary>Throws an xunit failure if any watched input changed, vanished or was replaced.</summary>
    public void AssertUnchanged()
    {
        _checked = true;
        foreach (var (path, before) in _before)
        {
            var after = Snapshot(path);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                throw new XunitException($"Original was modified, moved or deleted: {path}");
        }
    }

    public void Dispose()
    {
        if (!_checked) AssertUnchanged();
    }

    private static string Snapshot(string path)
    {
        if (File.Exists(path)) return "F:" + HashFile(path);
        if (Directory.Exists(path))
        {
            var sb = new StringBuilder("D:");
            foreach (var f in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(System.IO.Path.GetRelativePath(path, f)).Append('|');
                if (File.Exists(f)) sb.Append(HashFile(f));
                sb.Append('\n');
            }
            return sb.ToString();
        }
        return "MISSING";
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs));
    }
}
