using Microsoft.Win32;
using MikeShift.Core.Images;
using MikeShift.Core.Media;

namespace MikeShift.Core;

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

    private static readonly ProbeCache s_cache = new(Probe, Quick);

    /// <summary>
    /// The probe result for this machine, computed once per process (thread-safe). The first access runs the probe and can take
    /// a second or more (WIC HEIF encode, Media Foundation transforms), so UI and message-loop threads must not call this before
    /// <see cref="IsProbed"/> is true: use <see cref="TryGetCurrent"/> or <see cref="CurrentAsync"/> instead.
    /// </summary>
    public static Capabilities Current => s_cache.Value;

    /// <summary>True once the machine probe has finished, so <see cref="Current"/> returns immediately.</summary>
    public static bool IsProbed => s_cache.IsReady;

    /// <summary>
    /// Never blocks. Returns true with the real probe result once <see cref="IsProbed"/>; until then it returns false and a
    /// conservative <paramref name="capabilities"/> snapshot (<see cref="Quick"/>) so callers can show something right away and
    /// refresh when <see cref="CurrentAsync"/> completes.
    /// </summary>
    public static bool TryGetCurrent(out Capabilities capabilities) => s_cache.TryGet(out capabilities);

    /// <summary>The probe result, awaited. Starts the probe on a thread-pool thread if nothing has yet; completes when it is done.</summary>
    public static Task<Capabilities> CurrentAsync => s_cache.ValueAsync;

    /// <summary>
    /// What is known without running any codec probe: nothing optional is assumed to exist, except that Word is detected (one
    /// registry read). Tiles built from this are a safe subset of the real ones.
    /// </summary>
    public static Capabilities Quick() => new() { WordCom = DetectWord() };

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

/// <summary>
/// Runs a slow probe at most once and lets callers wait for it (<see cref="Value"/>), await it (<see cref="ValueAsync"/>) or peek
/// without blocking (<see cref="TryGet"/>, which hands out a quick stand-in until the probe is done). Split out from
/// <see cref="Capabilities"/> so the non-blocking behaviour can be tested with a probe that waits for the test.
/// </summary>
internal sealed class ProbeCache
{
    private readonly Func<Capabilities> _quick;
    private readonly Lazy<Capabilities> _value;
    private readonly Lazy<Task<Capabilities>> _async;

    public ProbeCache(Func<Capabilities> probe, Func<Capabilities> quick)
    {
        _quick = quick;
        _value = new Lazy<Capabilities>(probe, LazyThreadSafetyMode.ExecutionAndPublication);
        _async = new Lazy<Task<Capabilities>>(() => Task.Run(() => _value.Value), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Blocks until the probe has run (running it on this thread if nobody has started it).</summary>
    public Capabilities Value => _value.Value;

    /// <summary>True once the probe has finished.</summary>
    public bool IsReady => _value.IsValueCreated;

    /// <summary>The probe result as a task; starts the probe on the thread pool when nobody has.</summary>
    public Task<Capabilities> ValueAsync => _async.Value;

    /// <summary>True and the real result when ready; otherwise false and the quick stand-in. Never waits for a running probe.</summary>
    public bool TryGet(out Capabilities capabilities)
    {
        if (_value.IsValueCreated)
        {
            capabilities = _value.Value;
            return true;
        }
        capabilities = _quick();
        return false;
    }
}
