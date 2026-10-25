using MikeShift.Core;

namespace MikeShift.Core.Tests;

public class NamingTests
{
    [Fact]
    public void Output_changes_extension_and_appends_suffix_in_same_directory()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.heic", "x");

        Assert.Equal(tmp.Combine("photo.png"), Naming.Output(input, "png"));
        Assert.Equal(tmp.Combine("photo.png"), Naming.Output(input, ".PNG"));
        Assert.Equal(tmp.Combine("photo compressed.jpg"), Naming.Output(input, "jpg", " compressed"));
        Assert.Equal(tmp.Combine("photo 50%.heic"), Naming.Output(input, "heic", " 50%"));
    }

    [Fact]
    public void Unique_counts_up_from_two()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("photo.heic", "x");
        tmp.WriteFile("photo.png", "1");
        Assert.Equal(tmp.Combine("photo 2.png"), Naming.Output(input, "png"));
        tmp.WriteFile("photo 2.png", "2");
        Assert.Equal(tmp.Combine("photo 3.png"), Naming.Output(input, "png"));
    }

    [Fact]
    public void Unique_treats_existing_folders_as_taken()
    {
        using var tmp = new TempDir();
        tmp.CreateDir("doc pages");
        Assert.Equal(tmp.Combine("doc pages 2"), Naming.Unique(tmp.Path, "doc pages", ""));
        tmp.CreateDir("doc pages 2");
        Assert.Equal(tmp.Combine("doc pages 3"), Naming.Unique(tmp.Path, "doc pages", ""));
    }

    [Fact]
    public void Plan_for_a_folder_input_uses_the_folder_name_and_parent()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("My.Stuff");
        var spec = Naming.Plan(folder + Path.DirectorySeparatorChar, ".zip");
        Assert.Equal(tmp.Path, spec.Directory);
        Assert.Equal("My.Stuff", spec.BaseName);
        Assert.Equal(".zip", spec.Extension);
    }

    [Fact]
    public async Task Commit_writes_via_temp_then_moves_to_final_name_and_leaves_no_temp()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");
        string? seenTemp = null;

        var result = await Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            seenTemp = temp;
            Assert.NotEqual(tmp.Combine("a.out"), temp);
            Assert.Equal(tmp.Path, Path.GetDirectoryName(Path.GetDirectoryName(temp)));
            await File.WriteAllTextAsync(temp, "data", ct);
        }, CancellationToken.None);

        Assert.Equal(tmp.Combine("a.out"), result.Path);
        Assert.False(result.UsedDownloadsFallback);
        Assert.Equal("data", File.ReadAllText(result.Path));
        Assert.False(File.Exists(seenTemp));
        Assert.Equal(["a.out", "a.txt"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public async Task Temp_items_are_hidden_while_being_written_and_the_final_output_is_not()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");
        FileAttributes? tempContainer = null, folderTemp = null;

        var file = await Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            tempContainer = new DirectoryInfo(Path.GetDirectoryName(temp)!).Attributes;
            await File.WriteAllTextAsync(temp, "data", ct);
        }, CancellationToken.None);
        var folder = await Naming.CommitAsync(Naming.Plan(input, "", " pages"), async (tempDir, ct) =>
        {
            folderTemp = new DirectoryInfo(tempDir).Attributes;
            await File.WriteAllTextAsync(Path.Combine(tempDir, "p.txt"), "p", ct);
        }, CancellationToken.None);

        Assert.True(tempContainer!.Value.HasFlag(FileAttributes.Hidden));
        Assert.True(folderTemp!.Value.HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(file.Path).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(folder.Path).HasFlag(FileAttributes.Hidden));
        Assert.Equal(["a.out", "a.txt"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public async Task Commit_retries_the_final_move_while_the_temp_file_is_briefly_locked()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");

        var result = await Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            await File.WriteAllTextAsync(temp, "data", ct);
            var locked = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None);
            _ = Task.Delay(150, CancellationToken.None).ContinueWith(_ => locked.Dispose(), TaskScheduler.Default);
        }, CancellationToken.None);

        Assert.False(result.UsedDownloadsFallback);
        Assert.Equal(tmp.Combine("a.out"), result.Path);
        Assert.Equal("data", File.ReadAllText(result.Path));
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path, "*.mikeshift-tmp"));
    }

    /// <summary>
    /// A scanner (antivirus, search indexer) that opens a file written a moment ago without delete sharing makes the rename of the
    /// folder that contains it fail with "access denied" (not with a sharing violation). That was the intermittent
    /// "Access to the path '.doc pages.guid.mikeshift-tmp' is denied" of the page-to-image conversions of multi-page PDFs.
    /// </summary>
    [Fact]
    public async Task Commit_retries_the_final_folder_move_while_a_scanner_holds_a_file_inside_open()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");
        FileStream? scanner = null;

        var result = await Naming.CommitAsync(Naming.Plan(input, "", " pages"), async (dir, ct) =>
        {
            var page = Path.Combine(dir, "a 01.png");
            await File.WriteAllBytesAsync(page, [1, 2, 3], ct);
            scanner = new FileStream(page, FileMode.Open, FileAccess.Read, FileShare.Read); // no FileShare.Delete
            _ = Task.Delay(300, CancellationToken.None).ContinueWith(_ => scanner.Dispose(), TaskScheduler.Default);
        }, CancellationToken.None);

        Assert.Equal(tmp.Combine("a pages"), result.Path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(result.Path, "a 01.png")));
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path, "*.mikeshift-tmp"));
    }

    [Fact]
    public async Task Commit_retries_the_final_file_move_while_a_scanner_holds_the_temp_file_open_for_reading()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");

        var result = await Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            await File.WriteAllTextAsync(temp, "data", ct);
            var scanner = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = Task.Delay(300, CancellationToken.None).ContinueWith(_ => scanner.Dispose(), TaskScheduler.Default);
        }, CancellationToken.None);

        Assert.Equal("data", File.ReadAllText(result.Path));
    }

    [Fact]
    public async Task Commit_gives_up_with_a_clear_message_when_the_temp_stays_locked()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");
        FileStream? locked = null;
        try
        {
            var ex = await Assert.ThrowsAsync<ConversionException>(() => Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
            {
                await File.WriteAllTextAsync(temp, "data", ct);
                locked = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None);
            }, CancellationToken.None));
            Assert.Contains("another program", ex.Message);
        }
        finally
        {
            locked?.Dispose();
        }
        Assert.False(File.Exists(tmp.Combine("a.out")));
    }

    [Fact]
    public async Task Commit_never_overwrites_an_existing_file_even_when_the_name_is_taken_during_the_write()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");

        var result = await Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            await File.WriteAllTextAsync(temp, "mine", ct);
            // Somebody else grabs the name while we are still writing.
            File.WriteAllText(tmp.Combine("a.out"), "theirs");
        }, CancellationToken.None);

        Assert.Equal(tmp.Combine("a 2.out"), result.Path);
        Assert.Equal("theirs", File.ReadAllText(tmp.Combine("a.out")));
        Assert.Equal("mine", File.ReadAllText(result.Path));
    }

    [Fact]
    public async Task Commit_removes_the_temp_file_when_the_writer_throws_or_is_cancelled()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            await File.WriteAllTextAsync(temp, "partial", ct);
            throw new InvalidOperationException("boom");
        }, CancellationToken.None));

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Naming.CommitAsync(Naming.Plan(input, "out"), async (temp, ct) =>
        {
            await File.WriteAllTextAsync(temp, "partial", CancellationToken.None);
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }, cts.Token));

        Assert.Equal(["a.txt"], Directory.GetFileSystemEntries(tmp.Path).Select(f => Path.GetFileName(f)!).Where(n => n != "__downloads").ToArray());
    }

    [Fact]
    public async Task Commit_fails_clearly_when_the_writer_produces_nothing()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "x");
        await Assert.ThrowsAsync<ConversionException>(() =>
            Naming.CommitAsync(Naming.Plan(input, "out"), (_, _) => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task Commit_folder_variant_creates_a_unique_folder_with_the_content()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("doc.pdf", "x");
        tmp.CreateDir("doc pages");

        var result = await Naming.CommitAsync(Naming.Plan(input, "", " pages"), async (tempDir, ct) =>
        {
            Assert.True(Directory.Exists(tempDir));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "doc 01.png"), "p1", ct);
        }, CancellationToken.None);

        Assert.Equal(tmp.Combine("doc pages 2"), result.Path);
        Assert.Equal("p1", File.ReadAllText(Path.Combine(result.Path, "doc 01.png")));
    }

    [Fact]
    public async Task Commit_falls_back_to_downloads_when_the_directory_is_not_writable_and_says_so()
    {
        using var tmp = new TempDir();
        var locked = tmp.CreateDir("locked");
        var input = tmp.WriteFile(Path.Combine("locked", "a.txt"), "x");

        CommitResult result;
        using (TempDir.DenyWrites(locked))
        {
            result = await Naming.CommitAsync(Naming.Plan(input, "out"),
                (temp, ct) => File.WriteAllTextAsync(temp, "data", ct), CancellationToken.None);
        }

        Assert.True(result.UsedDownloadsFallback);
        Assert.Equal(tmp.Combine("__downloads", "a.out"), result.Path);
        Assert.Equal("data", File.ReadAllText(result.Path));
        Assert.Equal(["a.txt"], Directory.GetFiles(locked).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public void Downloads_directory_resolves_to_a_real_folder_when_not_overridden()
    {
        var saved = Naming.DownloadsOverride;
        Naming.DownloadsOverride = null;
        try
        {
            var dl = Naming.DownloadsDirectory();
            Assert.True(Path.IsPathRooted(dl));
            Assert.EndsWith("Downloads", dl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Naming.DownloadsOverride = saved;
        }
    }
}
