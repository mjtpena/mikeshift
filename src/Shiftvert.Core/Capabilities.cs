using Microsoft.Win32;
using Shiftvert.Core.Images;
using Shiftvert.Core.Media;

namespace Shiftvert.Core;

/// <summary>
/// What this machine can actually do (optional Windows codec packs, MFTs, Word).
/// Probed once at startup (<see cref="Current"/>); the action catalog hides tiles that cannot work.
/// An immutable record so tests build fakes with <c>Capabilities.All with { HeifEncode = false }</c>.
/// </summary>
public sealed record Capabilities
{
    /// <summary>WIC can create a HEIF encoder (Store "HEIF Image Extensions" + HEVC extension). Probed by W2a.</summary>
    public bool HeifEncode { get; init; }

    /// <summary>WebP can be encoded (SkiaSharp is present and loads). Probed by W2a.</summary>
    public bool WebpEncode { get; init; }

    /// <summary>AVIF input can be decoded (free AV1 extension). Informational; AVIF output is never offered. Probed by W2a.</summary>
    public bool AvifDecode { get; init; }

    /// <summary>The Media Foundation MP3 encoder MFT exists. Probed by W2c.</summary>
    public bool Mp3Encode { get; init; }

    /// <summary>The Media Foundation FLAC encoder MFT exists (Win10 1703+). Probed by W2c.</summary>
    public bool FlacEncode { get; init; }

    /// <summary>The H.264 encoder MFT exists. Probed by W2c.</summary>
    public bool H264Encode { get; init; }

    /// <summary>The AAC encoder MFT exists. Probed by W2c.</summary>
    public bool AacEncode { get; init; }

    /// <summary>An HEVC decoder MFT exists. Probed by W2c.</summary>
    public bool HevcDecode { get; init; }

    /// <summary>A VP9 decoder MFT exists. Probed by W2c.</summary>
    public bool Vp9Decode { get; init; }

    /// <summary>An AV1 decoder MFT exists. Probed by W2c.</summary>
    public bool Av1Decode { get; init; }

    /// <summary>Media Foundation can open <c>.webm</c> files. Probed by W2c.</summary>
    public bool WebmOpen { get; init; }

    /// <summary>Microsoft Word is installed (<c>HKCR\Word.Application</c>); enables legacy .doc and high-fidelity PDF export. Probed by W1.</summary>
    public bool WordCom { get; init; }

    /// <summary>Everything available. The default for tests and for <c>ActionCatalog</c> unit tests.</summary>
    public static Capabilities All { get; } = new()
    {
        HeifEncode = true, WebpEncode = true, AvifDecode = true,
        Mp3Encode = true, FlacEncode = true, H264Encode = true, AacEncode = true,
        HevcDecode = true, Vp9Decode = true, Av1Decode = true, WebmOpen = true,
        WordCom = true,
    };

    /// <summary>Nothing optional available (every flag false).</summary>
    public static Capabilities None { get; } = new();

    private static readonly Lazy<Capabilities> s_current = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The probe result for this machine, computed once per process (thread-safe).</summary>
    public static Capabilities Current => s_current.Value;

    /// <summary>
    /// Probes this machine now (uncached). Composes <see cref="ImageProbe.ProbeCapabilities"/> (W2a),
    /// <see cref="MediaProbe.ProbeCapabilities"/> (W2c) and the Word COM check (W1). Never throws:
    /// a probe that fails leaves its flags false.
    /// </summary>
    public static Capabilities Probe()
    {
        var caps = new Capabilities { WordCom = DetectWord() };
        caps = Safe(() => ImageProbe.ProbeCapabilities(caps), caps);
        caps = Safe(() => MediaProbe.ProbeCapabilities(caps), caps);
        return caps;
    }

    private static Capabilities Safe(Func<Capabilities> probe, Capabilities fallback)
    {
        try { return probe(); }
        catch (Exception) { return fallback; }
    }

    private static bool DetectWord()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("Word.Application");
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
