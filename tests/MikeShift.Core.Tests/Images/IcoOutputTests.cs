using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using MikeShift.Core.Tests.Integrity;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>The ICO tile: a hand-written container, read back through WIC, which is not the code that wrote it.</summary>
public class IcoOutputTests
{
    private static readonly int[] AllSizes = [16, 24, 32, 48, 64, 128, 256];

    private static ActionDef Action() => ActionCatalog.Find(ImageActions.All, "ICO")!;

    private static async Task<uint> FrameCountAsync(string path)
    {
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync((await File.ReadAllBytesAsync(path)).AsBuffer());
        ms.Seek(0);
        return (await BitmapDecoder.CreateAsync(ms)).FrameCount;
    }

    /// <summary>Every frame WIC finds in the file, smallest first.</summary>
    private static async Task<List<Px>> FramesAsync(string path)
    {
        var frames = new List<Px>();
        for (var i = 0; i < await FrameCountAsync(path); i++)
            frames.Add(await PixDecode.WicAsync(path, i));
        return frames.OrderBy(f => f.W).ToList();
    }

    private static bool Near(Px px, int x, int y, (byte R, byte G, byte B) expected, int tol = 24)
    {
        var (b, g, r, _) = px.At(x, y);
        return Math.Abs(r - expected.R) <= tol && Math.Abs(g - expected.G) <= tol && Math.Abs(b - expected.B) <= tol;
    }

    [Fact]
    public async Task A_picture_becomes_an_ico_with_all_seven_sizes_that_windows_decodes()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("logo.png", Fixtures.PngRgb(300, 300));
        using var guard = OriginalsGuard.Watch(input);

        var (output, soft) = await ImageConverter.ToIcoAsync(input, default);

        Assert.Equal(tmp.Combine("logo.ico"), output);
        Assert.False(soft);
        guard.AssertUnchanged();
        Assert.Equal(7u, await FrameCountAsync(output));
        var frames = await FramesAsync(output);
        Assert.Equal(AllSizes, frames.Select(f => f.W));
        Assert.All(frames, f => Assert.Equal(f.W, f.H));
        foreach (var px in frames.Where(f => f.W >= 32))
        {
            var q = px.W / 4;
            Assert.True(Near(px, q, q, Red), $"{px.W}px top-left was {px.At(q, q)}");
            Assert.True(Near(px, px.W - q, px.W - q, Yellow), $"{px.W}px bottom-right was {px.At(px.W - q, px.W - q)}");
        }
    }

    [Fact]
    public async Task The_directory_lists_each_size_with_png_data_and_256_as_zero()
    {
        using var tmp = new TempDir();
        var (output, _) = await ImageConverter.ToIcoAsync(tmp.WriteFile("logo.png", Fixtures.PngRgb(300, 300)), default);
        var bytes = await File.ReadAllBytesAsync(output);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        for (var i = 0; i < 7; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i, 16);
            Assert.Equal(AllSizes[i] == 256 ? 0 : AllSizes[i], entry[0]);
            Assert.Equal(entry[0], entry[1]);
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.True(offset + length <= bytes.Length);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[offset..(offset + 4)]);
        }
    }

    [Fact]
    public async Task Transparency_is_kept_in_the_32_px_image()
    {
        using var tmp = new TempDir();
        // PngAlpha: the top-left quadrant is fully transparent, the rest opaque.
        var (output, _) = await ImageConverter.ToIcoAsync(tmp.WriteFile("logo.png", Fixtures.PngAlpha(300, 300)), default);

        var frame = (await FramesAsync(output)).Single(f => f.W == 32);
        Assert.Equal(0, frame.At(6, 6).A);
        Assert.Equal(255, frame.At(26, 26).A);
    }

    [Fact]
    public async Task A_wide_picture_is_centred_on_a_transparent_square_not_stretched()
    {
        using var tmp = new TempDir();
        var (output, _) = await ImageConverter.ToIcoAsync(tmp.WriteFile("banner.png", Fixtures.PngRgb(400, 200)), default);

        // At 64 px the picture is 64 x 32, so it covers rows 16 to 47.
        var px = (await FramesAsync(output)).Single(f => f.W == 64);
        Assert.Equal(0, px.At(32, 2).A);
        Assert.Equal(0, px.At(32, 61).A);
        Assert.Equal(255, px.At(2, 32).A);
        Assert.Equal(255, px.At(61, 32).A);
        Assert.Equal(255, px.At(32, 20).A);
        Assert.Equal(255, px.At(32, 44).A);
    }

    [Fact]
    public async Task A_small_source_still_converts_and_is_reported_as_soft()
    {
        using var tmp = new TempDir();
        var (output, soft) = await ImageConverter.ToIcoAsync(tmp.WriteFile("tiny.png", Fixtures.PngRgb(40, 40)), default);

        Assert.True(soft);
        Assert.Equal(7u, await FrameCountAsync(output));
    }

    [Fact]
    public async Task The_action_warns_once_when_a_source_is_under_256_px_and_the_toast_says_so()
    {
        using var tmp = new TempDir();
        var small = tmp.WriteFile("small.png", Fixtures.PngRgb(64, 48));
        var big = tmp.WriteFile("big.png", Fixtures.PngRgb(300, 300));
        var ctx = new ActionContext(Capabilities.All);

        var result = await Action().Run([small, big], ctx, default);

        Assert.Equal(2, result.Outputs.Count);
        Assert.Equal([FeedbackCopy.IconSourceSmall], result.Notes);
        var toast = FeedbackPlan.ToastFor(result);
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Contains("256 px", toast.Detail);

        var clean = await Action().Run([big], ctx, default);
        Assert.Empty(clean.Notes);
        Assert.Equal(ToastKind.Success, FeedbackPlan.ToastFor(clean).Kind);
    }

    [Fact]
    public void The_tile_is_offered_for_pictures_but_not_for_an_ico()
    {
        Assert.Contains(Action(), ActionCatalog.For([@"C:\x\a.png"], Capabilities.None, _ => Category.Image));
        Assert.DoesNotContain(Action(), ActionCatalog.For([@"C:\x\a.ico"], Capabilities.None, _ => Category.Image));
    }

    [Fact]
    public async Task An_ico_input_is_skipped_as_already_in_format()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("a.png", Fixtures.PngRgb(300, 300));
        var (ico, _) = await ImageConverter.ToIcoAsync(png, default);

        var result = await Action().Run([ico], new ActionContext(Capabilities.All), default);

        Assert.Empty(result.Outputs);
        Assert.Equal(1, result.AlreadyInFormat);
    }

    [Fact]
    public async Task A_cancelled_conversion_writes_nothing()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("logo.png", Fixtures.PngRgb(300, 300));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageConverter.ToIcoAsync(input, cts.Token));

        Assert.Equal(["logo.png"], FileNames(tmp.Path));
    }
}
