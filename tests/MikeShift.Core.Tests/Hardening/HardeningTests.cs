using System.Diagnostics;
using MikeShift.Core.Actions;
using MikeShift.Core.Cli;

namespace MikeShift.Core.Tests.Hardening;

/// <summary>
/// PLAN Phase 6 "fuzz-ish inputs": truncated and misnamed files, zero-byte files and big selections. Whatever the input,
/// classification must not throw, and every action the catalog offers must either succeed or fail with a clean
/// <c>error: ...</c> (exit code 1), within 30 s, never leaving a partial or stray file next to the original and never touching it.
/// The matrix runs the real <see cref="CliRunner"/> in-process (the same code the <c>mikeshift</c> executable runs).
/// </summary>
[Collection(WordCollection.Name)] // the matrix sends a .doc to Word, which must not run beside the other Word tests
public class HardeningTests
{
    private static readonly TimeSpan MaxRun = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A legacy .doc goes through Word, which starts slowly and has its own 90 s kill switch (WordInterop.Timeout): the limit for those
    /// has to outlast that, or a slow Word start (cold machine, add-ins) would be reported as a hang. A real hang still fails.
    /// </summary>
    private static readonly TimeSpan MaxWordRun = TimeSpan.FromSeconds(120);

    private sealed record Sample(string Name, byte[] Bytes);

    // ------------------------------------------------------------------ the corpus

    /// <summary>Valid files of each family; truncations and misnamings are derived from them.</summary>
    private static List<Sample> GoodFiles()
    {
        var good = new List<Sample>
        {
            new("good.png", Fixtures.PngRgb(48, 32)),
            new("good.jpg", Fixtures.QuadrantJpeg(64, 48)),
            new("good.pdf", Fixtures.PdfText(2)),
            new("good.docx", Fixtures.DocxSample()),
            new("good.wav", Fixtures.SineWav(0.3)),
        };
        try
        {
            var mp4 = Path.Combine(Path.GetTempPath(), "mikeshift-hardening-" + Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                Fixtures.WriteMp4(mp4, seconds: 1);
                good.Add(new("good.mp4", File.ReadAllBytes(mp4)));
            }
            finally { try { File.Delete(mp4); } catch (IOException) { } }
        }
        catch (Exception)
        {
            // No H.264 encoder on this machine: the MP4 cases are simply not part of the corpus.
        }
        return good;
    }

    private static IEnumerable<Sample> Truncations(Sample good)
    {
        var ext = Path.GetExtension(good.Name);
        var stem = Path.GetFileNameWithoutExtension(good.Name);
        var cuts = new[] { 0, 1, 4, 12, good.Bytes.Length / 4, good.Bytes.Length / 2, good.Bytes.Length - 1 }
            .Where(n => n >= 0 && n < good.Bytes.Length).Distinct();
        foreach (var n in cuts)
            yield return new($"trunc-{stem}-{n}{ext}", good.Bytes[..n]);
    }

    private static IEnumerable<Sample> Misnamed(IReadOnlyList<Sample> good)
    {
        byte[] Bytes(string name) => good.First(g => g.Name == name).Bytes;
        yield return new("png-bytes.pdf", Bytes("good.png"));
        yield return new("pdf-bytes.png", Bytes("good.pdf"));
        yield return new("pdf-bytes.jpg", Bytes("good.pdf"));
        yield return new("png-bytes.jpg", Bytes("good.png"));
        yield return new("jpg-bytes.png", Bytes("good.jpg"));
        yield return new("png-bytes.docx", Bytes("good.png"));
        yield return new("docx-bytes.pdf", Bytes("good.docx"));
        yield return new("docx-bytes.png", Bytes("good.docx"));
        yield return new("docx-bytes.odt", Bytes("good.docx"));
        yield return new("wav-bytes.mp3", Bytes("good.wav"));
        yield return new("png-bytes.mp4", Bytes("good.png"));
        // Legacy .doc goes to Word when it is installed (a bad file must not leave a Word dialog or process hanging).
        yield return new("random.doc", Fixtures.Noise(4096, 10));
        yield return new("docx-bytes.doc", Bytes("good.docx"));
        yield return new("random.docx", Fixtures.Noise(4096, 11));
        yield return new("random.odt", Fixtures.Noise(4096, 12));
        yield return new("random.rtf", Fixtures.Noise(4096, 13));
        yield return new("random.png", Fixtures.Noise(4096, 14));
        yield return new("random.jpg", Fixtures.Noise(4096, 15));
        yield return new("random.pdf", Fixtures.Noise(4096, 16));
        yield return new("random.mp4", Fixtures.Noise(4096, 17));
        yield return new("random.mp3", Fixtures.Noise(4096, 18));
        yield return new("random.wav", Fixtures.Noise(4096, 19));
        yield return new("random.gif", Fixtures.Noise(4096, 20));
        yield return new("random.tiff", Fixtures.Noise(4096, 21));
        yield return new("random.webp", Fixtures.Noise(4096, 22));
        yield return new("random.html", Fixtures.Noise(4096, 23));
        yield return new("random.txt", Fixtures.Noise(4096, 24));
        yield return new("random.aiff", Fixtures.Noise(4096, 25));
        if (good.Any(g => g.Name == "good.mp4"))
            yield return new("mp4-bytes.png", Bytes("good.mp4"));
    }

    private static IEnumerable<Sample> ZeroBytes() =>
        new[] { "png", "jpg", "gif", "tiff", "bmp", "webp", "heic", "pdf", "docx", "doc", "odt", "rtf", "html", "txt", "md", "mp4", "mov", "mp3", "wav", "m4a", "flac", "aiff", "ts" }
            .Select(e => new Sample($"empty.{e}", []));

    // ------------------------------------------------------------------ running one input

    private sealed record Run(int Exit, string Out, string Err, TimeSpan Elapsed);

    private static Task<Run> RunCli(params string[] args) => RunCli(MaxRun, args);

    private static async Task<Run> RunCli(TimeSpan limit, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => CliRunner.RunAsync(args, stdout, stderr, Capabilities.Current));
        var exit = await task.WaitAsync(limit); // a hang fails the test with a TimeoutException
        return new Run(exit, stdout.ToString(), stderr.ToString(), sw.Elapsed);
    }

    private static string[] Snapshot(string dir) =>
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Classifies <paramref name="sample"/> and runs every offered action on it; returns the problems found.</summary>
    private static async Task<List<string>> Exercise(TempDir tmp, Sample sample)
    {
        var problems = new List<string>();
        var dir = tmp.CreateDir(Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, sample.Name);
        File.WriteAllBytes(path, sample.Bytes);

        Category category;
        IReadOnlyList<ActionDef> actions;
        try
        {
            Kind.ClearCache();
            category = Kind.Of(path);
            actions = ActionCatalog.For([path], Capabilities.Current);
        }
        catch (Exception ex)
        {
            problems.Add($"{sample.Name}: classification threw {ex.GetType().Name}: {ex.Message}");
            return problems;
        }

        foreach (var action in actions)
        {
            var args = new List<string> { "--run", action.Title };
            if (action.Title.Equals("Crop", StringComparison.OrdinalIgnoreCase)) args.AddRange(["--crop", "0,0,8,8"]);
            if (action.Title.Equals("Trim", StringComparison.OrdinalIgnoreCase)) args.AddRange(["--range", "0:00-0:00.2"]);
            args.Add(path);

            var before = Snapshot(dir);
            var label = $"{sample.Name} ({category}) / {action.Title}";
            Run run;
            try
            {
                using var guard = OriginalsGuard.Watch(path);
                run = await RunCli(sample.Name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ? MaxWordRun : MaxRun, [.. args]);
            }
            catch (Exception ex)
            {
                problems.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var reported = run.Out.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFullPath).ToArray();
            // Multi-page outputs go into a folder: its contents belong to the reported output.
            var created = Snapshot(dir).Except(before, StringComparer.OrdinalIgnoreCase)
                .Where(c => !reported.Any(r => Directory.Exists(r) && c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (run.Exit is not (0 or 1))
                problems.Add($"{label}: exit code {run.Exit}");
            if (run.Exit == 1 && !run.Err.Contains("error:", StringComparison.Ordinal))
                problems.Add($"{label}: exit 1 without an 'error:' line: {run.Err.Trim()}");
            if (run.Exit == 1 && reported.Length == 0 && created.Length > 0)
                problems.Add($"{label}: failed but left files behind: {string.Join(", ", created.Select(Path.GetFileName))}");
            if (run.Exit == 0 && created.Length != reported.Length)
                problems.Add($"{label}: reported {reported.Length} output(s) but {created.Length} new file(s) appeared: {string.Join(", ", created.Select(Path.GetFileName))}");
            foreach (var c in created.Where(c => !reported.Contains(c, StringComparer.OrdinalIgnoreCase)))
                problems.Add($"{label}: stray file {Path.GetFileName(c)} (not reported as an output)");
            if (run.Err.Contains("Exception", StringComparison.Ordinal) || run.Err.Contains("   at ", StringComparison.Ordinal))
                problems.Add($"{label}: stderr looks like a stack trace: {run.Err.Trim()}");
        }

        // Directory.Delete in TempDir.Dispose removes everything; nothing else to clean here.
        return problems;
    }

    private static async Task AssertAllClean(IEnumerable<Sample> samples)
    {
        using var tmp = new TempDir();
        var problems = new List<string>();
        foreach (var s in samples)
            problems.AddRange(await Exercise(tmp, s));
        Assert.True(problems.Count == 0, "Problems:\n" + string.Join("\n", problems));
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task Truncated_files_fail_cleanly_or_convert()
    {
        var good = GoodFiles();
        await AssertAllClean(good.SelectMany(Truncations));
    }

    [Fact]
    public async Task Misnamed_and_random_files_fail_cleanly_or_convert()
    {
        var good = GoodFiles();
        await AssertAllClean(Misnamed(good));
    }

    [Fact]
    public async Task Zero_byte_files_fail_cleanly_or_convert() => await AssertAllClean(ZeroBytes());

    [Fact]
    public async Task Garbage_and_empty_doc_files_are_refused_before_Word_is_started()
    {
        using var tmp = new TempDir();
        var sw = Stopwatch.StartNew();
        foreach (var (name, bytes) in new[] { ("noise.doc", Fixtures.Noise(4096, 3)), ("empty.doc", Array.Empty<byte>()) })
        {
            var path = tmp.WriteFile(name, bytes);
            var ex = await Assert.ThrowsAsync<ConversionException>(() =>
                MikeShift.Core.Documents.DocumentConverter.ToFormatAsync(path, MikeShift.Core.Documents.DocFormat.Pdf, Capabilities.All, default));
            Assert.Contains("valid Word document", ex.Message);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), "a refusal must not start Word");
    }

    [Fact]
    public async Task Valid_files_still_work_in_the_same_matrix()
    {
        // Guards the matrix itself: it must not "pass" just because every action fails.
        using var tmp = new TempDir();
        var png = tmp.WriteFile("ok.png", Fixtures.PngRgb(48, 32));
        var r = await RunCli("--run", "JPG", png);
        Assert.Equal(0, r.Exit);
        Assert.True(File.Exists(r.Out.Trim()));

        var pdf = tmp.WriteFile("ok.pdf", Fixtures.PdfText(2));
        r = await RunCli("--run", "TXT", pdf);
        Assert.Equal(0, r.Exit);
    }

    [Fact]
    public void Misnamed_files_classify_by_their_bytes_in_small_selections()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var pdfAsPng = tmp.WriteFile("a.png", Fixtures.PdfText(1));
        var pngAsPdf = tmp.WriteFile("b.pdf", Fixtures.PngRgb(8, 8));
        var realPng = tmp.WriteFile("c.png", Fixtures.PngRgb(8, 8));

        Assert.Equal(Category.Pdf, Kind.Of(pdfAsPng));
        Assert.Equal(Category.Other, Kind.Of(pngAsPdf)); // a ".pdf" without the magic is not a PDF
        Assert.Equal(Category.Image, Kind.Of(realPng));

        // One misnamed file among images makes the selection mixed: Zip only.
        var actions = ActionCatalog.For([realPng, pdfAsPng], Capabilities.All);
        Assert.Equal(["Zip"], actions.Select(a => a.Title));
    }

    // ------------------------------------------------------------------ big selections

    private static List<string> MakeFiles(TempDir tmp, int count, Func<int, (string Name, byte[] Bytes)> make)
    {
        var paths = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var (name, bytes) = make(i);
            paths.Add(tmp.WriteFile(Path.Combine("many", name), bytes));
        }
        return paths;
    }

    [Fact]
    public void A_500_file_selection_gets_its_tiles_in_under_two_seconds_with_a_cold_cache()
    {
        using var tmp = new TempDir();
        var jpg = Fixtures.PhotoJpeg(640, 480, 90);
        var paths = MakeFiles(tmp, 500, i => ($"img{i:000}.jpg", jpg));

        Kind.ClearCache();
        var sw = Stopwatch.StartNew();
        var actions = ActionCatalog.For(paths, Capabilities.All);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"ActionCatalog.For took {sw.Elapsed.TotalMilliseconds:0} ms for 500 JPEGs");
        Assert.Contains(actions, a => a.Title == "PNG");
        Assert.Contains(actions, a => a.Title == "Compress");
    }

    [Fact]
    public void A_500_file_mixed_selection_is_zip_only_and_fast()
    {
        using var tmp = new TempDir();
        var jpg = Fixtures.QuadrantJpeg(32, 24);
        var pdf = Fixtures.PdfStub();
        var paths = MakeFiles(tmp, 500, i => i % 2 == 0 ? ($"img{i:000}.jpg", jpg) : ($"doc{i:000}.pdf", pdf));

        Kind.ClearCache();
        var sw = Stopwatch.StartNew();
        var actions = ActionCatalog.For(paths, Capabilities.All);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"ActionCatalog.For took {sw.Elapsed.TotalMilliseconds:0} ms");
        Assert.Equal(["Zip"], actions.Select(a => a.Title));
    }

    [Fact]
    public void A_large_selection_whose_last_file_is_a_misnamed_pdf_is_not_all_images()
    {
        using var tmp = new TempDir();
        var jpg = Fixtures.QuadrantJpeg(32, 24);
        var paths = MakeFiles(tmp, 100, i => (i == 99 ? "zz-last.jpg" : $"img{i:000}.jpg", i == 99 ? Fixtures.PdfStub() : jpg));

        Kind.ClearCache();
        var actions = ActionCatalog.For(paths, Capabilities.All);
        Assert.Equal(["Zip"], actions.Select(a => a.Title));
    }

    [Fact]
    public void A_large_selection_containing_a_folder_or_a_missing_file_is_zip_only()
    {
        using var tmp = new TempDir();
        var jpg = Fixtures.QuadrantJpeg(32, 24);
        var paths = MakeFiles(tmp, 60, i => ($"img{i:000}.jpg", jpg));

        var withFolder = new List<string>(paths);
        withFolder[30] = tmp.CreateDir("many/folder.jpg");
        Kind.ClearCache();
        Assert.Equal(["Zip"], ActionCatalog.For(withFolder, Capabilities.All).Select(a => a.Title));

        var withMissing = new List<string>(paths);
        withMissing[45] = Path.Combine(tmp.Path, "many", "gone.jpg");
        Kind.ClearCache();
        Assert.Equal(["Zip"], ActionCatalog.For(withMissing, Capabilities.All).Select(a => a.Title));
    }

    [Fact]
    public void A_500_file_selection_of_unreadable_extensions_is_zip_only()
    {
        using var tmp = new TempDir();
        var paths = MakeFiles(tmp, 500, i => ($"data{i:000}.bin", Fixtures.Noise(64, i)));
        Kind.ClearCache();
        var sw = Stopwatch.StartNew();
        var actions = ActionCatalog.For(paths, Capabilities.All);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(["Zip"], actions.Select(a => a.Title));
    }
}
