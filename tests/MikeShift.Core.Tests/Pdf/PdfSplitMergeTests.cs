using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Pdf;
using static MikeShift.Core.Tests.Pdf.PdfTestHelpers;

namespace MikeShift.Core.Tests.Pdf;

public class PdfSplitMergeTests
{
    [Fact]
    public async Task Split_writes_one_pdf_per_page_into_a_split_folder()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("report.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(pdf);

        var output = await PdfConverter.SplitAsync(pdf, default);

        Assert.Equal(tmp.Combine("report split"), output);
        var files = Directory.GetFiles(output).Order().ToArray();
        Assert.Equal(["report 01.pdf", "report 02.pdf", "report 03.pdf"], files.Select(p => Path.GetFileName(p)!).ToArray());
        Assert.All(files, f => Assert.Equal(1, PageCount(f)));
    }

    [Fact]
    public async Task Split_pages_keep_their_own_content_and_size()
    {
        using var tmp = new TempDir();
        var merged = await PdfConverter.MergeAsync(
            [tmp.WriteFile("a.pdf", Fixtures.PdfText(1)), tmp.WriteFile("b.pdf", Fixtures.PdfLandscape())], default);

        var output = await PdfConverter.SplitAsync(merged, default);

        var second = Directory.GetFiles(output).Order().Last();
        var (w, h) = PageSize(second);
        Assert.True(w > h, $"landscape page lost its shape ({w} x {h})");
    }

    [Fact]
    public async Task Split_folder_is_uniquified_not_reused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("report.pdf", Fixtures.PdfText(2));
        var first = await PdfConverter.SplitAsync(pdf, default);
        var second = await PdfConverter.SplitAsync(pdf, default);
        Assert.Equal(tmp.Combine("report split"), first);
        Assert.Equal(tmp.Combine("report split 2"), second);
    }

    [Fact]
    public async Task Merge_puts_pages_in_selection_order_in_Merged_pdf_beside_the_first_file()
    {
        using var tmp = new TempDir();
        tmp.CreateDir("one");
        tmp.CreateDir("two");
        var landscape = tmp.WriteFile("one/first.pdf", Fixtures.PdfLandscape());
        var text = tmp.WriteFile("two/second.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(landscape, text);

        var output = await PdfConverter.MergeAsync([landscape, text], default);

        Assert.Equal(tmp.Combine("one", "Merged.pdf"), output);
        Assert.Equal(4, PageCount(output));
        var (w0, h0) = PageSize(output, 0);
        var (w1, h1) = PageSize(output, 1);
        Assert.True(w0 > h0, "first page should be the landscape one");
        Assert.True(w1 < h1);
    }

    [Fact]
    public async Task Merge_action_joins_the_files_in_the_order_the_user_chose()
    {
        using var tmp = new TempDir();
        tmp.CreateDir("one");
        tmp.CreateDir("two");
        var landscape = tmp.WriteFile("one/first.pdf", Fixtures.PdfLandscape());
        var text = tmp.WriteFile("two/second.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(landscape, text);
        var ask = new FakeInteraction { Order = paths => [.. paths.Reverse()] };
        var merge = ActionCatalog.Find(PdfActions.All, "Merge")!;

        var result = await merge.Run([landscape, text], new ActionContext(Capabilities.All, ask), default);

        Assert.Equal([landscape, text], ask.OrderAsked);
        var output = Assert.Single(result.Outputs);
        Assert.Equal(tmp.Combine("two", "Merged.pdf"), output.Path); // beside the first file of the chosen order
        Assert.Equal(text, output.Input);
        Assert.Equal(4, PageCount(output.Path));
        var (w0, h0) = PageSize(output.Path, 0);
        var (w3, h3) = PageSize(output.Path, 3);
        Assert.True(w0 < h0, "the first page should now be the portrait one");
        Assert.True(w3 > h3, "the last page should now be the landscape one");
    }

    [Fact]
    public async Task Merge_action_cancelled_in_the_order_dialog_writes_nothing()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", Fixtures.PdfText(1));
        var b = tmp.WriteFile("b.pdf", Fixtures.PdfText(1));
        var merge = ActionCatalog.Find(PdfActions.All, "Merge")!;

        var result = await merge.Run([a, b], new ActionContext(Capabilities.All, new FakeInteraction { Order = _ => null }), default);

        Assert.True(result.Cancelled);
        Assert.False(File.Exists(tmp.Combine("Merged.pdf")));
        Assert.Equal(["a.pdf", "b.pdf"], Directory.GetFileSystemEntries(tmp.Path).Select(p => Path.GetFileName(p)!).Where(n => n != "__downloads").Order().ToArray());
    }

    [Fact]
    public async Task Merge_never_overwrites_an_earlier_Merged_pdf()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.pdf", Fixtures.PdfText(1));
        var b = tmp.WriteFile("b.pdf", Fixtures.PdfText(2));
        var first = await PdfConverter.MergeAsync([a, b], default);
        var second = await PdfConverter.MergeAsync([b, a], default);
        Assert.Equal(tmp.Combine("Merged.pdf"), first);
        Assert.Equal(tmp.Combine("Merged 2.pdf"), second);
        Assert.Equal(3, PageCount(first));
        Assert.Equal(3, PageCount(second));
    }

    [Fact]
    public async Task Merge_names_the_bad_file_when_one_input_is_unusable()
    {
        using var tmp = new TempDir();
        var good = tmp.WriteFile("good.pdf", Fixtures.PdfText(1));
        var locked = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeAsync([good, locked], default));

        Assert.Contains("locked.pdf", ex.Message);
        Assert.Contains("password", ex.Message);
        Assert.False(File.Exists(tmp.Combine("Merged.pdf")));
    }

    [Fact]
    public async Task Split_of_an_encrypted_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.SplitAsync(pdf, default));
        Assert.Contains("password", ex.Message);
        Assert.False(Directory.Exists(tmp.Combine("locked split")));
    }

    [Fact]
    public async Task Split_and_merge_of_a_corrupt_pdf_are_refused()
    {
        using var tmp = new TempDir();
        var bad = tmp.WriteFile("broken.pdf", Fixtures.PdfCorrupt());
        var good = tmp.WriteFile("good.pdf", Fixtures.PdfText(1));

        var split = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.SplitAsync(bad, default));
        Assert.Contains("damaged", split.Message);
        var merge = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.MergeAsync([good, bad], default));
        Assert.Contains("broken.pdf", merge.Message);
    }

    [Fact]
    public async Task Cancellation_leaves_no_partial_output()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("report.pdf", Fixtures.PdfText(3));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfConverter.SplitAsync(pdf, cts.Token));

        Assert.Equal(["report.pdf"], Directory.GetFileSystemEntries(tmp.Path).Select(p => Path.GetFileName(p)!).Where(n => n != "__downloads").ToArray());
    }
}
