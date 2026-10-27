using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Cli;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using MikeShift.Core.Shell;
using static MikeShift.Core.Tests.Images.ImageTestUtil;

namespace MikeShift.Core.Tests.Images;

/// <summary>
/// Blur faces. The detector is replaced by a stand-in that returns fixed boxes, so none of these depend on how accurate Windows' detector is;
/// the one test that runs the real detector only needs it to find nothing in a flat picture.
/// </summary>
public class BlurFacesTests
{
    private static readonly Capabilities Caps = Capabilities.All with { HeifEncode = false };

    private static FaceFinder Finds(params RectPx[] boxes) => (_, _) => Task.FromResult<IReadOnlyList<RectPx>>(boxes);

    private static string[] FileNames(TempDir tmp) => Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).Order().ToArray();

    private static Raster Checkerboard(int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (byte)(((x + y) & 1) == 0 ? 0 : 255);
                var i = (y * w + x) * 4;
                bgra[i] = bgra[i + 1] = bgra[i + 2] = v;
                bgra[i + 3] = 255;
            }
        return new Raster(w, h, bgra);
    }

    // ------------------------------------------------------------------ the blur

    [Fact]
    public async Task Only_the_face_boxes_change_and_the_rest_of_the_picture_is_pixel_identical()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("party.png", Fixtures.PhotoPng(160, 120));
        var before = Decode(input);
        var face = new RectPx(40, 30, 40, 40);
        using var guard = OriginalsGuard.Watch(input);

        var result = await ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default, Finds(face), default);

        Assert.Equal(tmp.Combine("party blurred.png"), result.Path);
        Assert.Equal(1, result.Faces);
        var after = Decode(result.Path);
        var covered = FaceBlur.Cover([face], before.Width, before.Height).Single();
        var changedInside = 0;
        for (var y = 0; y < before.Height; y++)
            for (var x = 0; x < before.Width; x++)
            {
                var i = (y * before.Width + x) * 4;
                var same = before.Bgra.AsSpan(i, 4).SequenceEqual(after.Bgra.AsSpan(i, 4));
                var inside = x >= covered.X && x < covered.X + covered.W && y >= covered.Y && y < covered.Y + covered.H;
                if (!inside) Assert.True(same, $"pixel {x},{y} outside the face changed");
                else if (!same) changedInside++;
            }
        Assert.True(changedInside > covered.W * covered.H / 2, $"only {changedInside} pixels inside the box changed");
        guard.AssertUnchanged();
    }

    [Fact]
    public void A_box_is_grown_by_thirty_percent_and_kept_inside_the_picture()
    {
        Assert.Equal(new RectPx(35, 35, 130, 130), FaceBlur.Cover([new RectPx(50, 50, 100, 100)], 400, 300).Single());
        Assert.Equal(new RectPx(0, 0, 58, 58), FaceBlur.Cover([new RectPx(0, 0, 50, 50)], 400, 300).Single());
        Assert.Equal(new RectPx(342, 242, 58, 58), FaceBlur.Cover([new RectPx(350, 250, 50, 50)], 400, 300).Single());
        Assert.Empty(FaceBlur.Cover([new RectPx(500, 500, 20, 20), new RectPx(10, 10, 0, 0)], 400, 300));
    }

    [Fact]
    public void The_finest_detail_is_gone_and_the_box_is_made_of_a_few_coarse_blocks()
    {
        // A checkerboard is the most detail a picture can hold; a blur that could be undone would leave some of it.
        var source = Checkerboard(120, 120);
        var region = new RectPx(10, 10, 96, 96);

        var blurred = FaceBlur.Apply(source, [region]);

        var maxStep = 0;
        var distinct = new HashSet<int>();
        for (var y = region.Y; y < region.Y + region.H; y++)
            for (var x = region.X; x < region.X + region.W - 1; x++)
            {
                var i = (y * 120 + x) * 4;
                maxStep = Math.Max(maxStep, Math.Abs(blurred.Bgra[i] - blurred.Bgra[i + 4]));
                distinct.Add(blurred.Bgra[i]);
            }
        Assert.True(maxStep <= 8, $"neighbouring pixels still differ by {maxStep}");
        Assert.True(distinct.Count <= 40, $"{distinct.Count} different values");
        Assert.Equal(source.Bgra.AsSpan(0, 10 * 4).ToArray(), blurred.Bgra.AsSpan(0, 10 * 4).ToArray());
    }

    [Fact]
    public void Blurring_a_box_reads_and_writes_nothing_outside_it()
    {
        var source = Checkerboard(60, 60);
        var region = new RectPx(20, 20, 20, 20);

        var blurred = FaceBlur.Apply(source, [region]);

        for (var y = 0; y < 60; y++)
            for (var x = 0; x < 60; x++)
            {
                if (x >= 20 && x < 40 && y >= 20 && y < 40) continue;
                var i = (y * 60 + x) * 4;
                Assert.Equal(source.Bgra[i], blurred.Bgra[i]);
            }
    }

    [Fact]
    public void Transparent_pixels_stay_transparent_and_do_not_tint_their_neighbours()
    {
        var bgra = new byte[40 * 40 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var left = (i / 4) % 40 < 20;
            bgra[i + 2] = 255; // red where opaque
            bgra[i + 3] = (byte)(left ? 255 : 0);
        }
        var blurred = FaceBlur.Apply(new Raster(40, 40, bgra), [new RectPx(0, 0, 40, 40)]);

        for (var i = 0; i < bgra.Length; i += 4)
            if (blurred.Bgra[i + 3] > 0)
                Assert.True(blurred.Bgra[i + 2] >= 250 && blurred.Bgra[i] <= 5, "colour of an opaque red picture stays red");
    }

    [Fact]
    public async Task Several_faces_are_each_covered_and_counted()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("group.png", Fixtures.PhotoPng(200, 100));

        var result = await ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default,
            Finds(new RectPx(10, 10, 30, 30), new RectPx(80, 20, 30, 30), new RectPx(150, 30, 30, 30)), default);

        Assert.Equal(3, result.Faces);
    }

    // ------------------------------------------------------------------ the output file

    [Fact]
    public async Task A_jpeg_keeps_its_format_and_loses_exif_gps_xmp_and_the_embedded_thumbnail()
    {
        using var tmp = new TempDir();
        var plain = Fixtures.JpegWithMetadata(120, 80, orientation: 1, gps: true, xmp: true, photoshop: true);
        var source = WithThumbnail(plain);
        var input = tmp.WriteFile("trip.jpg", source);
        using var guard = OriginalsGuard.Watch(input);
        Assert.Equal(2, CountSoi(source)); // the fixture really holds a second picture inside the Exif block

        var result = await ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default, Finds(new RectPx(30, 20, 40, 30)), default);

        Assert.Equal(tmp.Combine("trip blurred.jpg"), result.Path);
        var bytes = File.ReadAllBytes(result.Path);
        Assert.Equal(ImageContainer.Jpeg, ContainerOf(result.Path));
        Assert.Equal(1, CountSoi(bytes));
        Assert.False(JpegMeta.HasMetadataSegments(bytes), JpegMeta.Describe(bytes));
        Assert.Null(JpegMeta.ExtractExif(bytes));
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain(Fixtures.ExifMake, text);
        Assert.DoesNotContain("xmpmeta", text);
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Other_formats_keep_their_format()
    {
        using var tmp = new TempDir();
        var bmp = tmp.WriteFile("face.bmp", Fixtures.Bmp(64, 48));

        var result = await ImageConverter.BlurFacesAsync(bmp, Caps, ConversionOptions.Default, Finds(new RectPx(10, 10, 20, 20)), default);

        Assert.Equal(tmp.Combine("face blurred.bmp"), result.Path);
        Assert.Equal(ImageContainer.Bmp, ContainerOf(result.Path));
    }

    [Fact]
    public async Task A_picture_with_no_faces_fails_with_a_message_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("scenery.png", Fixtures.PngRgb(64, 48));
        using var guard = OriginalsGuard.Watch(input);

        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default, Finds(), default));

        Assert.Equal(FaceBlurCopy.NoFaces, ex.Message);
        Assert.Equal(["scenery.png"], FileNames(tmp));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task Animated_gifs_and_multi_page_tiffs_are_refused_before_the_detector_runs()
    {
        using var tmp = new TempDir();
        var ran = false;
        FaceFinder spy = (_, _) => { ran = true; return Task.FromResult<IReadOnlyList<RectPx>>([new RectPx(0, 0, 10, 10)]); };
        var gif = tmp.WriteFile("loop.gif", Fixtures.Gif(64, 48, animated: true));
        var tiff = tmp.WriteFile("scan.tiff", Fixtures.TiffMultiFrame(64, 48, 3));

        var animated = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.BlurFacesAsync(gif, Caps, ConversionOptions.Default, spy, default));
        var pages = await Assert.ThrowsAsync<ConversionException>(() => ImageConverter.BlurFacesAsync(tiff, Caps, ConversionOptions.Default, spy, default));

        Assert.Contains("blurred", animated.Message);
        Assert.Contains("3 pages", pages.Message);
        Assert.False(ran);
        Assert.Equal(["loop.gif", "scan.tiff"], FileNames(tmp));
    }

    [Fact]
    public async Task Cancelling_stops_before_anything_is_written()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("party.png", Fixtures.PngRgb(64, 48));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default, Finds(new RectPx(5, 5, 20, 20)), cts.Token));

        Assert.Equal(["party.png"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ memory, cancellation, mapping

    [Fact]
    public void A_box_covering_a_large_picture_needs_little_more_than_the_copy_of_the_picture()
    {
        var source = new Raster(4000, 3000, new byte[4000 * 3000 * 4]);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var blurred = FaceBlur.Apply(source, [new RectPx(0, 0, 4000, 3000)]);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(source.Bgra.Length, blurred.Bgra.Length);
        Assert.True(allocated < source.Bgra.Length * 1.1, $"{allocated:N0} bytes allocated for a {source.Bgra.Length:N0}-byte picture");
    }

    [Fact]
    public void Blurring_observes_the_cancellation_token()
    {
        var source = Checkerboard(200, 200);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FaceBlur.Apply(source, [new RectPx(10, 10, 100, 100)], cts.Token));
    }

    [Fact]
    public async Task Cancelling_after_the_faces_are_found_writes_nothing()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("party.png", Fixtures.PngRgb(64, 48));
        using var cts = new CancellationTokenSource();
        FaceFinder cancelsAfterFinding = (_, _) =>
        {
            cts.Cancel();
            return Task.FromResult<IReadOnlyList<RectPx>>([new RectPx(5, 5, 20, 20)]);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageConverter.BlurFacesAsync(input, Caps, ConversionOptions.Default, cancelsAfterFinding, cts.Token));

        Assert.Equal(["party.png"], FileNames(tmp));
    }

    [Theory]
    [InlineData(4000, 3000, 1600, 1200)]
    [InlineData(3000, 4000, 1200, 1600)]
    [InlineData(1600, 900, 1600, 900)]
    [InlineData(800, 600, 800, 600)]
    [InlineData(20000, 10, 1600, 1)]
    public void The_detector_reads_a_copy_of_at_most_1600_pixels_on_the_long_side(int w, int h, int expectedW, int expectedH) =>
        Assert.Equal((expectedW, expectedH), FaceDetection.ReducedSize(w, h, FaceDetection.DetectLongSide));

    [Fact]
    public void A_box_found_in_the_reduced_copy_is_mapped_back_to_the_full_picture_and_then_grown_and_clamped()
    {
        // 4000 x 3000 is read as 1600 x 1200, so every coordinate is multiplied by 2.5.
        var (rw, rh) = FaceDetection.ReducedSize(4000, 3000, FaceDetection.DetectLongSide);
        double scaleX = 4000.0 / rw, scaleY = 3000.0 / rh;

        var box = FaceDetection.MapBox(100.5, 40, 200, 220.2, scaleX, scaleY);

        Assert.Equal(new RectPx(251, 100, 500, 551), box); // rounded outwards: floor of the corner, ceiling of the size
        Assert.Equal(new RectPx(176, 17, 650, 717), FaceBlur.Cover([box], 4000, 3000).Single());
        // a face at the edge of the reduced copy ends at the edge of the picture after growing
        var edge = FaceDetection.MapBox(1500, 1100, 100, 100, scaleX, scaleY);
        var covered = FaceBlur.Cover([edge], 4000, 3000).Single();
        Assert.Equal(4000, covered.X + covered.W);
        Assert.Equal(3000, covered.Y + covered.H);
    }

    [SkipIfNoCapability(nameof(Capabilities.FaceDetect))]
    public async Task The_real_detector_reads_a_picture_over_1600_pixels_without_failing()
    {
        // No face photo can be checked in (no binary fixtures), so this only shows the reduce-and-map path runs on the real API.
        var pixels = new Raster(4000, 3000, Enumerable.Repeat((byte)200, 4000 * 3000 * 4).ToArray());

        var boxes = await FaceDetection.FindAsync(pixels, default);

        Assert.All(boxes, b => Assert.True(b.X >= 0 && b.Y >= 0 && b.X + b.W <= 4001 && b.Y + b.H <= 3001));
    }

    // ------------------------------------------------------------------ the tile

    [Fact]
    public void The_tile_is_a_picture_tool_that_needs_face_detection()
    {
        var png = new[] { @"C:\x\a.png" };
        var tile = ActionCatalog.Find(ImageActions.All, "Blur faces")!;

        Assert.Equal(WheelPage.Tool, tile.Page);
        Assert.False(string.IsNullOrEmpty(tile.Glyph));
        Assert.True(tile.IsVisibleFor(png, Capabilities.All));
        Assert.False(tile.IsVisibleFor(png, Capabilities.All with { FaceDetect = false }));
        Assert.DoesNotContain(ActionCatalog.For(png, Capabilities.All with { FaceDetect = false }, _ => Category.Image), a => a.Title == "Blur faces");
    }

    [Fact]
    public async Task The_tile_writes_through_naming_counts_the_faces_and_the_toast_says_so()
    {
        using var tmp = new TempDir();
        var one = tmp.WriteFile("one.png", Fixtures.PhotoPng(100, 80));
        var two = tmp.WriteFile("two.png", Fixtures.PhotoPng(100, 80));
        var none = tmp.WriteFile("none.png", Fixtures.PhotoPng(100, 80));
        using var guard = OriginalsGuard.Watch(one, two, none);
        var tile = BlurFacesAction.Create(Finds(new RectPx(10, 10, 20, 20), new RectPx(50, 20, 20, 20)));

        var single = await tile.Run([one], new ActionContext(Caps), default);

        Assert.Equal(tmp.Combine("one blurred.png"), Assert.Single(single.Outputs).Path);
        Assert.Equal("Blurred 2 faces", single.Summary);
        Assert.Equal("Saved one blurred.png · Blurred 2 faces · Next to the original", FeedbackPlan.ToastFor(single).Text);

        var batch = await tile.Run([two, none], new ActionContext(Caps), default);
        Assert.Equal(2, batch.Outputs.Count);
        Assert.Equal("Blurred 4 faces", batch.Summary);
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task In_a_batch_a_picture_without_faces_fails_and_the_others_are_kept()
    {
        using var tmp = new TempDir();
        var withFace = tmp.WriteFile("a.png", Fixtures.PngRgb(64, 48));
        var without = tmp.WriteFile("b.png", Fixtures.PngRgb(32, 24)); // the stand-in finds a face only in the larger one
        FaceFinder bySize = (pixels, _) =>
            Task.FromResult<IReadOnlyList<RectPx>>(pixels.Width == 64 ? [new RectPx(10, 10, 20, 20)] : []);

        var result = await BlurFacesAction.Create(bySize).Run([withFace, without], new ActionContext(Caps), default);

        Assert.True(result.PartialFailure);
        Assert.Equal(tmp.Combine("a blurred.png"), Assert.Single(result.Outputs).Path);
        Assert.Equal(FaceBlurCopy.NoFaces, Assert.Single(result.Failures).Message);
        Assert.Equal(["a blurred.png", "a.png", "b.png"], FileNames(tmp));
    }

    [Fact]
    public void A_partly_successful_batch_still_shows_how_many_faces_were_blurred()
    {
        var result = new ActionResult
        {
            Total = 2,
            Outputs = [new ActionOutput(@"C:.png", @"C: blurred.png", false)],
            Failures = [new ActionFailure(@"C:.png", FaceBlurCopy.NoFaces)],
            Summary = FaceBlurCopy.Blurred(2),
        };

        var toast = FeedbackPlan.ToastFor(result);

        Assert.Equal("Saved 1 of 2", toast.Title);
        Assert.Equal("Blurred 2 faces · 1 couldn't be converted", toast.Detail);
        Assert.Equal(ToastKind.Warning, toast.Kind);
    }

    [Fact]
    public void The_words_are_plain_sentence_case()
    {
        Assert.Equal("Blurred 1 face", FaceBlurCopy.Blurred(1));
        Assert.Equal("Blurred 3 faces", FaceBlurCopy.Blurred(3));
        Assert.StartsWith("No faces found", FaceBlurCopy.NoFaces);
        Assert.Equal("Blurring faces…", FeedbackPlan.StartToast(ActionCatalog.Find(ImageActions.All, "Blur faces")!, 0).Title);
    }

    [Fact]
    public async Task The_command_line_does_not_offer_the_tool_on_a_PC_without_face_detection()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("party.png", Fixtures.PngRgb(64, 48));
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliRunner.RunAsync(["--run", "Blur faces", input], stdout, stderr, Capabilities.All with { FaceDetect = false });

        Assert.Equal(1, exit);
        Assert.Contains("error:", stderr.ToString());
        Assert.Equal(["party.png"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.FaceDetect))]
    public async Task The_command_line_runs_it_by_name_and_ends_a_picture_without_faces_as_a_clean_error()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("party.png", Fixtures.PngRgb(64, 48));
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliRunner.RunAsync(["--run", "Blur faces", input], stdout, stderr, Capabilities.Current);

        Assert.Equal(1, exit);
        Assert.Contains("error: " + FaceBlurCopy.NoFaces, stderr.ToString());
        Assert.Equal(["party.png"], FileNames(tmp));
    }

    [SkipIfNoCapability(nameof(Capabilities.FaceDetect))]
    public async Task The_real_detector_finds_no_face_in_a_flat_picture_and_no_file_is_written()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("flat.png", Fixtures.PngRgb(320, 240));
        var tile = ActionCatalog.Find(ImageActions.All, "Blur faces")!;

        var result = await tile.Run([input], new ActionContext(Capabilities.Current), default);

        Assert.True(result.AllFailed);
        Assert.Equal(FaceBlurCopy.NoFaces, result.Failures[0].Message);
        Assert.Equal(["flat.png"], FileNames(tmp));
    }

    // ------------------------------------------------------------------ the capability

    [Fact]
    public void The_dev_switch_and_the_shell_cache_know_about_face_detection()
    {
        Assert.Equal(Capabilities.All with { FaceDetect = false }, CapabilityOverrides.Parse("no-faces")!(Capabilities.All));
        Assert.Equal(Capabilities.All, CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.All)));
        Assert.False(CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.All with { FaceDetect = false })).FaceDetect);
        Assert.False(CapabilityCache.Parse("Ocr=True").FaceDetect);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A copy of <paramref name="jpeg"/> with a second, tiny JPEG inside a leading Exif block: what a camera's embedded thumbnail looks like to a byte scan.</summary>
    private static byte[] WithThumbnail(byte[] jpeg)
    {
        var thumbnail = Fixtures.QuadrantJpeg(8, 8);
        var payload = System.Text.Encoding.ASCII.GetBytes("Exif\0\0").Concat(thumbnail).ToArray();
        var length = payload.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static int CountSoi(byte[] bytes)
    {
        var count = 0;
        for (var i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8) count++;
        return count;
    }
}
