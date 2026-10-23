using System.Text;

namespace MikeShift.Core.Hosting;

/// <summary>Keeps a diagnostic log file small: over the cap, only the newest lines survive.</summary>
public static class LogTrimmer
{
    /// <summary>The log is cut when it grows past this.</summary>
    public const long DefaultMaxBytes = 1_000_000;

    /// <summary>How many writes pass between size checks (a check is a file-system call).</summary>
    public const int CheckEveryWrites = 64;

    /// <summary>
    /// If <paramref name="path"/> is longer than <paramref name="maxBytes"/>, rewrites it with its last half (starting at a line boundary).
    /// Returns true if it was cut. A missing file is not an error; I/O errors propagate to the caller (the logger swallows them).
    /// </summary>
    public static bool TrimIfLarge(string path, long maxBytes = DefaultMaxBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= maxBytes) return false;

        var keep = Math.Max(1, maxBytes / 2);
        byte[] tail;
        using (var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var length = read.Length;
            var start = Math.Max(0, length - keep);
            read.Seek(start, SeekOrigin.Begin);
            tail = new byte[length - start];
            read.ReadExactly(tail);
            // Skip the partial first line so the log starts on a whole one.
            if (start > 0)
            {
                var newline = Array.IndexOf(tail, (byte)'\n');
                if (newline >= 0) tail = tail[(newline + 1)..];
            }
        }

        var marker = Encoding.UTF8.GetBytes("--- older lines trimmed ---" + Environment.NewLine);
        using var write = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        write.Write(marker, 0, marker.Length);
        write.Write(tail, 0, tail.Length);
        return true;
    }
}
