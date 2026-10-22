using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com.StructuredStorage;

namespace Shiftvert.Core.Media;

/// <summary>
/// The only place (with <see cref="MediaProbe"/>, <see cref="MediaInfo"/>, <see cref="MfPcmReader"/> and
/// <see cref="GifExporter"/>) that talks to Media Foundation COM objects. Everything above this layer deals in
/// plain managed values. Rules: every COM object obtained here is released with <see cref="Release"/> in a
/// <c>finally</c>; <c>MFStartup</c> runs once per process and is balanced by one <c>MFShutdown</c> at process exit.
/// </summary>
internal static class MfInterop
{
    // Source Reader stream selectors (mfreadwrite.h). Declared here to avoid enum casts.
    public const uint FirstVideoStream = 0xFFFFFFFC;
    public const uint FirstAudioStream = 0xFFFFFFFD;
    public const uint AllStreams = 0xFFFFFFFE;
    public const uint MediaSource = 0xFFFFFFFF;

    private static readonly object s_gate = new();
    private static bool s_started;
    private static int s_startHr;

    /// <summary>Starts Media Foundation once. Throws <see cref="ConversionException"/> if the platform is unavailable.</summary>
    public static void EnsureStarted()
    {
        lock (s_gate)
        {
            if (!s_started)
            {
                s_startHr = PInvoke.MFStartup(PInvoke.MF_VERSION, PInvoke.MFSTARTUP_FULL).Value;
                if (s_startHr >= 0)
                {
                    s_started = true;
                    AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
                    {
                        try { PInvoke.MFShutdown(); } catch (Exception) { /* process is ending */ }
                    };
                }
            }
        }
        if (!s_started)
            throw new ConversionException("Windows Media Foundation is not available on this PC.");
    }

    /// <summary>Releases a COM object (all references held by this RCW). Safe on null and non-COM objects.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { /* already gone */ }
        }
    }

    /// <summary>Opens <paramref name="path"/> with a Source Reader. Returns null if Media Foundation cannot open it.</summary>
    public static IMFSourceReader? TryOpenReader(string path, IMFAttributes? attributes = null)
    {
        EnsureStarted();
        var hr = PInvoke.MFCreateSourceReaderFromURL(path, attributes, out IMFSourceReader reader);
        return hr.Succeeded ? reader : null;
    }

    /// <summary>A fresh attribute store (used for the "enable video processing" switch).</summary>
    public static IMFAttributes CreateAttributes(uint capacity = 4)
    {
        EnsureStarted();
        PInvoke.MFCreateAttributes(out IMFAttributes attrs, capacity).ThrowOnFailure();
        return attrs;
    }

    /// <summary>A fresh, empty media type.</summary>
    public static IMFMediaType CreateMediaType()
    {
        EnsureStarted();
        PInvoke.MFCreateMediaType(out IMFMediaType mt).ThrowOnFailure();
        return mt;
    }

    // ------------------------------------------------------------ attribute helpers

    public static unsafe bool TryGetUInt32(IMFAttributes a, Guid key, out uint value)
    {
        try { a.GetUINT32(&key, out value); return true; }
        catch (COMException) { value = 0; return false; }
    }

    public static unsafe bool TryGetUInt64(IMFAttributes a, Guid key, out ulong value)
    {
        try { a.GetUINT64(&key, out value); return true; }
        catch (COMException) { value = 0; return false; }
    }

    public static unsafe bool TryGetGuid(IMFAttributes a, Guid key, out Guid value)
    {
        Guid v = default;
        try { a.GetGUID(&key, &v); value = v; return true; }
        catch (COMException) { value = default; return false; }
    }

    public static unsafe void SetUInt32(IMFAttributes a, Guid key, uint value) => a.SetUINT32(&key, value);

    public static unsafe void SetUInt64(IMFAttributes a, Guid key, ulong value) => a.SetUINT64(&key, value);

    public static unsafe void SetGuid(IMFAttributes a, Guid key, Guid value) => a.SetGUID(&key, &value);

    /// <summary>Packs two 32-bit values the way MF stores sizes and ratios.</summary>
    public static ulong Pack(uint hi, uint lo) => ((ulong)hi << 32) | lo;

    /// <summary>Reads <c>MF_PD_DURATION</c> of the presentation; null if unknown.</summary>
    public static unsafe TimeSpan? GetDuration(IMFSourceReader reader)
    {
        PROPVARIANT pv = default;
        try
        {
            var key = PInvoke.MF_PD_DURATION;
            reader.GetPresentationAttribute(MediaSource, &key, out pv);
            PInvoke.PropVariantToUInt64(in pv, out var hundredNs).ThrowOnFailure();
            return TimeSpan.FromTicks((long)Math.Min(hundredNs, (ulong)long.MaxValue));
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            PInvoke.PropVariantClear(ref pv);
        }
    }

    /// <summary>The native media type of the first stream of the given kind, or null when the file has none.</summary>
    public static IMFMediaType? TryGetNativeType(IMFSourceReader reader, uint stream)
    {
        try
        {
            reader.GetNativeMediaType(stream, 0, out var mt);
            return mt;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Selects only the given stream on the reader (all others are skipped).</summary>
    public static void SelectOnly(IMFSourceReader reader, uint stream)
    {
        reader.SetStreamSelection(AllStreams, false);
        reader.SetStreamSelection(stream, true);
    }

    /// <summary>
    /// Reads one sample. Returns false at end of stream (or on a stream error). <paramref name="sample"/> may be null for a
    /// gap/tick flag; the caller must <see cref="Release"/> a non-null sample.
    /// </summary>
    public static unsafe bool ReadSample(IMFSourceReader reader, uint stream, out IMFSample? sample, out long timestamp100ns)
    {
        IMFSample_unmanaged* raw = null;
        uint flags = 0;
        long ts = 0;
        reader.ReadSample(stream, 0, null, &flags, &ts, &raw);
        timestamp100ns = ts;
        sample = null;
        if (raw is not null)
        {
            try { sample = (IMFSample)Marshal.GetObjectForIUnknown((IntPtr)raw); }
            finally { Marshal.Release((IntPtr)raw); }
        }
        // MF_SOURCE_READER_FLAG_ENDOFSTREAM = 0x2, ERROR = 0x1
        return (flags & 0x3) == 0;
    }

    /// <summary>Copies the sample's media data to a managed array (one contiguous buffer).</summary>
    public static unsafe byte[] CopySampleBytes(IMFSample sample)
    {
        sample.ConvertToContiguousBuffer(out var buffer);
        try
        {
            byte* p = null;
            uint len = 0;
            buffer.Lock(&p, null, &len);
            try
            {
                var bytes = new byte[len];
                Marshal.Copy((IntPtr)p, bytes, 0, (int)len);
                return bytes;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Release(buffer);
        }
    }
}
