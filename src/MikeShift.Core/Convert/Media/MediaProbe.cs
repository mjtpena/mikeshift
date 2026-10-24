using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace MikeShift.Core.Media;

/// <summary>
/// Audio/video openability and codec capability probing (PLAN §1.2 "Category detection", §3.4 Capabilities probe).
/// </summary>
public static class MediaProbe
{
    /// <summary>
    /// True if Media Foundation (or the built-in AIFF reader) can open <paramref name="path"/>
    /// (<c>MFCreateSourceReaderFromURL</c> succeeds). Does not decode; <c>Kind.Of</c> caches the verdict per path.
    /// </summary>
    public static bool CanOpen(string path)
    {
        try
        {
            // Media Foundation has no AIFF source, so the built-in reader answers for those files.
            if (AiffFile.LooksLikeAiff(path)) return true;

            var reader = MfInterop.TryOpenReader(path);
            if (reader is null) return false;
            MfInterop.Release(reader);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>True if a Media Foundation HEVC decoder exists (the Store "HEVC Video Extensions", or a hardware decoder). Never throws.</summary>
    public static bool HasHevcDecoder()
    {
        try
        {
            MfInterop.EnsureStarted();
            return HasMft(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, PInvoke.MFVideoFormat_HEVC, output: false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Fills in Mp3Encode, FlacEncode, H264Encode, AacEncode, HevcDecode, Vp9Decode, Av1Decode and WebmOpen on
    /// <paramref name="caps"/> (via <c>MFTEnumEx</c> and the registered byte-stream handlers) and returns the updated record.
    /// </summary>
    public static Capabilities ProbeCapabilities(Capabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        try
        {
            MfInterop.EnsureStarted();
        }
        catch (ConversionException)
        {
            return caps;
        }

        return caps with
        {
            Mp3Encode = HasMft(PInvoke.MFT_CATEGORY_AUDIO_ENCODER, PInvoke.MFMediaType_Audio, PInvoke.MFAudioFormat_MP3, output: true),
            FlacEncode = HasMft(PInvoke.MFT_CATEGORY_AUDIO_ENCODER, PInvoke.MFMediaType_Audio, PInvoke.MFAudioFormat_FLAC, output: true),
            H264Encode = HasMft(PInvoke.MFT_CATEGORY_VIDEO_ENCODER, PInvoke.MFMediaType_Video, PInvoke.MFVideoFormat_H264, output: true),
            AacEncode = HasMft(PInvoke.MFT_CATEGORY_AUDIO_ENCODER, PInvoke.MFMediaType_Audio, PInvoke.MFAudioFormat_AAC, output: true),
            HevcDecode = HasMft(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, PInvoke.MFVideoFormat_HEVC, output: false),
            Vp9Decode = HasMft(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, PInvoke.MFVideoFormat_VP90, output: false),
            Av1Decode = HasMft(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, PInvoke.MFVideoFormat_AV1, output: false),
            WebmOpen = WebmHandlerRegistered(),
        };
    }

    /// <summary>True if at least one MFT of <paramref name="category"/> accepts (input) or produces (output) <paramref name="subtype"/>.</summary>
    internal static unsafe bool HasMft(Guid category, Guid major, Guid subtype, bool output)
    {
        try
        {
            var info = new MFT_REGISTER_TYPE_INFO { guidMajorType = major, guidSubtype = subtype };
            const MFT_ENUM_FLAG flags = MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_ASYNCMFT
                                        | MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG.MFT_ENUM_FLAG_TRANSCODE_ONLY;

            IMFActivate_unmanaged** activates;
            uint count;
            var hr = output
                ? PInvoke.MFTEnumEx(category, flags, null, info, out activates, out count)
                : PInvoke.MFTEnumEx(category, flags, info, null, out activates, out count);
            if (hr.Failed) return false;

            for (uint i = 0; i < count; i++)
                Marshal.Release((IntPtr)activates[i]);
            if (activates is not null)
                Marshal.FreeCoTaskMem((IntPtr)activates);
            return count > 0;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Media Foundation binds .webm only when the "Web Media Extensions" (or equivalent) byte-stream handler is installed.</summary>
    private static bool WebmHandlerRegistered()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                foreach (var sub in new[] { @"SOFTWARE\Microsoft\Windows Media Foundation\ByteStreamHandlers\.webm",
                                            @"SOFTWARE\Microsoft\Windows Media Foundation\ByteStreamHandlers\video/webm" })
                {
                    using var key = hklm.OpenSubKey(sub);
                    if (key is { SubKeyCount: > 0 }) return true;
                }
            }
            catch (Exception)
            {
                // registry unavailable: fall through to "no"
            }
        }
        return false;
    }
}
