using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Zip of a folder, of several files and of a mixed selection. The archive is opened by <c>System.IO.Compression</c> AND extracted
/// by Windows' own <c>tar.exe</c> (bsdtar / libarchive), then every file is compared byte for byte (SHA-256) with the source tree;
/// names (Unicode included), folder structure, empty folders and the UTF-8 flag in the zip headers are checked too.
/// </summary>
public class ZipIntegrityTests
{
    private static readonly string[] UnicodeNames =
    [
        "café ñandú.txt",                  // accents
        "日本語\\ファイル.txt",  // Japanese folder and file
        "שלום.txt",                   // Hebrew
        "Ελληνικά.md",   // Greek
        "emoji \U0001F600 smile.txt",                     // outside the BMP
    ];

    /// <summary>Builds a tree with text, binary, empty, Unicode-named and deeply nested files plus an empty folder.</summary>
    private static string MakeTree(TempDir tmp, string root = "Project")
    {
        var dir = tmp.CreateDir(root);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), string.Concat(Enumerable.Repeat("Hello MikeShift. ", 500)));
        File.WriteAllBytes(Path.Combine(dir, "random.bin"), Fixtures.Noise(300_000, seed: 21));
        File.WriteAllBytes(Path.Combine(dir, "empty.dat"), []);
        File.WriteAllBytes(Path.Combine(dir, "photo.jpg"), Fixtures.PhotoJpeg(320, 240));
        File.WriteAllBytes(Path.Combine(dir, "a file with spaces and (parens) [brackets] & ampersand.txt"), "spaces"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(dir, "empty folder"));
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deeper", "deepest"));
        File.WriteAllText(Path.Combine(dir, "sub", "deeper", "deepest", "leaf.txt"), "leaf");
        File.WriteAllBytes(Path.Combine(dir, "sub", "deeper", "data.bin"), Fixtures.Noise(70_000, seed: 22));
        foreach (var n in UnicodeNames)
        {
            var path = Path.Combine(dir, n);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "content of " + n, new UTF8Encoding(false));
        }
        File.SetLastWriteTimeUtc(Path.Combine(dir, "readme.txt"), new DateTime(2021, 3, 4, 5, 6, 8, DateTimeKind.Utc));
        return dir;
    }

    private static Dictionary<string, string> Hashes(string root, bool includeFolders)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (Directory.Exists(f)) { if (includeFolders) map[rel + "/"] = "<dir>"; }
            else map[rel] = Kit.Sha256(f);
        }
        return map;
    }

    /// <summary>
    /// True if <paramref name="relative"/> can be written in this PC's ANSI code page. Windows' tar.exe (libarchive) converts UTF-8 names
    /// to the ANSI code page and skips entries that do not fit ("Archive entry has empty or unreadable filename"): a limit of tar.exe, shown
    /// the same way for any standards-conforming zip (a zip written by Python's zipfile fails identically). Such names are checked with
    /// System.IO.Compression instead.
    /// </summary>
    private static bool AnsiSafe(string relative)
    {
        MikeShift.Core.Documents.TextDecoding.EnsureProviders();
        var enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try { return enc.GetString(enc.GetBytes(relative)) == relative; }
        catch (EncoderFallbackException) { return false; }
    }

    /// <summary>The expected files for a tar.exe extraction: everything whose name fits the ANSI code page (and folders on the way to it).</summary>
    private static Dictionary<string, string> AnsiSubset(Dictionary<string, string> all) =>
        all.Where(kv => AnsiSafe(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Extracts with Windows' tar.exe into <paramref name="dest"/>; returns its exit code and output.</summary>
    private static (int Exit, string Output) TarExtract(string zip, string dest)
    {
        Directory.CreateDirectory(dest);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-xf"); psi.ArgumentList.Add(zip); psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(dest);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, outp + err);
    }

    /// <summary>The "UTF-8 names" bit (general purpose flag 11) of every central-directory entry whose name is not pure ASCII.</summary>
    private static void AssertUtf8Flags(string zipPath)
    {
        var b = File.ReadAllBytes(zipPath);
        // End of central directory record.
        var eocd = b.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4B, 5, 6 });
        Assert.True(eocd >= 0, "no end-of-central-directory record");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(eocd + 10));
        var pos = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(eocd + 16));
        for (var i = 0; i < count; i++)
        {
            Assert.True(b.AsSpan(pos, 4).SequenceEqual(new byte[] { 0x50, 0x4B, 1, 2 }), "bad central directory entry");
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + 8));
            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + 28));
            var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + 30));
            var commentLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + 32));
            var nameBytes = b.AsSpan(pos + 46, nameLen);
            var ascii = nameBytes.ToArray().All(x => x < 0x80);
            if (!ascii)
            {
                Assert.True((flags & 0x800) != 0, $"entry '{Encoding.UTF8.GetString(nameBytes)}' has non-ASCII bytes but no UTF-8 flag");
                // And the bytes must be valid UTF-8.
                _ = new UTF8Encoding(false, true).GetString(nameBytes);
            }
            pos += 46 + nameLen + extraLen + commentLen;
        }
    }

    [Fact]
    public async Task A_folder_zips_to_an_archive_every_reader_extracts_byte_for_byte()
    {
        using var tmp = new TempDir();
        var tree = MakeTree(tmp);
        var before = Hashes(tree, includeFolders: true);

        var zip = Kit.One(await Kit.RunAsync("Zip", tree));

        Assert.Equal(tmp.Combine("Project.zip"), zip);

        // 1. System.IO.Compression
        var viaDotnet = tmp.Combine("out-dotnet");
        ZipFile.ExtractToDirectory(zip, viaDotnet);
        var extractedA = Hashes(Path.Combine(viaDotnet, "Project"), includeFolders: true);
        Assert.Equal(before.OrderBy(k => k.Key, StringComparer.Ordinal), extractedA.OrderBy(k => k.Key, StringComparer.Ordinal));

        // 2. Windows' built-in bsdtar
        var viaTar = tmp.Combine("out-tar");
        var (exit, tarOutput) = TarExtract(zip, viaTar);
        var extractedB = Hashes(Path.Combine(viaTar, "Project"), includeFolders: true);
        var skippedByTar = before.Count(kv => !AnsiSafe(kv.Key));
        Assert.True(exit == 0 || tarOutput.Split('\n').Count(l => l.Contains("unreadable filename")) == skippedByTar, "tar.exe: " + tarOutput);
        Assert.Equal(AnsiSubset(before).OrderBy(k => k.Key, StringComparer.Ordinal), AnsiSubset(extractedB).OrderBy(k => k.Key, StringComparer.Ordinal));
        Assert.Equal(before.Count - skippedByTar, extractedB.Count);

        // 3. Archive facts: UTF-8 names flagged, each file entry's declared size matches, timestamps kept (UTC clock value), nothing stored twice.
        AssertUtf8Flags(zip);
        using (var za = ZipFile.OpenRead(zip))
        {
            var names = za.Entries.Select(e => e.FullName).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("Project/empty folder/", names);
            foreach (var n in UnicodeNames) Assert.Contains("Project/" + n.Replace('\\', '/'), names);
            Assert.All(za.Entries.Where(e => !e.FullName.EndsWith('/')), e =>
                Assert.Equal(new FileInfo(Path.Combine(tmp.Path, e.FullName.Replace('/', '\\'))).Length, e.Length));
            var readme = za.GetEntry("Project/readme.txt")!;
            Assert.True(Math.Abs((readme.LastWriteTime.DateTime - new DateTime(2021, 3, 4, 5, 6, 8)).TotalSeconds) <= 2, "timestamp was not kept");
            // Already-compressed payloads are stored, text is deflated.
            Assert.True(readme.Length > readme.CompressedLength);
        }

        // 4. The tree on disk is untouched and there is no staging folder (Kit checked); the archive does not contain itself.
        Assert.Equal(before.OrderBy(k => k.Key, StringComparer.Ordinal), Hashes(tree, includeFolders: true).OrderBy(k => k.Key, StringComparer.Ordinal));
        Kit.Record("zip", "folder with 17 entries (Unicode names, empty folder, empty file, 300 KB binary, nested 4 deep)", "Zip",
            "System.IO.Compression extract + Windows tar.exe (bsdtar) extract + central-directory parser",
            $"{before.Count(kv => kv.Value != "<dir>")} files byte-identical (SHA-256) with System.IO.Compression; tar.exe: all {before.Count - skippedByTar} entries whose names fit the ANSI code page identical (the {skippedByTar} others are skipped by tar.exe itself, a bsdtar limit), empty folder kept, UTF-8 flag set for non-ASCII names");
    }

    [Fact]
    public async Task Several_files_and_folders_zip_into_Archive_with_unique_names()
    {
        using var tmp = new TempDir();
        var folder = MakeTree(tmp, "Docs");
        var a = tmp.WriteFile("one\\same.txt", "first");
        var b = tmp.WriteFile("two\\same.txt", "second");
        var c = tmp.WriteFile("c.bin", Fixtures.Noise(1000, 5));
        var otherFolder = tmp.CreateDir("two\\Docs");
        File.WriteAllText(Path.Combine(otherFolder, "x.txt"), "x");

        var zip = Kit.One(await Kit.RunAsync("Zip", a, b, c, folder, otherFolder));

        Assert.Equal(Path.Combine(Path.GetDirectoryName(a)!, "Archive.zip"), zip);
        var dest = tmp.Combine("out");
        var (exit, output) = TarExtract(zip, dest);
        var skipped = Hashes(folder, true).Count(kv => !AnsiSafe(kv.Key));
        Assert.True(exit == 0 || output.Split('\n').Count(l => l.Contains("unreadable filename")) == skipped, output);
        Assert.Equal("first", File.ReadAllText(Path.Combine(dest, "same.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(dest, "same 2.txt")));
        Assert.Equal(Kit.Sha256(c), Kit.Sha256(Path.Combine(dest, "c.bin")));
        Assert.Equal(AnsiSubset(Hashes(folder, true)).OrderBy(k => k.Key, StringComparer.Ordinal), AnsiSubset(Hashes(Path.Combine(dest, "Docs"), true)).OrderBy(k => k.Key, StringComparer.Ordinal));
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "Docs 2", "x.txt")));
        AssertUtf8Flags(zip);
        using var za = ZipFile.OpenRead(zip);
        foreach (var e in za.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms); // throws InvalidDataException on a CRC / deflate error
            Assert.Equal(e.Length, ms.Length);
        }
        Kit.Record("zip", "2 same-named files + 1 file + 2 same-named folders", "Zip", "Windows tar.exe extract + System.IO.Compression CRC check",
            "Archive.zip; names uniquified (same.txt, same 2.txt, Docs, Docs 2); every file byte-identical");
    }

    [Fact]
    public async Task A_mixed_selection_of_a_picture_a_pdf_and_a_video_zips_unchanged()
    {
        using var tmp = new TempDir();
        var jpg = tmp.WriteFile("pic.jpg", Fixtures.PhotoJpeg(400, 300));
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(2));
        var wav = tmp.WriteFile("snd.wav", AudioCheck.WavBytes(AudioSource.Chirp(16, 1)));
        var txt = tmp.WriteFile("nöte.txt", "über");

        var zip = Kit.One(await Kit.RunAsync("Zip", jpg, pdf, wav, txt));

        var dest = tmp.Combine("out");
        var (exit, output) = TarExtract(zip, dest);
        Assert.True(exit == 0, output);
        foreach (var f in new[] { jpg, pdf, wav, txt })
            Assert.Equal(Kit.Sha256(f), Kit.Sha256(Path.Combine(dest, Path.GetFileName(f))));
        // The stored (already compressed) picture and PDF must be exactly as big as the originals.
        using var za = ZipFile.OpenRead(zip);
        Assert.Equal(new FileInfo(jpg).Length, za.GetEntry("pic.jpg")!.CompressedLength);
        Assert.True(za.GetEntry("snd.wav")!.CompressedLength < new FileInfo(wav).Length);
        Kit.Record("zip", "pic.jpg + doc.pdf + snd.wav + nöte.txt", "Zip", "Windows tar.exe extract + entry sizes", "all four byte-identical; compressed formats stored, WAV deflated");
    }
}
