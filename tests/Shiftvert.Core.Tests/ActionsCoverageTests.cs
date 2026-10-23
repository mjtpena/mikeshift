using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using Shiftvert.Core;
using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Tests;

/// <summary>Edge and error branches of the Actions layer (results, batching, catalog lookup, special files, zip, naming, kind).</summary>
public class ActionsCoverageTests
{
    // ------------------------------------------------------------------ ActionResult

    [Fact]
    public void Single_wraps_a_commit_result_as_one_output()
    {
        var r = ActionResult.Single(1, @"C:\a\in.png", new CommitResult(@"C:\a\out.jpg", false));
        Assert.Equal(1, r.Total);
        var o = Assert.Single(r.Outputs);
        Assert.Equal(@"C:\a\in.png", o.Input);
        Assert.Equal(@"C:\a\out.jpg", o.Path);
        Assert.False(o.UsedDownloadsFallback);
        Assert.True(r.Succeeded);
        Assert.Equal("out.jpg", r.ToastText);
    }

    [Fact]
    public void Single_carries_the_downloads_fallback_into_the_toast()
    {
        var r = ActionResult.Single(1, @"C:\a\in.png", new CommitResult(@"C:\dl\in.jpg", true));
        Assert.True(r.AnyDownloadsFallback);
        Assert.Equal("in.jpg · in Downloads", r.ToastText);
    }

    [Fact]
    public void Failed_is_an_all_failed_result_whose_message_depends_on_the_batch_size()
    {
        var one = ActionResult.Failed(1, @"C:\a\x.png", "no luck");
        Assert.True(one.AllFailed);
        Assert.Equal("no luck", one.AllFailedMessage);
        Assert.Equal("no luck", one.ToastText);

        var many = ActionResult.Failed(4, @"C:\a\x.png", "no luck");
        Assert.Equal("None of the 4 files could be converted: no luck", many.AllFailedMessage);
        Assert.Equal(["x.png: no luck"], many.FailureLines);
    }

    [Fact]
    public void Toast_of_a_cancelled_run_and_of_an_empty_run()
    {
        Assert.Equal("Cancelled", new ActionResult { Total = 2, Cancelled = true }.ToastText);
        Assert.Equal("", new ActionResult { Total = 0 }.ToastText);
        // Only "already" skips: the note stands alone, with no leading separator.
        Assert.Equal("2 already converted", new ActionResult { Total = 2, AlreadyInFormat = 2 }.ToastText);
    }

    [Fact]
    public void AllFailedMessage_is_null_unless_everything_failed()
    {
        Assert.Null(new ActionResult { Total = 1 }.AllFailedMessage);
        Assert.Null(new ActionResult
        {
            Total = 2,
            Outputs = [new ActionOutput("a", "b", false)],
            Failures = [new ActionFailure("c", "x")],
        }.AllFailedMessage);
    }

    [Fact]
    public void Failure_lines_are_empty_without_failures_and_not_capped_at_exactly_six()
    {
        Assert.Empty(new ActionResult().FailureLines);
        var six = new ActionResult { Failures = [.. Enumerable.Range(1, 6).Select(i => new ActionFailure($@"C:\a\{i}.png", "bad"))] };
        Assert.Equal(6, six.FailureLines.Count);
        Assert.DoesNotContain(six.FailureLines, l => l.StartsWith('…'));
    }

    // ------------------------------------------------------------------ Each

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "Access to the file was denied.")]
    [InlineData(typeof(FileNotFoundException), "The file could not be found.")]
    [InlineData(typeof(DirectoryNotFoundException), "The file could not be found.")]
    public void Unexpected_exceptions_get_a_plain_language_message(Type type, string expected)
    {
        var ex = (Exception)Activator.CreateInstance(type, "technical detail")!;
        Assert.Equal(expected, Each.DescribeUnexpected(ex));
    }

    [Fact]
    public void Unexpected_exception_messages_fall_back_to_the_exception_text()
    {
        Assert.Equal("This conversion is not implemented yet (soon).", Each.DescribeUnexpected(new NotImplementedException("soon")));
        Assert.Equal("kaput", Each.DescribeUnexpected(new InvalidOperationException("kaput")));
    }

    [Fact]
    public void UsedFallback_compares_parent_folders_ignoring_case_and_trailing_separators()
    {
        Assert.False(Each.UsedFallback(@"C:\Photos\a.heic", @"c:\photos\A.png"));
        Assert.False(Each.UsedFallback(@"C:\Photos\a.heic", @"C:\Photos\a pages\"));
        Assert.True(Each.UsedFallback(@"C:\Photos\a.heic", @"C:\Users\me\Downloads\a.png"));
    }

    [Fact]
    public void UsedFallback_never_throws_on_unusable_paths()
    {
        Assert.False(Each.UsedFallback("in\0put", "out\0put"));
    }

    [Fact]
    public async Task Each_with_a_null_skip_set_and_no_progress_converts_everything()
    {
        var seen = new List<string>();
        var result = await Each.RunAsync(
            [@"C:\a\1.png", @"C:\a\2.png"], new ActionContext(Capabilities.All),
            (input, _) => { seen.Add(input); return Task.FromResult(input + ".out"); }, CancellationToken.None);
        Assert.Equal(2, result.Outputs.Count);
        Assert.Equal(0, result.AlreadyInFormat);
        Assert.Equal([@"C:\a\1.png", @"C:\a\2.png"], seen);
    }

    [Fact]
    public async Task Each_with_an_already_cancelled_token_does_nothing_and_reports_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var called = false;
        var result = await Each.RunAsync(
            [@"C:\a\1.png"], new ActionContext(Capabilities.All),
            (_, _) => { called = true; return Task.FromResult("x"); }, cts.Token);
        Assert.False(called);
        Assert.True(result.Cancelled);
        Assert.False(result.Succeeded);
        Assert.False(result.AllFailed);
    }

    // ------------------------------------------------------------------ IInteraction / ActionContext

    [Fact]
    public async Task NoInteraction_cancels_every_question()
    {
        Assert.Null(await NoInteraction.Instance.ChooseCropAsync("x.png", CancellationToken.None));
        Assert.Null(await NoInteraction.Instance.ChooseRangeAsync("x.wav", CancellationToken.None));
    }

    [Fact]
    public async Task Require_methods_throw_cancellation_when_the_user_said_no_and_return_the_answer_otherwise()
    {
        var none = new ActionContext(Capabilities.All);
        await Assert.ThrowsAsync<OperationCanceledException>(() => none.RequireCropAsync("x.png", CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() => none.RequireRangeAsync("x.wav", CancellationToken.None));

        var crop = new CropChoice(new RectPx(1, 2, 3, 4), 3, 4);
        var range = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var yes = new ActionContext(Capabilities.All, new Infrastructure.FakeInteraction(crop, range));
        Assert.Equal(crop, await yes.RequireCropAsync("x.png", CancellationToken.None));
        var got = await yes.RequireRangeAsync("x.wav", CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(2), got.Duration);
    }

    // ------------------------------------------------------------------ ActionCatalog

    [Fact]
    public void ListFor_other_is_empty_and_the_others_are_not()
    {
        Assert.Empty(ActionCatalog.ListFor(Category.Other));
        foreach (var c in new[] { Category.Image, Category.Pdf, Category.Video, Category.Audio, Category.Document })
            Assert.NotEmpty(ActionCatalog.ListFor(c));
    }

    [Fact]
    public void Find_ignores_case_and_surrounding_spaces_and_misses_cleanly()
    {
        var list = ActionCatalog.ListFor(Category.Image);
        Assert.Equal("PNG", ActionCatalog.Find(list, "  png ")?.Title);
        Assert.Null(ActionCatalog.Find(list, "no such tile"));
        Assert.Null(ActionCatalog.Find([], "PNG"));
    }

    [Fact]
    public void CommonCategory_is_null_for_mixed_unknown_and_empty_selections()
    {
        Assert.Null(ActionCatalog.CommonCategory([], _ => Category.Image));
        Assert.Equal(Category.Pdf, ActionCatalog.CommonCategory(["a", "b"], _ => Category.Pdf));
        Assert.Null(ActionCatalog.CommonCategory(["a", "b"], p => p == "a" ? Category.Pdf : Category.Image));
        Assert.Null(ActionCatalog.CommonCategory(["a", "b"], p => p == "b" ? Category.Other : Category.Pdf));
    }

    [Fact]
    public void An_injected_classifier_with_an_empty_selection_offers_nothing_and_with_an_unknown_one_only_zip()
    {
        Assert.Empty(ActionCatalog.For([], Capabilities.All, _ => Category.Image));
        var tiles = ActionCatalog.For(["x.zzz"], Capabilities.All, _ => Category.Other);
        Assert.Equal([ZipAction.Title], tiles.Select(t => t.Title));
        Assert.Throws<ArgumentNullException>(() => ActionCatalog.For(null!, Capabilities.All, _ => Category.Image));
        Assert.Throws<ArgumentNullException>(() => ActionCatalog.For(["x"], null!, _ => Category.Image));
        Assert.Throws<ArgumentNullException>(() => ActionCatalog.For((IReadOnlyList<string>)null!, Capabilities.All));
    }

    // ------------------------------------------------------------------ SpecialFiles

    [Theory]
    [InlineData(@"\\.\pipe\x", true)]
    [InlineData(@"\\.\PhysicalDrive0", true)]
    [InlineData(@"\\?\C:\Users\me\file.txt", false)]
    [InlineData(@"\\?\c:\x", false)]
    [InlineData(@"\\?\UNC\server\share\file.txt", false)]
    [InlineData(@"\\?\unc\server\share", false)]
    [InlineData(@"\\?\GLOBALROOT\Device\Foo", true)]
    [InlineData(@"\\?\pipe\x", true)]
    [InlineData(@"\\?\1:\x", true)]
    [InlineData(@"C:\Users\me\file.txt", false)]
    [InlineData(@"\\server\share\file.txt", false)]
    [InlineData("relative.txt", false)]
    public void Device_namespace_paths_are_recognised(string path, bool device) =>
        Assert.Equal(device, SpecialFiles.IsDeviceNamespacePath(path));

    [Fact]
    public void Regular_files_folders_and_missing_paths_are_not_special_and_have_no_reparse_tag()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.txt", "x");
        Assert.False(SpecialFiles.IsSpecial(file));
        Assert.False(SpecialFiles.IsSpecial(tmp.Path));
        Assert.False(SpecialFiles.IsSpecial(tmp.Combine("missing.txt")));
        Assert.Equal(0u, SpecialFiles.ReparseTag(file));
        Assert.Equal(0u, SpecialFiles.ReparseTag(tmp.Path));
        Assert.Equal(0u, SpecialFiles.ReparseTag(tmp.Combine("missing.txt")));
        Assert.True(SpecialFiles.IsSpecial(@"\\.\pipe\whatever"));
    }

    [Fact]
    public void A_junction_has_a_name_surrogate_tag_but_is_not_a_special_file()
    {
        using var tmp = new TempDir();
        var target = tmp.CreateDir("target");
        var link = tmp.Combine("link");
        if (!TryMakeJunction(link, target)) return; // junctions unavailable: nothing to assert on this machine

        var tag = SpecialFiles.ReparseTag(link);
        Assert.Equal(0xA0000003u, tag);
        Assert.True(SpecialFiles.IsNameSurrogate(tag));
        Assert.False(SpecialFiles.IsSpecial(link));
    }

    [Fact]
    public void Long_paths_get_the_extended_length_prefix_and_short_ones_do_not()
    {
        Assert.Equal(@"C:\short\file.txt", SpecialFiles.ToExtendedLength(@"C:\short\file.txt"));

        var longName = new string('a', 120);
        var local = $@"C:\{longName}\{longName}\file.txt";
        Assert.Equal(@"\\?\" + local, SpecialFiles.ToExtendedLength(local));

        var unc = $@"\\server\share\{longName}\{longName}\file.txt";
        Assert.Equal(@"\\?\UNC\server\share\" + $@"{longName}\{longName}\file.txt", SpecialFiles.ToExtendedLength(unc));

        var already = @"\\?\C:\" + longName + @"\" + longName + @"\x.txt";
        Assert.Equal(already, SpecialFiles.ToExtendedLength(already));
    }

    // ------------------------------------------------------------------ ZipAction

    [Fact]
    public async Task Creating_a_zip_of_nothing_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ZipAction.CreateAsync([], CancellationToken.None));
        Assert.Equal("Nothing to zip.", ex.Message);
    }

    [Fact]
    public async Task A_file_another_program_holds_open_exclusively_is_reported_and_leaves_no_archive()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("locked.txt", "data");
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await ZipAction.Def.Run([file], new ActionContext(Capabilities.All), CancellationToken.None);

        Assert.True(result.AllFailed);
        Assert.Contains("Couldn't read \"locked.txt\"", result.AllFailedMessage);
        Assert.Equal(["__downloads", "locked.txt"], Directory.GetFileSystemEntries(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray());
    }

    [Fact]
    public async Task A_locked_file_inside_a_folder_fails_the_whole_zip_instead_of_producing_a_partial_archive()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("docs");
        tmp.WriteFile("docs/ok.txt", "fine");
        var bad = tmp.WriteFile("docs/zz-locked.txt", "data");
        using var hold = new FileStream(bad, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await ZipAction.Def.Run([folder], new ActionContext(Capabilities.All), CancellationToken.None);

        Assert.True(result.AllFailed);
        Assert.False(File.Exists(tmp.Combine("docs.zip")));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_listed_is_reported_with_its_name()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("secret");
        tmp.WriteFile("secret/a.txt", "a");
        ActionResult result;
        using (DenyRead(folder))
            result = await ZipAction.Def.Run([folder], new ActionContext(Capabilities.All), CancellationToken.None);

        Assert.True(result.AllFailed);
        Assert.Contains("Couldn't read the folder \"secret\"", result.AllFailedMessage);
        Assert.False(File.Exists(tmp.Combine("secret.zip")));
    }

    [Fact]
    public async Task Already_compressed_files_are_stored_and_text_is_deflated()
    {
        using var tmp = new TempDir();
        var text = tmp.WriteFile("big.txt", new string('a', 50_000));
        var png = tmp.WriteFile("pic.PNG", new string('b', 50_000));
        var output = (await ZipAction.Def.Run([text, png], new ActionContext(Capabilities.All), CancellationToken.None)).Outputs[0].Path;

        using var z = ZipFile.OpenRead(output);
        Assert.True(z.GetEntry("big.txt")!.CompressedLength < 1000);
        Assert.Equal(50_000, z.GetEntry("pic.PNG")!.CompressedLength);
    }

    [Fact]
    public async Task The_same_folder_selected_twice_gets_two_distinct_top_level_entries()
    {
        using var tmp = new TempDir();
        var a = tmp.CreateDir("one/photos");
        var b = tmp.CreateDir("two/photos");
        tmp.WriteFile("one/photos/x.txt", "1");
        tmp.WriteFile("two/photos/x.txt", "2");
        var output = (await ZipAction.Def.Run([a, b], new ActionContext(Capabilities.All), CancellationToken.None)).Outputs[0].Path;
        using var z = ZipFile.OpenRead(output);
        var names = z.Entries.Select(e => e.FullName).Order().ToArray();
        Assert.Equal(["photos 2/", "photos 2/x.txt", "photos/", "photos/x.txt"], names);
    }

    [Fact]
    public async Task A_trailing_separator_on_a_folder_path_is_tolerated()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("trail");
        tmp.WriteFile("trail/a.txt", "a");
        var output = (await ZipAction.Def.Run([folder + @"\"], new ActionContext(Capabilities.All), CancellationToken.None)).Outputs[0].Path;
        Assert.Equal(tmp.Combine("trail.zip"), output);
    }

    // ------------------------------------------------------------------ Naming

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData("", "")]
    [InlineData(".", ".")]
    public void Extensions_are_normalised(string input, string expected) => Assert.Equal(expected, Naming.NormalizeExtension(input));

    [Fact]
    public void Plan_for_a_root_level_file_keeps_the_root_as_its_directory()
    {
        var spec = Naming.Plan(@"C:\a.png", "jpg", " small");
        Assert.Equal(@"C:\", spec.Directory);
        Assert.Equal("a small", spec.BaseName);
        Assert.Equal(".jpg", spec.Extension);
        Assert.False(spec.IsFolder);
        Assert.True(Naming.Plan(@"C:\a.png", "").IsFolder);
    }

    [Fact]
    public void Plan_strips_a_trailing_separator_from_a_folder_input()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("pages");
        var spec = Naming.Plan(folder + @"\", "");
        Assert.Equal(tmp.Path, spec.Directory);
        Assert.Equal("pages", spec.BaseName);
    }

    [Fact]
    public void Unique_gives_up_with_a_clear_error_when_ten_thousand_names_are_taken()
    {
        using var tmp = new TempDir();
        var dir = tmp.CreateDir("crowded");
        File.WriteAllBytes(Path.Combine(dir, "a.txt"), []);
        for (var n = 2; n <= 10_000; n++) File.WriteAllBytes(Path.Combine(dir, $"a {n}.txt"), []);

        var ex = Assert.Throws<ConversionException>(() => Naming.Unique(dir, "a", ".txt"));
        Assert.Contains("free file name", ex.Message);
    }

    [Fact]
    public void Without_an_override_downloads_is_the_users_real_downloads_folder()
    {
        using var tmp = new TempDir(); // would redirect the lookup...
        Naming.DownloadsOverride = null; // ...so clear it for this async flow; TempDir.Dispose restores its own previous value
        var real = Naming.DownloadsDirectory();
        Assert.True(Path.IsPathRooted(real));
        Assert.EndsWith("Downloads", real, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Committing_into_a_directory_that_does_not_exist_yet_creates_it_instead_of_falling_back()
    {
        using var tmp = new TempDir();
        var spec = new OutputSpec(tmp.Combine("does", "not", "exist"), "out", ".txt");
        var result = await Naming.CommitStreamAsync(spec, async (s, ct) => await s.WriteAsync("hi"u8.ToArray(), ct), CancellationToken.None);
        Assert.False(result.UsedDownloadsFallback);
        Assert.Equal(tmp.Combine("does", "not", "exist", "out.txt"), result.Path);
        Assert.Equal("hi", File.ReadAllText(result.Path));
        Assert.Equal(["out.txt"], Directory.GetFileSystemEntries(tmp.Combine("does", "not", "exist")).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public async Task Committing_with_neither_the_target_nor_downloads_writable_fails_with_both_named()
    {
        using var tmp = new TempDir();
        var target = tmp.CreateDir("t");
        using var d1 = TempDir.DenyWrites(target);
        using var d2 = TempDir.DenyWrites(tmp.Downloads);
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            Naming.CommitStreamAsync(new OutputSpec(target, "x", ".txt"), (s, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Contains("Downloads isn't writable", ex.Message);
    }

    [Fact]
    public async Task Committing_checks_cancellation_before_touching_the_disk()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Naming.CommitAsync(new OutputSpec(tmp.Path, "x", ".txt"), (_, _) => Task.CompletedTask, cts.Token));
        Assert.Equal(["__downloads"], Directory.GetFileSystemEntries(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
        await Assert.ThrowsAsync<ArgumentNullException>(() => Naming.CommitAsync(new OutputSpec(tmp.Path, "x", ".txt"), null!, CancellationToken.None));
    }

    // ------------------------------------------------------------------ Kind

    [Fact]
    public void A_pdf_extension_on_a_file_that_cannot_be_read_is_still_trusted_as_a_pdf()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("held.pdf", Fixtures.PdfStub());
        Kind.ClearCache();
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(Category.Pdf, Kind.Of(file));
    }

    [Fact]
    public void A_transport_stream_that_is_unreadable_or_too_short_is_not_video()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var tiny = tmp.WriteFile("tiny.ts", new byte[] { 0x47, 0, 0, 0 });
        Assert.Equal(Category.Other, Kind.Of(tiny));

        var held = tmp.WriteFile("held.ts", Fixtures.MpegTs(8));
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(Category.Other, Kind.Of(held));
    }

    [Fact]
    public void M2ts_sync_bytes_sit_after_a_four_byte_timestamp_in_192_byte_packets()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var good = new byte[192 * 6];
        for (var i = 0; i < 6; i++) good[4 + i * 192] = 0x47;
        var badFile = tmp.WriteFile("plain.m2ts", Fixtures.MpegTs(8)); // 188-byte layout: wrong for .m2ts
        var goodFile = tmp.WriteFile("ok.m2ts", good);
        // Both fail the media probe (no real video inside), but only the second one gets as far as asking it.
        Assert.Equal(Category.Other, Kind.Of(badFile));
        Assert.Equal(Category.Other, Kind.Of(goodFile));
    }

    [Fact]
    public void A_large_uniform_selection_is_confirmed_by_sampling_and_a_planted_wrong_file_is_found_in_the_sample()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var png = Fixtures.Png();
        var paths = Enumerable.Range(0, 40).Select(i => tmp.WriteFile($"p{i:00}.png", png)).ToList();
        Assert.Equal(Category.Image, Kind.CommonOf(paths));

        // The last file is always checked: a misnamed final file demotes the selection to the exhaustive check, which spots it.
        File.WriteAllBytes(paths[^1], Fixtures.Noise(64));
        Kind.ClearCache();
        Assert.Null(Kind.CommonOf(paths));
    }

    [Fact]
    public void A_large_selection_with_a_folder_or_missing_file_or_mixed_extensions_is_sniffed_exhaustively()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var png = Fixtures.Png();
        var paths = Enumerable.Range(0, 20).Select(i => tmp.WriteFile($"p{i:00}.png", png)).ToList();

        var withFolder = paths.ToList();
        withFolder[7] = tmp.CreateDir("looks-like.png");
        Assert.Null(Kind.CommonOf(withFolder));

        var withMissing = paths.ToList();
        withMissing[3] = tmp.Combine("vanished.png");
        Assert.Null(Kind.CommonOf(withMissing));

        var mixed = paths.ToList();
        mixed[5] = tmp.WriteFile("note.txt", "hello");
        Assert.Null(Kind.CommonOf(mixed));

        var firstUnknown = paths.ToList();
        firstUnknown[0] = tmp.WriteFile("blob.zzz", "x");
        Assert.Null(Kind.CommonOf(firstUnknown));
    }

    [Fact]
    public void A_large_ts_family_selection_cannot_be_decided_by_extension_alone()
    {
        using var tmp = new TempDir();
        Kind.ClearCache();
        var paths = Enumerable.Range(0, 20).Select(i => tmp.WriteFile($"s{i:00}.ts", "export const x = 1;")).ToList();
        Assert.Null(Kind.CommonOf(paths)); // TypeScript, not video
    }

    [Fact]
    public void CommonOf_rejects_null_and_treats_an_empty_selection_as_undecided()
    {
        Assert.Throws<ArgumentNullException>(() => Kind.CommonOf(null!));
        Assert.Null(Kind.CommonOf([]));
    }

    [Fact]
    public void Hidden_or_odd_paths_never_throw_from_Of()
    {
        Assert.Equal(Category.Other, Kind.Of("bad\0name.png"));
        Assert.Equal(Category.Other, Kind.Of(@"\\.\pipe\not-a-file"));
        Assert.Equal(Category.Other, Kind.Of(""));
    }

    // ------------------------------------------------------------------ helpers

    private static bool TryMakeJunction(string link, string target)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Denies listing and reading <paramref name="directory"/> to the current user until disposed.</summary>
    private static IDisposable DenyRead(string directory) => new DenyReadScope(directory);

    private sealed class DenyReadScope : IDisposable
    {
        private readonly DirectoryInfo _dir;
        private readonly FileSystemAccessRule _rule;

        public DenyReadScope(string directory)
        {
            _dir = new DirectoryInfo(directory);
            _rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory | FileSystemRights.ReadData,
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
