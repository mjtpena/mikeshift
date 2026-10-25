using System.Text;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Tests.Integrity;

namespace MikeShift.Cli.Tests;

/// <summary>
/// The integrity checks through the real <c>mikeshift.exe</c> child process: for each kind of file, <c>--list</c> must name the same
/// actions the catalog offers, and every action must exit 0, print output paths that exist and are valid files of the right kind
/// (checked by the suite's independent readers), leave the original byte-identical and leave no staging folder behind.
/// </summary>
public class IntegrityCliTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    /// <summary>The catalog's titles in the order the CLI prints them: "A, B, C".</summary>
    private static string Titles(string path) => string.Join(", ", ActionCatalog.For([path], Capabilities.Current).Select(a => a.Title));

    private static async Task RunEvery(string input, TempDir tmp, IEnumerable<string> titles, string[]? extra = null)
    {
        var sha = Kit.Sha256(input);
        var list = CliProcess.Run(Limit, "--list", input);
        Assert.Equal(0, list.ExitCode);
        Assert.Equal(Titles(input), list.Stdout.Trim());

        foreach (var title in titles)
        {
            var args = new List<string> { "--run", title };
            if (title == "Trim") args.AddRange(["--range", "0:00.5-0:01.5"]);
            if (title == "Crop") args.AddRange(["--crop", "2,2,20,16"]);
            args.Add(input);
            var r = CliProcess.Run(Limit, [.. args]);
            Assert.True(r.ExitCode == 0, $"mikeshift --run {title} exited {r.ExitCode}: {r.Stderr}");
            Assert.DoesNotContain("error:", r.Stderr);
            Assert.NotEmpty(r.StdoutLines);
            foreach (var line in r.StdoutLines)
            {
                Assert.True(Path.IsPathRooted(line), "output path is not absolute: " + line);
                var what = await OutputCheck.ValidateAsync(line);
                Kit.Record("cli", Path.GetFileName(input), title, "mikeshift.exe child process + " + what, "exit 0, output valid");
            }
            Assert.Equal(sha, Kit.Sha256(input));
            Kit.AssertNoStagingLeftovers([input]);
        }
    }

    [Fact]
    public async Task Image_actions_through_the_exe()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic é.jpg", Fixtures.PhotoJpeg(640, 480));
        await RunEvery(input, tmp, ["PNG", "TIFF", "BMP", "GIF", "WebP", "PDF", "Compress", "Resize 50%", "Crop", "Strip EXIF", "Zip"]);
    }

    [Fact]
    public async Task Pdf_actions_through_the_exe()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("doc.pdf", Fixtures.PdfText(3));
        await RunEvery(input, tmp, ["PNG", "JPG", "TXT", "Split", "Zip"]);
        var heavy = tmp.WriteFile("heavy.pdf", Fixtures.PdfImageHeavy(2));
        await RunEvery(heavy, tmp, ["Compress"]);
        var other = tmp.WriteFile("other.pdf", Fixtures.PdfText(2));
        var r = CliProcess.Run(Limit, "--run", "Merge", input, other);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(5, PdfCheck.Read(r.StdoutLines.Single()).Count);
    }

    [Fact]
    public async Task Document_actions_through_the_exe()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("report.docx", Fixtures.DocxSample());
        // Word is opt-in (--use-word), so every format here is MikeShift's own writer.
        await RunEvery(input, tmp, ["PDF", "RTF", "TXT", "HTML", "ODT", "Zip"]);
    }

    [Fact]
    public async Task A_folder_zips_through_the_exe()
    {
        using var tmp = new TempDir();
        var dir = tmp.CreateDir("My Folder");
        File.WriteAllText(Path.Combine(dir, "café.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "empty"));
        File.WriteAllBytes(Path.Combine(dir, "d.bin"), Fixtures.Noise(5000, 3));
        var r = CliProcess.Run(Limit, "--run", "Zip", dir);
        Assert.Equal(0, r.ExitCode);
        var zip = r.StdoutLines.Single();
        Assert.Equal(tmp.Combine("My Folder.zip"), zip);
        await OutputCheck.ValidateAsync(zip);
        Kit.Record("cli", "My Folder", "Zip", "mikeshift.exe child process + zip entry CRC check", "exit 0, valid archive");
    }

    [SlowFact, Trait("Category", "Slow")]
    public async Task Audio_actions_through_the_exe()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("tone.wav", AudioCheck.WavBytes(AudioSource.Chirp(16, 2)));
        await RunEvery(input, tmp, ["AIFF", "FLAC", "MP3", "M4A", "Compress", "Trim", "Zip"]);
    }

    [SlowFact, Trait("Category", "Slow")]
    public async Task Video_actions_through_the_exe()
    {
        using var tmp = new TempDir();
        var input = tmp.Combine("clip.mp4");
        await VideoCheck.WriteMp4Async(input);
        await RunEvery(input, tmp, ["M4V", "WMV", "GIF", "M4A", "MP3", "Compress", "Trim", "Zip"]);
    }

    [Fact]
    public void An_unknown_action_lists_what_is_available_and_fails_cleanly()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.png", Fixtures.PngRgb(32, 24));
        var r = CliProcess.Run(Limit, "--run", "Teleport", input);
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("error:", r.Stderr);
        Assert.Contains("JPG", r.Stderr);
        Assert.Empty(r.StdoutLines);
        Assert.Single(Directory.GetFileSystemEntries(tmp.Path), p => !p.EndsWith("__downloads"));
    }

    [Fact]
    public void Output_paths_with_unicode_survive_the_pipe()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("日本語 Ελ.png", Fixtures.PngRgb(32, 24));
        var r = CliProcess.Run(Limit, "--run", "JPG", input);
        Assert.Equal(0, r.ExitCode);
        var line = r.StdoutLines.Single();
        Assert.Equal(tmp.Combine("日本語 Ελ.jpg"), line);
        Assert.True(File.Exists(line));
    }
}
