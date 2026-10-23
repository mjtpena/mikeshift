using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using MikeShift.Core.Media;
using SkiaSharp;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// The video fixture and its independent readers: Windows' video editing API (<c>MediaClip</c> / <c>MediaComposition</c>, a different
/// Media Foundation client from the converters) for frame grabs, size and duration; the suite's own MP4 box parser, GIF block parser
/// and ASF signature check for structure.
/// </summary>
internal static class VideoCheck
{
    public const int Width = 640;
    public const int Height = 360;
    public const double Seconds = 2.0;

    /// <summary>The picture shown at time <paramref name="t"/> in the fixture (segment of 0.5 s each; colour and box position change per segment).</summary>
    public static byte[] SegmentPng(int segment)
    {
        var bgra = new byte[Width * Height * 4];
        var boxX = 40 + segment * 120;
        var hue = new[] { (200, 40, 40), (40, 180, 60), (50, 70, 210), (220, 200, 40) }[segment];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 4;
                var inBox = x >= boxX && x < boxX + 100 && y >= 120 && y < 220;
                // Soft vertical gradient in the segment's colour, with a white box whose position tells the segment apart.
                var k = 0.55 + 0.45 * y / Height;
                bgra[i + 2] = (byte)(inBox ? 255 : hue.Item1 * k);
                bgra[i + 1] = (byte)(inBox ? 255 : hue.Item2 * k);
                bgra[i] = (byte)(inBox ? 255 : hue.Item3 * k);
                bgra[i + 3] = 255;
            }
        using var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var pm = bmp.PeekPixels();
        using var data = pm.Encode(new SKPngEncoderOptions())!;
        return data.ToArray();
    }

    /// <summary>A <paramref name="seconds"/> long H.264/AAC MP4 (640x360): four 0.5 s still pictures and the integrity chirp as audio.</summary>
    public static async Task WriteMp4Async(string path, bool withAudio = true, double seconds = Seconds)
    {
        var work = Path.Combine(Path.GetTempPath(), "mikeshift-vfix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var composition = new MediaComposition();
            for (var i = 0; i < 4; i++)
            {
                var png = Path.Combine(work, $"seg{i}.png");
                await File.WriteAllBytesAsync(png, SegmentPng(i));
                var file = await StorageFile.GetFileFromPathAsync(png);
                composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(seconds / 4)));
            }
            if (withAudio)
            {
                var wav = Path.Combine(work, "audio.wav");
                await File.WriteAllBytesAsync(wav, AudioCheck.WavBytes(AudioSource.Chirp(16, seconds + 0.5)));
                var audioFile = await StorageFile.GetFileFromPathAsync(wav);
                composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(audioFile));
            }
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
            profile.Video.Width = Width;
            profile.Video.Height = Height;
            profile.Video.Bitrate = 4_000_000;
            profile.Video.FrameRate.Numerator = 30;
            profile.Video.FrameRate.Denominator = 1;
            // One key frame every 15 frames (0.5 s), so a frame grab at any time decodes from a nearby key frame (MF_MT_MAX_KEYFRAME_SPACING).
            profile.Video.Properties[new Guid("c16eb52b-73a1-476f-8d62-839d6a020652")] = 15u;
            if (!withAudio) profile.Audio = null;
            var rendered = Path.Combine(work, "out.mp4");
            await File.WriteAllBytesAsync(rendered, []);
            var dest = await StorageFile.GetFileFromPathAsync(rendered);
            var result = await composition.RenderToFileAsync(dest, MediaTrimmingPreference.Fast, profile);
            Assert.Equal(TranscodeFailureReason.None, result);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.Copy(rendered, path, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { /* Windows may still hold the render for a moment */ }
        }
    }

    /// <summary>Makes a WMV from an MP4 with Windows' transcoder (fixture only).</summary>
    public static async Task WriteWmvAsync(string mp4, string wmv)
    {
        await File.WriteAllBytesAsync(wmv, []);
        var src = await StorageFile.GetFileFromPathAsync(mp4);
        var dst = await StorageFile.GetFileFromPathAsync(wmv);
        var profile = MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Vga);
        profile.Video.Width = Width; // the Vga preset is 4:3 (640x480); keep the source picture shape
        profile.Video.Height = Height;
        var prep = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dst, profile);
        Assert.True(prep.CanTranscode, "cannot make the WMV fixture: " + prep.FailureReason);
        await prep.TranscodeAsync();
    }

    /// <summary>Re-encodes an MP4 with Windows' transcoder into an ordinary H.264/AAC MP4 (monotonic time stamps), for the lossless-rewrap test.</summary>
    public static async Task WriteCleanMp4Async(string mp4, string dst)
    {
        await File.WriteAllBytesAsync(dst, []);
        var src = await StorageFile.GetFileFromPathAsync(mp4);
        var dest = await StorageFile.GetFileFromPathAsync(dst);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = Width;
        profile.Video.Height = Height;
        profile.Audio.SampleRate = 44100; // not the preset's 48 kHz, so a re-encode by the code under test would show
        profile.Audio.ChannelCount = 2;
        var prep = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dest, profile);
        Assert.True(prep.CanTranscode, "cannot make the clean MP4 fixture: " + prep.FailureReason);
        await prep.TranscodeAsync();
    }

    // ------------------------------------------------------------------ independent facts

    public sealed record Facts(TimeSpan Duration, int Width, int Height, double Fps);

    /// <summary>Duration, frame size and frame rate according to <c>MediaClip</c> (Windows' own video editing stack).</summary>
    public static async Task<Facts> FactsAsync(string path)
    {
        var f = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(f);
        var v = clip.GetVideoEncodingProperties();
        var fps = v.FrameRate.Denominator == 0 ? 0 : (double)v.FrameRate.Numerator / v.FrameRate.Denominator;
        return new Facts(clip.OriginalDuration, (int)v.Width, (int)v.Height, fps);
    }

    /// <summary>The picture at time <paramref name="t"/>, grabbed through <c>MediaComposition.GetThumbnailAsync</c> at the given size.</summary>
    public static async Task<Px> FrameAtAsync(string path, TimeSpan t, int w, int h)
    {
        var f = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(f);
        var comp = new MediaComposition();
        comp.Clips.Add(clip);
        using var stream = await comp.GetThumbnailAsync(t, w, h, VideoFramePrecision.NearestFrame);
        using var s = stream.AsStreamForRead();
        using var bmp = SKBitmap.Decode(s) ?? throw new Xunit.Sdk.XunitException($"no decodable frame at {t} in {Path.GetFileName(path)}");
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var img = SKImage.FromBitmap(bmp);
            Assert.True(img.ReadPixels(info, handle.AddrOfPinnedObject(), bmp.Width * 4, 0, 0));
        }
        finally { handle.Free(); }
        return new Px(bmp.Width, bmp.Height, bytes);
    }

    /// <summary>
    /// Every frame of the video in presentation order, decoded SEQUENTIALLY by a Media Foundation source reader (so inter-coded frames
    /// are right, which a thumbnail grab on a stream with a single key frame is not) and reduced to <paramref name="w"/> x
    /// <paramref name="h"/> by area averaging.
    /// </summary>
    public static IReadOnlyList<(TimeSpan Time, Px Frame)> Decode(string path, int w = 160, int h = 90)
    {
        MfInterop.EnsureStarted();
        var frames = new List<(TimeSpan, Px)>();
        IMFAttributes? attrs = null;
        IMFSourceReader? reader = null;
        try
        {
            attrs = MfInterop.CreateAttributes();
            MfInterop.SetUInt32(attrs, PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            reader = MfInterop.TryOpenReader(path, attrs) ?? throw new Xunit.Sdk.XunitException("Media Foundation cannot open " + Path.GetFileName(path));
            MfInterop.SelectOnly(reader, MfInterop.FirstVideoStream);
            var (sw, sh, stride) = GifExporter.ConfigureRgb32(reader);
            var rgb = new byte[w * h * 3];
            while (true)
            {
                if (!MfInterop.ReadSample(reader, MfInterop.FirstVideoStream, out var sample, out var ts)) { MfInterop.Release(sample); break; }
                if (sample is null) continue;
                try
                {
                    GifExporter.Resample(MfInterop.CopySampleBytes(sample), sw, sh, stride, rgb, w, h);
                }
                finally { MfInterop.Release(sample); }
                var bgra = new byte[w * h * 4];
                for (var i = 0; i < w * h; i++)
                {
                    bgra[i * 4] = rgb[i * 3 + 2]; bgra[i * 4 + 1] = rgb[i * 3 + 1]; bgra[i * 4 + 2] = rgb[i * 3]; bgra[i * 4 + 3] = 255;
                }
                frames.Add((TimeSpan.FromTicks(ts), new Px(w, h, bgra)));
            }
        }
        finally
        {
            MfInterop.Release(reader);
            MfInterop.Release(attrs);
        }
        Assert.NotEmpty(frames);
        return frames;
    }

    /// <summary>The frame being shown at <paramref name="seconds"/> (the last one whose time stamp is not later).</summary>
    public static Px At(IReadOnlyList<(TimeSpan Time, Px Frame)> frames, double seconds)
    {
        var t = frames[0].Time + TimeSpan.FromSeconds(seconds);
        var best = frames[0].Frame;
        foreach (var (time, frame) in frames)
        {
            if (time > t) break;
            best = frame;
        }
        return best;
    }

    /// <summary>The fixture's picture for a given source time, at 160x90 (what a frame grab at that size should resemble).</summary>
    public static Px ExpectedFrame(double seconds, int w = 160, int h = 90, double sourceSeconds = Seconds)
    {
        var seg = Math.Clamp((int)(seconds / (sourceSeconds / 4)), 0, 3);
        using var full = SKBitmap.Decode(SegmentPng(seg));
        using var scaled = full.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))!;
        var bytes = new byte[w * h * 4];
        System.Runtime.InteropServices.Marshal.Copy(scaled.GetPixels(), bytes, 0, bytes.Length);
        return new Px(w, h, bytes);
    }

    /// <summary>ASF (WMV/WMA) files start with the Header Object GUID 75B22630-668E-11CF-A6D9-00AA0062CE6C.</summary>
    public static bool IsAsf(string path)
    {
        var b = new byte[16];
        using var fs = File.OpenRead(path);
        return fs.Read(b) == 16 && b.AsSpan().SequenceEqual(new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C });
    }

    // ------------------------------------------------------------------ GIF

    public sealed record GifFrames(int Width, int Height, IReadOnlyList<int> DelaysCs, int LoopCount, bool HasLoopExtension, IReadOnlyList<Px> Frames);

    /// <summary>Block-level GIF facts (frame count, delays, loop extension) from the suite's parser plus every frame composited by SkiaSharp.</summary>
    public static GifFrames ReadGif(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var info = Media.MediaTestSupport.ParseGif(bytes);

        using var codec = SKCodec.Create(path)!;
        var n = codec.FrameCount;
        var frames = new List<Px>();
        var ii = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var infos = codec.FrameInfo;
        var done = new List<byte[]>();
        for (var i = 0; i < n; i++)
        {
            var buf = new byte[ii.Width * ii.Height * 4];
            var req = infos[i].RequiredFrame;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                if (req >= 0) Array.Copy(done[req], buf, buf.Length);
                var r = codec.GetPixels(ii, handle.AddrOfPinnedObject(), new SKCodecOptions(i) { PriorFrame = req });
                Assert.Equal(SKCodecResult.Success, r);
            }
            finally { handle.Free(); }
            done.Add(buf);
            frames.Add(new Px(ii.Width, ii.Height, buf));
        }
        return new GifFrames(info.Width, info.Height, info.Delays, info.LoopCount, info.HasNetscapeLoop, frames);
    }
}
