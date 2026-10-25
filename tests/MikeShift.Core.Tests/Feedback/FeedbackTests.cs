using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;

namespace MikeShift.Core.Tests.Feedback;

public class FeedbackTests
{
    private static ActionOutput Out(string name) => new($@"C:\a\{name}.heic", $@"C:\a\{name}.png", false);

    private static ActionResult Ok(int n) => new() { Total = n, Outputs = [.. Enumerable.Range(1, n).Select(i => Out(i.ToString()))] };

    private static ActionResult Failures(int total, int failures, int saved = 0) => new()
    {
        Total = total,
        Outputs = [.. Enumerable.Range(1, saved).Select(i => Out("ok" + i))],
        Failures = [.. Enumerable.Range(1, failures).Select(i => new ActionFailure($@"C:\a\bad{i}.heic", $"reason {i}"))],
    };

    private static ActionDef Def(string title, WheelPage page) => new(title, page, (_, _, _) => Task.FromResult(new ActionResult()));

    [Fact]
    public void Success_shows_toast_and_chimes()
    {
        var plan = FeedbackPlan.For(Ok(3), 0, chimeEnabled: true, toastEnabled: true);
        Assert.Equal("Saved 3 files", plan.Toast!.Title);
        Assert.Equal("Next to the originals", plan.Toast.Detail);
        Assert.Equal(ToastKind.Success, plan.Toast.Kind);
        Assert.Equal(@"C:\a\1.png", plan.Toast.RevealPath);
        Assert.Null(plan.Dialog);
        Assert.True(plan.Chime);
    }

    [Fact]
    public void A_single_file_names_the_output_and_says_where_it_went()
    {
        var toast = FeedbackPlan.ToastFor(Ok(1));
        Assert.Equal("Saved 1.png · Next to the original", toast.Text);
        Assert.Equal("Saved 1.png. Next to the original", toast.SpokenText);
        Assert.True(toast.HasAction);
    }

    [Fact]
    public void Preferences_gate_toast_and_chime()
    {
        var plan = FeedbackPlan.For(Ok(1), 0, chimeEnabled: false, toastEnabled: false);
        Assert.Null(plan.Toast);
        Assert.False(plan.Chime);
    }

    [Fact]
    public void Other_jobs_add_a_more_in_progress_note()
    {
        var plan = FeedbackPlan.For(Ok(2), 2, true, true);
        Assert.Equal("Saved 2 files · Next to the originals · 2 more in progress", plan.Toast!.Text);
        Assert.Equal("Still working…", FeedbackPlan.StillWorkingToast(1).Title);
        Assert.Equal("1 more in progress", FeedbackPlan.StillWorkingToast(1).Detail);
    }

    [Fact]
    public void Start_toast_says_what_is_happening_in_the_progressive_tense()
    {
        var png = FeedbackPlan.StartToast(Def("PNG", WheelPage.Format), 0);
        Assert.Equal("Converting to PNG…", png.Title);
        Assert.Equal(ToastKind.Progress, png.Kind);
        Assert.Null(png.Detail);
        Assert.Equal("Compressing…", FeedbackPlan.StartToast(Def("Compress", WheelPage.Tool), 0).Title);
        Assert.Equal("Resizing to 50%…", FeedbackPlan.StartToast(Def("Resize 50%", WheelPage.Tool), 0).Title);
        Assert.Equal("Removing EXIF data…", FeedbackPlan.StartToast(Def("Strip EXIF", WheelPage.Tool), 0).Title);
        Assert.Equal("Zipping…", FeedbackPlan.StartToast(Def("Zip", WheelPage.Tool), 0).Title);
        Assert.Equal("1 more in progress", FeedbackPlan.StartToast(Def("PNG", WheelPage.Format), 1).Detail);
    }

    [Fact]
    public void Progress_toast_counts_files_and_names_the_current_one()
    {
        var t = FeedbackPlan.ProgressToast(Def("PNG", WheelPage.Format), "photo.heic", 2, 5, 1);
        Assert.Equal("Converting 2 of 5…", t.Title);
        Assert.Equal("photo.heic · 1 more in progress", t.Detail);
        Assert.Equal("Converting 5 of 5…", FeedbackPlan.ProgressToast(Def("PNG", WheelPage.Format), "x", 9, 5, 0).Title);
        Assert.Equal("Compressing 1 of 3…", FeedbackPlan.ProgressToast(Def("Compress", WheelPage.Tool), "x", 0, 3, 0).Title);
    }

    [Fact]
    public void All_failed_is_a_dialog_without_toast_or_chime()
    {
        var plan = FeedbackPlan.For(Failures(3, 3), 0, true, true);
        Assert.Null(plan.Toast);
        Assert.False(plan.Chime);
        var dialog = plan.Dialog!;
        Assert.Equal("None of the 3 files could be converted", dialog.Title);
        Assert.Null(dialog.Message);
        Assert.Equal(DialogSeverity.Error, dialog.Severity);
        Assert.Equal(["bad1.heic - reason 1", "bad2.heic - reason 2", "bad3.heic - reason 3"], dialog.Items.Select(i => i.Line));
        Assert.Equal([new DialogButton("Close", DialogCommand.Close)], dialog.Buttons);
    }

    [Fact]
    public void Single_file_failure_names_the_file_and_gives_the_reason()
    {
        var dialog = FeedbackPlan.For(Failures(1, 1), 0, true, true).Dialog!;
        Assert.Equal("Couldn't convert bad1.heic", dialog.Title);
        Assert.Equal("reason 1", dialog.Message);
        Assert.Empty(dialog.Items);
    }

    [Fact]
    public void A_damaged_file_gets_the_try_another_app_hint()
    {
        var r = ActionResult.Failed(1, @"C:\a\corrupt.pdf", "This PDF is damaged and can't be read.");
        var dialog = FeedbackPlan.DialogFor(r)!;
        Assert.Equal("Couldn't convert corrupt.pdf", dialog.Title);
        Assert.Equal("This PDF is damaged and can't be read. Try opening it in another app.", dialog.Message);
        // A reason that already says what to do is left alone, and other reasons get no hint.
        Assert.Equal("x damaged. Try opening it in another app.", FeedbackCopy.WithHint("x damaged. Try opening it in another app."));
        Assert.Equal("The file is locked.", FeedbackCopy.WithHint("The file is locked."));
    }

    [Fact]
    public void Long_converter_reasons_are_shortened_for_the_list()
    {
        const string longReason = "This image is too large (300 megapixels). MikeShift handles up to 200 megapixels.";
        Assert.Equal("Too large (300 megapixels; the limit is 200).", FeedbackCopy.ShortReason(longReason));
        Assert.Equal("Anything else", FeedbackCopy.ShortReason("Anything else"));
        var r = new ActionResult { Total = 1, Failures = [new ActionFailure(@"C:\a\huge-300mp.png", longReason)] };
        Assert.Equal("Too large (300 megapixels; the limit is 200).", FeedbackPlan.DialogFor(r)!.Message);
    }

    [Fact]
    public void Partial_failure_lists_at_most_six_and_keeps_toast_without_chime()
    {
        var plan = FeedbackPlan.For(Failures(10, 8, saved: 2), 0, true, true);
        Assert.Equal("Saved 2 of 10", plan.Toast!.Title);
        Assert.Equal("8 couldn't be converted", plan.Toast.Detail);
        Assert.Equal(ToastKind.Warning, plan.Toast.Kind);
        Assert.False(plan.Chime);

        var dialog = plan.Dialog!;
        Assert.Equal("2 of 10 files converted", dialog.Title);
        Assert.Equal(DialogSeverity.Warning, dialog.Severity);
        Assert.Equal(6 + 1, dialog.Items.Count); // 6 failures + "…and 2 more"
        Assert.Equal("bad1.heic - reason 1", dialog.Items[0].Line);
        Assert.Equal("…and 2 more", dialog.Items[^1].Line);
        Assert.Equal("The 2 converted files are next to the originals.", dialog.Footer);
        Assert.Equal(new DialogButton("Show in folder", DialogCommand.ShowInFolder, @"C:\a\ok1.png"), dialog.Buttons[0]);
        Assert.Equal(DialogCommand.Close, dialog.Buttons[^1].Command);
    }

    [Fact]
    public void Partial_footer_follows_the_downloads_fallback()
    {
        var r = new ActionResult
        {
            Total = 2,
            Outputs = [new ActionOutput(@"C:\a\1.jpg", @"C:\dl\1.png", true)],
            Failures = [new ActionFailure(@"C:\a\2.jpg", "bad")],
        };
        Assert.Equal("The converted file is in Downloads.", FeedbackPlan.DialogFor(r)!.Footer);
        Assert.Equal("1 of 2 files converted", FeedbackPlan.DialogFor(r)!.Title);
    }

    [Fact]
    public void Downloads_fallback_is_explained_and_downgrades_the_toast_to_a_warning()
    {
        var r = new ActionResult { Total = 1, Outputs = [new ActionOutput(@"C:\a\photo.jpg", @"C:\dl\photo.png", true)] };
        var toast = FeedbackPlan.ToastFor(r);
        Assert.Equal("Saved photo.png · In Downloads (that folder isn't writable)", toast.Text);
        Assert.Equal(ToastKind.Warning, toast.Kind);
    }

    [Fact]
    public void Cancelled_with_nothing_written_has_no_chime_and_a_cancel_toast()
    {
        var plan = FeedbackPlan.For(new ActionResult { Total = 2, Cancelled = true }, 0, true, true);
        Assert.Equal("Cancelled", plan.Toast!.Text);
        Assert.False(plan.Chime);
        Assert.Null(plan.Dialog);
        Assert.False(plan.Toast.HasAction);
    }

    [Fact]
    public void Nothing_at_all_reads_nothing_to_convert()
    {
        var plan = FeedbackPlan.For(new ActionResult { Total = 0 }, 0, true, true);
        Assert.Equal("Nothing to convert", plan.Toast!.Text);
        Assert.False(plan.Chime);
    }

    [Fact]
    public void Already_in_format_notes()
    {
        var r = new ActionResult { Total = 2, Outputs = [Out("1")], AlreadyInFormat = 1, AlreadyLabel = "PNG" };
        Assert.Equal("Saved 1.png · Next to the original · 1 already PNG", FeedbackPlan.For(r, 0, true, true).Toast!.Text);
        Assert.Equal("These files are already PNG", FeedbackPlan.ToastFor(new ActionResult { Total = 2, AlreadyInFormat = 2, AlreadyLabel = "PNG" }).Title);
    }

    [Fact]
    public void Toast_lifetime_gives_actions_time_to_be_used()
    {
        Assert.True(FeedbackPlan.ToastFor(Ok(1)).Lifetime > TimeSpan.FromSeconds(4));
        Assert.Equal(ToastContent.ProgressLifetime, new ToastContent("Converting…", null, ToastKind.Progress).Lifetime);
        Assert.True(ToastContent.ProgressLifetime >= TimeSpan.FromMinutes(1)); // stays while a long single-file job runs
    }

    [Fact]
    public void Elevation_copy_is_short_and_actionable()
    {
        Assert.Equal("MikeShift is running as administrator", FeedbackCopy.ElevatedTitle);
        Assert.Contains("Quit MikeShift", FeedbackCopy.ElevatedBody);
    }

    [Fact]
    public void Simple_dialog_has_one_close_button_and_plain_text()
    {
        var d = DialogContent.Simple("Title", "Body");
        Assert.Single(d.Buttons);
        Assert.Equal("Title" + Environment.NewLine + "Body", d.PlainText);
    }
}

public class JobLedgerTests
{
    [Fact]
    public void Tracks_running_jobs_and_last_outputs()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var first = now;
        var ledger = new JobLedger(() => now);
        Assert.Null(ledger.LastFinishedAt);
        Assert.Empty(ledger.LastOutputs);

        var a = ledger.Begin();
        var b = ledger.Begin();
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(2, ledger.RunningCount);
        Assert.Equal(1, ledger.OthersRunning(a));

        ledger.Complete(a, new ActionResult
        {
            Total = 1,
            Outputs = [new ActionOutput(@"C:\x\a.heic", @"C:\x\a.png", false)],
        });
        Assert.Equal(1, ledger.RunningCount);
        Assert.Equal([@"C:\x\a.png"], ledger.LastOutputs);
        Assert.Equal(first, ledger.LastFinishedAt);
        Assert.Equal(0, ledger.OthersRunning(b));

        // A job that wrote nothing leaves LastOutputs alone.
        now = now.AddMinutes(1);
        ledger.Complete(b, new ActionResult { Total = 1, Cancelled = true });
        Assert.Equal(0, ledger.RunningCount);
        Assert.Equal([@"C:\x\a.png"], ledger.LastOutputs);
        Assert.Equal(first, ledger.LastFinishedAt);
    }

    [Fact]
    public void Complete_with_null_result_still_frees_the_slot()
    {
        var ledger = new JobLedger();
        var id = ledger.Begin();
        ledger.Complete(id, null);
        Assert.Equal(0, ledger.RunningCount);
    }
}

public class CodecSummaryTests
{
    [Fact]
    public void All_capabilities_report_available_and_none_report_missing()
    {
        Assert.All(CodecSummary.From(Capabilities.All), c => Assert.True(c.Available));
        Assert.All(CodecSummary.From(Capabilities.None), c => Assert.False(c.Available));
        Assert.Contains(CodecSummary.From(Capabilities.None), c => c.Name.Contains("WebP") && c.Hint.Contains("Store"));
    }

    [Fact]
    public void Rows_have_text_state_an_accessible_name_and_a_store_link_when_there_is_something_to_install()
    {
        var webp = CodecSummary.From(Capabilities.None).Single(c => c.Name == "WebP output");
        Assert.Equal("Not installed", webp.StateText);
        Assert.Equal("WebP output, not installed", webp.AccessibleName);
        Assert.StartsWith("ms-windows-store://pdp/?ProductId=", webp.StoreUri);
        var ok = CodecSummary.From(Capabilities.All).Single(c => c.Name == "WebP output");
        Assert.Equal("Available", ok.StateText);
        Assert.Equal("WebP output, available", ok.AccessibleName);
        // Nothing to install for rows Windows itself must provide.
        Assert.Null(CodecSummary.From(Capabilities.None).Single(c => c.Name == "MP3 audio output").StoreUri);
    }

    [Fact]
    public void Headline_counts_what_is_missing_and_missing_rows_come_first()
    {
        Assert.Equal("All available", CodecSummary.Headline(CodecSummary.From(Capabilities.All)));
        var some = Capabilities.All with { WebpEncode = false, WebmOpen = false };
        var rows = CodecSummary.From(some);
        Assert.Equal("2 not installed", CodecSummary.Headline(rows));
        Assert.Equal(["WebP output", "WebM files"], CodecSummary.MissingFirst(rows).Take(2).Select(r => r.Name));
        Assert.All(CodecSummary.MissingFirst(rows).Skip(2), r => Assert.True(r.Available));
    }

    [Fact]
    public void A_changed_probe_is_detected()
    {
        Assert.False(CodecSummary.Differs(Capabilities.All, Capabilities.All));
        Assert.True(CodecSummary.Differs(Capabilities.None, Capabilities.All with { Av1Decode = false }));
    }
}

public class FileTypeFilterTests
{
    [Fact]
    public void First_entry_is_every_supported_extension_and_the_last_is_all_files()
    {
        var all = FileTypeFilters.All;
        Assert.Equal("Supported files", all[0].Name);
        Assert.Equal(new FileTypeFilter("All files", "*.*"), all[^1]);

        var patterns = all[0].Spec.Split(';').ToHashSet(StringComparer.Ordinal);
        foreach (var ext in Kind.ImageExtensions.Concat(Kind.VideoExtensions).Concat(Kind.AudioExtensions).Concat(Kind.DocumentExtensions).Append(Kind.PdfExtension))
            Assert.Contains("*." + ext, patterns);
        Assert.DoesNotContain("*.*", patterns);
    }

    [Fact]
    public void Each_kind_has_its_own_entry_and_specs_are_well_formed()
    {
        Assert.Equal(["Supported files", "Images", "Videos", "Audio", "PDF and documents", "All files"], FileTypeFilters.All.Select(f => f.Name));
        Assert.All(FileTypeFilters.All, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Spec));
            Assert.All(f.Spec.Split(';'), p => Assert.StartsWith("*.", p));
            Assert.Equal(f.Spec.Split(';').Length, f.Spec.Split(';').Distinct().Count());
        });
        Assert.Contains("*.heic", FileTypeFilters.All.Single(f => f.Name == "Images").Spec);
        Assert.Contains("*.pdf", FileTypeFilters.All.Single(f => f.Name == "PDF and documents").Spec);
    }

    [Fact]
    public void Dialog_wording_is_an_instruction()
    {
        Assert.Equal("Choose files to convert", FileTypeFilters.Title);
        Assert.Equal("Choose", FileTypeFilters.OkLabel);
    }
}
