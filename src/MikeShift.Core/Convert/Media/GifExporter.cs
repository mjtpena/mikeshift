using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace MikeShift.Core.Media;

/// <summary>
/// Video -> animated GIF (PLAN §1.2): the first 15 s at 10 fps, longest side at most 480 px, looping forever.
/// Frames come from an <c>IMFSourceReader</c> converted to RGB32, are box-filtered down, and go through <see cref="GifWriter"/>.
/// </summary>
internal static class GifExporter
{
    public const int MaxSeconds = 15;
    public const int FramesPerSecond = 10;
    public const int MaxSide = 480;
    private const long TickHns = 10_000_000L / FramesPerSecond;
    private const int MaxFrames = MaxSeconds * FramesPerSecond;

    public static void Export(string input, string destination, CancellationToken ct)
    {
        MfInterop.EnsureStarted();

        IMFAttributes? attrs = null;
        IMFSourceReader? reader = null;
        try
        {
            attrs = MfInterop.CreateAttributes();
            MfInterop.SetUInt32(attrs, PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            reader = MfInterop.TryOpenReader(input, attrs) ?? throw new ConversionException("Windows can't open this video.");
            MfInterop.SelectOnly(reader, MfInterop.FirstVideoStream);

            var (srcW, srcH, stride) = ConfigureRgb32(reader);
            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(srcW, srcH));
            var dstW = Math.Max(1, (int)Math.Round(srcW * scale));
            var dstH = Math.Max(1, (int)Math.Round(srcH * scale));

            using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            var gif = new GifWriter(fs, dstW, dstH);
            var rgb = new byte[dstW * dstH * 3];

            var frames = 0;
            long nextTick = 0;
            long? origin = null;
            var halfFrameHns = TickHns / 4;
            // Frames repeat when the source is slower than 10 fps, so the animation keeps real time.
            while (frames < MaxFrames)
            {
                ct.ThrowIfCancellationRequested();
                IMFSample? sample;
                long ts;
                bool ok;
                try { ok = MfInterop.ReadSample(reader, MfInterop.FirstVideoStream, out sample, out ts); }
                catch (COMException) { break; }
                if (!ok) { MfInterop.Release(sample); break; }
                if (sample is null) continue;

                try
                {
                    origin ??= ts;
                    var t = ts - origin.Value;
                    if (t >= MaxSeconds * 10_000_000L) break;

                    var repeats = 0;
                    while (t + halfFrameHns >= nextTick && frames + repeats < MaxFrames)
                    {
                        repeats++;
                        nextTick += TickHns;
                    }
                    if (repeats == 0) continue;

                    var bytes = MfInterop.CopySampleBytes(sample);
                    Resample(bytes, srcW, srcH, stride, rgb, dstW, dstH);
                    gif.AddFrame(rgb, repeats * (100 / FramesPerSecond));
                    frames += repeats;
                }
                finally
                {
                    MfInterop.Release(sample);
                }
            }

            if (frames == 0) throw new ConversionException("Couldn't read any frames from this video.");
            gif.Finish();
        }
        catch (COMException ex)
        {
            throw new ConversionException("Windows can't decode the video in this file.", ex);
        }
        finally
        {
            MfInterop.Release(reader);
            MfInterop.Release(attrs);
        }
    }

    /// <summary>Asks the reader for RGB32 frames at the native size; returns the actual width, height and row stride in bytes (negative = bottom-up).</summary>
    private static (int W, int H, int Stride) ConfigureRgb32(IMFSourceReader reader)
    {
        var native = MfInterop.TryGetNativeType(reader, MfInterop.FirstVideoStream)
                     ?? throw new ConversionException("This file has no video.");
        MfInterop.TryGetUInt64(native, PInvoke.MF_MT_FRAME_SIZE, out var size);
        MfInterop.Release(native);

        IMFMediaType? want = null;
        IMFMediaType? got = null;
        try
        {
            want = MfInterop.CreateMediaType();
            MfInterop.SetGuid(want, PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            MfInterop.SetGuid(want, PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
            MfInterop.SetUInt32(want, PInvoke.MF_MT_INTERLACE_MODE, (uint)MFVideoInterlaceMode.MFVideoInterlace_Progressive);
            unsafe { reader.SetCurrentMediaType(MfInterop.FirstVideoStream, null, want); }

            reader.GetCurrentMediaType(MfInterop.FirstVideoStream, out got);
            MfInterop.TryGetUInt64(got, PInvoke.MF_MT_FRAME_SIZE, out var actual);
            if (actual != 0) size = actual;
            var w = (int)(size >> 32);
            var h = (int)(uint)size;
            if (w < 1 || h < 1) throw new ConversionException("Couldn't read the size of this video.");

            var stride = w * 4;
            if (MfInterop.TryGetUInt32(got, PInvoke.MF_MT_DEFAULT_STRIDE, out var s) && s != 0)
                stride = (int)s; // two's complement: negative for bottom-up
            return (w, h, stride);
        }
        catch (COMException ex)
        {
            throw new ConversionException("Windows can't decode the video in this file.", ex);
        }
        finally
        {
            MfInterop.Release(want);
            MfInterop.Release(got);
        }
    }

    /// <summary>Area-averaging resample of a BGRX frame into tightly packed RGB.</summary>
    internal static void Resample(byte[] src, int srcW, int srcH, int stride, byte[] dst, int dstW, int dstH)
    {
        var bottomUp = stride < 0;
        var rowBytes = Math.Abs(stride);
        for (var y = 0; y < dstH; y++)
        {
            var y0 = (int)((long)y * srcH / dstH);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * srcH / dstH));
            for (var x = 0; x < dstW; x++)
            {
                var x0 = (int)((long)x * srcW / dstW);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * srcW / dstW));
                long r = 0, g = 0, b = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    var row = (bottomUp ? srcH - 1 - sy : sy) * rowBytes;
                    var p = row + x0 * 4;
                    if (p + (x1 - x0) * 4 > src.Length) continue;
                    for (var sx = x0; sx < x1; sx++, p += 4)
                    {
                        b += src[p];
                        g += src[p + 1];
                        r += src[p + 2];
                    }
                }
                var n = Math.Max(1, (y1 - y0) * (x1 - x0));
                var o = (y * dstW + x) * 3;
                dst[o] = (byte)(r / n);
                dst[o + 1] = (byte)(g / n);
                dst[o + 2] = (byte)(b / n);
            }
        }
    }
}
