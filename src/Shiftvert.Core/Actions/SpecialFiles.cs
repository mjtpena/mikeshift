using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Shiftvert.Core.Actions;

/// <summary>Detects file-system objects that must never be read as files: devices, named pipes, sockets, WSL fifos.</summary>
internal static class SpecialFiles
{
    private const uint ReparseTagAfUnix = 0x80000023;
    private const uint ReparseTagLxFifo = 0x80000024;
    private const uint ReparseTagLxChr = 0x80000025;
    private const uint ReparseTagLxBlk = 0x80000026;

    private const uint ReparseTagNameSurrogateBit = 0x20000000;

    /// <summary>
    /// True if a reparse tag denotes a name-surrogate link (symlink 0xA000000C, junction 0xA0000003, ...): another name for a
    /// different path. Cloud placeholders (OneDrive 0x9000701A, ...) and other tags are not.
    /// </summary>
    public static bool IsNameSurrogate(uint tag) => (tag & ReparseTagNameSurrogateBit) != 0;

    /// <summary>True for Win32 device-namespace paths such as <c>\\.\pipe\x</c> or <c>\\.\PhysicalDrive0</c>.</summary>
    public static bool IsDeviceNamespacePath(string path)
    {
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal)) return true;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            // \\?\C:\...  and  \\?\UNC\server\share  are ordinary long paths; anything else is a device path.
            var rest = path.AsSpan(4);
            var isDrive = rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':';
            var isUnc = rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase);
            return !(isDrive || isUnc);
        }
        return false;
    }

    /// <summary>
    /// The reparse tag of <paramref name="path"/> (0 if it is not a reparse point or cannot be queried), obtained from the
    /// directory entry so the object is never opened (opening a pipe or socket could block or have side effects).
    /// </summary>
    public static uint ReparseTag(string path)
    {
        try
        {
            using var handle = PInvoke.FindFirstFile(ToExtendedLength(path), out WIN32_FIND_DATAW data);
            if (handle.IsInvalid) return 0;
            return (data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>True if <paramref name="path"/> is a device, named pipe, AF_UNIX socket, or WSL special file (not a regular file or directory).</summary>
    public static bool IsSpecial(string path)
    {
        if (IsDeviceNamespacePath(path)) return true;
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Device) != 0) return true;
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                var tag = ReparseTag(path);
                return tag is ReparseTagAfUnix or ReparseTagLxFifo or ReparseTagLxChr or ReparseTagLxBlk;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Adds the <c>\\?\</c> prefix to long paths so the Win32 call works without the long-path manifest setting.</summary>
    public static string ToExtendedLength(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 240 || full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }
}
