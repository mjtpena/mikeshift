using System.Diagnostics;
using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Media;
using SkiaSharp;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using static MikeShift.Core.Tests.Media.MediaTestSupport;

namespace MikeShift.Core.Tests.Media;

/// <summary>
/// The video Fit tools ("Fit 10 MB", "Fit 25 MB"): the output is never over the limit, a source already under it is re-wrapped instead of
/// re-encoded, and a video too long for the size is refused before anything is written.
/// </summary>
public class VideoFitTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    /// <summary>
    /// A 60 s, 1280x720 H.264/AAC MP4 that is far over 25 MB: every frame is a key frame of fine random blocks, so the encoder cannot make the
    /// source small, and the Fit encodes have real work to do. Made once per test run.
    /// </summary>
    private static readonly Lazy<string> s_busy = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "mikeshift-tests", "fit-fixture-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(dir, true); } catch (Exception) { } };
        var path = Path.Combine(dir, "busy.mp4");
        Task.Run(() => WriteBusyMp4Async(path, seconds: 60)).GetAwaiter().GetResult();
        return path;
    });

    private static string CopyBusy(TempDir tmp, string name = "busy.mp4")
    {
        var dest = tmp.Combine(name);
        File.Copy(s_busy.Value, dest);
        return dest;
    }

    /// <summary>Every file and folder under <paramref name="dir"/>, hidden temporary folders included (TempDir's own fake Downloads folder is not counted).</summary>
    private static string[] Everything(string dir) =>
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p)).Where(p => p != "__downloads").Order(StringComparer.Ordinal).ToArray();

    // ------------------------------------------------------------------ the size limit (slow: real 60 s transcodes)

    [SlowTheory(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode)), Trait("Category", "Slow")]
    [InlineData(10)]
    [InlineData(25)]
    public async Task A_sixty_second_video_ends_at_or_under_the_size(int megabytes)
    {
        using var tmp = new TempDir();
        var busy = CopyBusy(tmp);
        using var guard = OriginalsGuard.Watch(busy);
        var source = Info(busy);
        Assert.True(new FileInfo(busy).Length > 25_000_000, $"the fixture must be over 25 MB, it is {new FileInfo(busy).Length:N0} bytes");

        var output = await VideoConverter.FitAsync(busy, megabytes, NoCancel);

        Assert.Equal(tmp.Combine($"busy {megabytes} MB.mp4"), output);
        Assert.InRange(new FileInfo(output).Length, 1, megabytes * 1_000_000L);
        var info = Info(output);
        Assert.True(info.IsH264);
        Assert.True(info.IsAac);
        Assert.InRange(info.Duration.TotalSeconds, source.Duration.TotalSeconds - 0.5, source.Duration.TotalSeconds + 0.5);
        Assert.InRange(info.Height, 360, source.Height); // never upscaled, never below 360p
        Assert.Equal([Path.GetFileName(output), Path.GetFileName(busy)], Everything(tmp.Path));
    }

    [SlowFact(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode)), Trait("Category", "Slow")]
    public async Task An_encode_that_comes_out_over_the_limit_is_redone_lower_and_never_saved_over_it()
    {
        using var tmp = new TempDir();
        var busy = CopyBusy(tmp);
        using var guard = OriginalsGuard.Watch(busy);

        // The first encode aims at 130 % of 10 MB, so only the measured second encode can bring the file under the limit.
        var output = await VideoConverter.FitAsync(busy, 10, streamShare: 1.3, NoCancel);

        Assert.Equal(tmp.Combine("busy 10 MB.mp4"), output);
        Assert.InRange(new FileInfo(output).Length, 5_000_000, 10_000_000);
        Assert.Equal([Path.GetFileName(output), Path.GetFileName(busy)], Everything(tmp.Path));
    }

    [SlowFact(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode)), Trait("Category", "Slow")]
    public async Task A_video_too_long_for_the_size_is_refused_before_any_encoding_and_leaves_nothing_behind()
    {
        using var tmp = new TempDir();
        var busy = CopyBusy(tmp);
        using var guard = OriginalsGuard.Watch(busy);

        // 1 MB holds about 25 s of watchable video: 60 s is too long.
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => VideoConverter.FitAsync(busy, 1, NoCancel));
        sw.Stop();

        Assert.Equal(SizeTargetCopy.TooLong(1, SizeTarget.LongestWatchable(1_000_000, hasAudio: true)), ex.Message);
        Assert.Contains("20 seconds", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"the refusal took {sw.Elapsed}: it must come before encoding");
        Assert.Equal(["busy.mp4"], Everything(tmp.Path));
    }

    [SlowFact(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode)), Trait("Category", "Slow")]
    public async Task Cancelling_a_fit_part_way_leaves_nothing_behind()
    {
        using var tmp = new TempDir();
        var busy = CopyBusy(tmp);
        using var guard = OriginalsGuard.Watch(busy);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoConverter.FitAsync(busy, 10, cts.Token));

        Assert.Equal(["busy.mp4"], Everything(tmp.Path));
    }

    // ------------------------------------------------------------------ a source already under the limit

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task A_video_already_under_the_size_is_rewrapped_without_reencoding()
    {
        using var tmp = new TempDir();
        // An ordinary H.264/AAC file at 44.1 kHz (a re-encode would make it 48 kHz and change the size a lot).
        var mp4 = tmp.Combine("small.mp4");
        await Integrity.VideoCheck.WriteCleanMp4Async(CopyMp4(tmp, "fixture.mp4"), mp4);
        File.Delete(tmp.Combine("fixture.mp4"));
        using var guard = OriginalsGuard.Watch(mp4);
        var source = Info(mp4);

        var output = await VideoConverter.FitAsync(mp4, 10, NoCancel);

        Assert.Equal(tmp.Combine("small 10 MB.mp4"), output);
        var copy = Info(output);
        Assert.True(copy.IsH264);
        Assert.True(copy.IsAac);
        Assert.Equal(source.AudioSampleRate, copy.AudioSampleRate);
        Assert.Equal(source.Width, copy.Width);
        Assert.Equal(source.Height, copy.Height);
        var ratio = (double)new FileInfo(output).Length / new FileInfo(mp4).Length;
        Assert.InRange(ratio, 0.9, 1.1);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task A_small_video_that_cannot_be_rewrapped_is_encoded_and_still_fits()
    {
        using var tmp = new TempDir();
        var wmv = tmp.Combine("small.wmv");
        await Integrity.VideoCheck.WriteWmvAsync(CopyMp4(tmp, "fixture.mp4"), wmv);
        File.Delete(tmp.Combine("fixture.mp4"));
        using var guard = OriginalsGuard.Watch(wmv);

        var output = await VideoConverter.FitAsync(wmv, 10, NoCancel);

        Assert.Equal(tmp.Combine("small 10 MB.mp4"), output);
        Assert.InRange(new FileInfo(output).Length, 1, 10_000_000);
        Assert.True(Info(output).IsH264);
    }

    // ------------------------------------------------------------------ through the catalog

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task A_batch_keeps_the_outputs_written_before_a_damaged_file_fails()
    {
        using var tmp = new TempDir();
        var good = CopyMp4(tmp, "a.mp4");
        var bad = tmp.WriteFile("b.mp4", Fixtures.Noise(4096, 3));
        using var guard = OriginalsGuard.Watch(good, bad);
        var fit = ActionCatalog.Find(VideoActions.All, "Fit 10 MB")!;

        var result = await fit.Run([good, bad], new ActionContext(Capabilities.Current, null), NoCancel);

        var written = Assert.Single(result.Outputs);
        Assert.Equal(tmp.Combine("a 10 MB.mp4"), written.Path);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(bad, failure.Input);
        Assert.DoesNotContain("Exception", failure.Message, StringComparison.Ordinal);
    }

    [SkipIfNoCapability(nameof(Capabilities.H264Encode), nameof(Capabilities.AacEncode))]
    public async Task A_cancelled_batch_writes_nothing()
    {
        using var tmp = new TempDir();
        var mp4 = CopyMp4(tmp);
        var fit = ActionCatalog.Find(VideoActions.All, "Fit 25 MB")!;

        var result = await fit.Run([mp4], new ActionContext(Capabilities.Current, null), new CancellationToken(canceled: true));

        Assert.True(result.Cancelled);
        Assert.Empty(result.Outputs);
        Assert.Equal(["clip.mp4"], Everything(tmp.Path));
    }

    // ------------------------------------------------------------------ the fixture

    private static async Task WriteBusyMp4Async(string path, double seconds)
    {
        const int width = 1280, height = 720, pictures = 6;
        var work = Path.Combine(Path.GetTempPath(), "mikeshift-busy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var composition = new MediaComposition();
            for (var i = 0; i < pictures; i++)
            {
                var png = Path.Combine(work, $"busy{i}.png");
                await File.WriteAllBytesAsync(png, BusyPng(width, height, seed: i));
                var file = await StorageFile.GetFileFromPathAsync(png);
                composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(seconds / pictures)));
            }
            var wav = Path.Combine(work, "audio.wav");
            await File.WriteAllBytesAsync(wav, Fixtures.SineWav(seconds + 0.5));
            composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav)));

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Video.Width = width;
            profile.Video.Height = height;
            profile.Video.Bitrate = 8_000_000;
            profile.Video.FrameRate.Numerator = 30;
            profile.Video.FrameRate.Denominator = 1;
            // Every frame a key frame (MF_MT_MAX_KEYFRAME_SPACING = 1): still pictures cannot then shrink to almost nothing.
            profile.Video.Properties[new Guid("c16eb52b-73a1-476f-8d62-839d6a020652")] = 1u;

            var rendered = Path.Combine(work, "out.mp4");
            await File.WriteAllBytesAsync(rendered, []);
            var result = await composition.RenderToFileAsync(await StorageFile.GetFileFromPathAsync(rendered), MediaTrimmingPreference.Fast, profile);
            Assert.Equal(Windows.Media.Transcoding.TranscodeFailureReason.None, result);
            File.Copy(rendered, path, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { /* Windows may still hold the render for a moment */ }
        }
    }

    /// <summary>A picture of 4x4-pixel random colour blocks: detail the encoder has to spend bits on.</summary>
    private static byte[] BusyPng(int width, int height, int seed)
    {
        var rng = new Random(seed);
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint())
        {
            for (var y = 0; y < height; y += 4)
                for (var x = 0; x < width; x += 4)
                {
                    paint.Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
                    canvas.DrawRect(x, y, 4, 4, paint);
                }
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
