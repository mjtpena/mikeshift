using System.Diagnostics;
using System.IO.Compression;
using Shiftvert.Core;
using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Tests;

public class ZipTests
{
    private static Task<ActionResult> Run(IReadOnlyList<string> paths, CancellationToken ct = default) =>
        ZipAction.Def.Run(paths, new ActionContext(Capabilities.All), ct);

    private static string[] EntryNames(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Single_file_becomes_name_zip_beside_it_with_the_content()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("report.txt", "hello zip");
        using var guard = OriginalsGuard.Watch(file);

        var result = await Run([file]);

        var output = Assert.Single(result.Outputs).Path;
        Assert.Equal(tmp.Combine("report.zip"), output);
        Assert.False(result.AnyDownloadsFallback);
        using var z = ZipFile.OpenRead(output);
        var entry = Assert.Single(z.Entries);
        Assert.Equal("report.txt", entry.FullName);
        using var r = new StreamReader(entry.Open());
        Assert.Equal("hello zip", r.ReadToEnd());
    }

    [Fact]
    public async Task Folder_is_stored_under_its_own_name_with_subfolders_and_empty_folders()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("Trip 2024");
        tmp.WriteFile(Path.Combine("Trip 2024", "a.txt"), "a");
        tmp.WriteFile(Path.Combine("Trip 2024", "day1", "b.txt"), "b");
        tmp.CreateDir(Path.Combine("Trip 2024", "empty"));
        using var guard = OriginalsGuard.Watch(folder);

        var result = await Run([folder]);

        var output = Assert.Single(result.Outputs).Path;
        Assert.Equal(tmp.Combine("Trip 2024.zip"), output);
        Assert.Equal(
            ["Trip 2024/", "Trip 2024/a.txt", "Trip 2024/day1/", "Trip 2024/day1/b.txt", "Trip 2024/empty/"],
            EntryNames(output));
    }

    [Fact]
    public async Task Folder_name_with_dots_keeps_them()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("v1.2.3");
        tmp.WriteFile(Path.Combine("v1.2.3", "x.txt"), "x");
        var result = await Run([folder]);
        Assert.Equal(tmp.Combine("v1.2.3.zip"), result.Outputs[0].Path);
    }

    [Fact]
    public async Task Multiple_items_become_Archive_zip_beside_the_first()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("one/a.txt", "a");
        var b = tmp.WriteFile("two/b.txt", "b");
        var dir = tmp.CreateDir("three");
        tmp.WriteFile("three/c.txt", "c");

        var result = await Run([a, b, dir]);

        var output = Assert.Single(result.Outputs).Path;
        Assert.Equal(tmp.Combine("one", "Archive.zip"), output);
        Assert.Equal(["a.txt", "b.txt", "three/", "three/c.txt"], EntryNames(output));
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task Existing_zip_is_never_overwritten()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("report.txt", "x");
        File.WriteAllText(tmp.Combine("report.zip"), "precious");

        var first = (await Run([file])).Outputs[0].Path;
        var second = (await Run([file])).Outputs[0].Path;

        Assert.Equal(tmp.Combine("report 2.zip"), first);
        Assert.Equal(tmp.Combine("report 3.zip"), second);
        Assert.Equal("precious", File.ReadAllText(tmp.Combine("report.zip")));
    }

    [Fact]
    public async Task Same_file_name_from_different_folders_gets_distinct_entries()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("x/notes.txt", "1");
        var b = tmp.WriteFile("y/notes.txt", "2");
        var output = (await Run([a, b])).Outputs[0].Path;
        Assert.Equal(["notes 2.txt", "notes.txt"], EntryNames(output));
    }

    [Fact]
    public async Task Entry_timestamps_are_the_files_utc_times()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("dated.txt", "x");
        var stamp = new DateTime(2020, 5, 17, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        var output = (await Run([file])).Outputs[0].Path;

        using var z = ZipFile.OpenRead(output);
        // The archive carries a zone-less DOS clock; we store the UTC clock reading.
        Assert.Equal(stamp, DateTime.SpecifyKind(z.Entries[0].LastWriteTime.DateTime, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Timestamps_outside_the_dos_range_are_clamped_not_fatal()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("ancient.txt", "x");
        File.SetLastWriteTimeUtc(file, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await Run([file]);
        Assert.True(result.Succeeded);
        using var z = ZipFile.OpenRead(result.Outputs[0].Path);
        Assert.Equal(1980, z.Entries[0].LastWriteTime.Year);
    }

    [Fact]
    public async Task No_temp_file_is_left_behind_and_the_archive_is_not_inside_itself()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("proj");
        tmp.WriteFile("proj/a.txt", "a");
        var sibling = tmp.WriteFile("proj/b.txt", "b");

        // Zipping a file and its parent folder together puts Archive.zip (and its temp) inside the folder being zipped.
        var result = await Run([sibling, folder]);

        var output = result.Outputs[0].Path;
        Assert.Equal(Path.Combine(folder, "Archive.zip"), output);
        Assert.Equal(["b.txt", "proj/", "proj/a.txt", "proj/b.txt"], EntryNames(output));
        Assert.DoesNotContain(Directory.GetFiles(folder), f => f.EndsWith(".shiftvert-tmp"));
    }

    [Fact]
    public async Task Directory_junction_loops_are_skipped()
    {
        using var tmp = new TempDir();
        var root = tmp.CreateDir("root");
        tmp.WriteFile("root/real.txt", "real");
        var loop = Path.Combine(root, "loop");
        if (!TryCreateJunction(loop, root)) Assert.Fail("JUNCTION UNAVAILABLE");

        var done = Run([root]);
        var finished = await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(done, finished);

        var output = (await done).Outputs[0].Path;
        Assert.Equal(["root/", "root/real.txt"], EntryNames(output));
    }

    [Fact]
    public async Task Named_pipes_and_device_paths_are_refused()
    {
        var result = await Run([@"\\.\pipe\shiftvert-test-pipe"]);
        Assert.True(result.AllFailed);
        Assert.Contains("pipe", result.AllFailedMessage);
        Assert.Empty(result.Outputs);

        var device = await Run([@"\\.\PhysicalDrive0"]);
        Assert.True(device.AllFailed);
    }

    [Fact]
    public async Task Missing_items_are_reported_not_thrown()
    {
        using var tmp = new TempDir();
        var result = await Run([tmp.Combine("gone.txt")]);
        Assert.True(result.AllFailed);
        Assert.Contains("gone.txt", result.AllFailedMessage);
    }

    [Fact]
    public async Task Cancellation_leaves_no_output_and_no_temp()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("big.bin", new byte[1024]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Run([file], cts.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Outputs);
        Assert.Equal(["big.bin"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public async Task Unwritable_folder_falls_back_to_downloads_and_says_so()
    {
        using var tmp = new TempDir();
        var locked = tmp.CreateDir("ro");
        var file = tmp.WriteFile("ro/a.txt", "a");

        ActionResult result;
        using (TempDir.DenyWrites(locked))
            result = await Run([file]);

        var output = Assert.Single(result.Outputs);
        Assert.True(output.UsedDownloadsFallback);
        Assert.Equal(tmp.Combine("__downloads", "a.zip"), output.Path);
        Assert.EndsWith("in Downloads", result.ToastText);
    }

    [Fact]
    public void Zip_is_a_tool_tile_with_a_glyph_and_is_not_interactive()
    {
        Assert.Equal(WheelPage.Tool, ZipAction.Def.Page);
        Assert.False(string.IsNullOrEmpty(ZipAction.Def.Glyph));
        Assert.False(ZipAction.Def.Interactive);
        Assert.Equal("Zip", ZipAction.Def.Title);
        Assert.True(ZipAction.Def.IsVisibleFor([@"C:\x\anything.zzz"], Capabilities.None));
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
