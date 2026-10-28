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
    /// <summary>WIC can create a HEIF encoder (Store "HEIF Image Extensions" + HEVC extension).</summary>
    public bool HeifEncode { get; init; }

    /// <summary>
    /// WIC has a decoder for HEIC/HEIF (the free Store "HEIF Image Extensions" are installed). Reading HEIC photos also needs an HEVC
    /// decoder (<see cref="HevcDecode"/>).
    /// </summary>
    public bool HeifDecode { get; init; }

    /// <summary>WebP can be encoded (SkiaSharp is present and loads).</summary>
    public bool WebpEncode { get; init; }

    /// <summary>AVIF input can be decoded (free AV1 extension). Informational; AVIF output is never offered.</summary>
    public bool AvifDecode { get; init; }

    /// <summary>The Media Foundation MP3 encoder MFT exists.</summary>
    public bool Mp3Encode { get; init; }

    /// <summary>The Media Foundation FLAC encoder MFT exists (Win10 1703+).</summary>
    public bool FlacEncode { get; init; }

    /// <summary>The H.264 encoder MFT exists.</summary>
    public bool H264Encode { get; init; }

    /// <summary>The AAC encoder MFT exists.</summary>
    public bool AacEncode { get; init; }

    /// <summary>An HEVC decoder MFT exists.</summary>
    public bool HevcDecode { get; init; }

    /// <summary>A VP9 decoder MFT exists.</summary>
    public bool Vp9Decode { get; init; }

    /// <summary>An AV1 decoder MFT exists.</summary>
    public bool Av1Decode { get; init; }

    /// <summary>Media Foundation can open <c>.webm</c> files.</summary>
    public bool WebmOpen { get; init; }

    /// <summary>
    /// Windows text recognition (<c>Windows.Media.Ocr</c>) has a language for this user (OCR comes with the language packs). Gates the picture
    /// TXT tile; without it PDF TXT still reads text layers but refuses scans.
    /// </summary>
    public bool Ocr { get; init; }

    /// <summary>Windows face detection (<c>FaceDetector.IsSupported</c>) works on this PC. Gates the Blur faces tile.</summary>
    public bool FaceDetect { get; init; }

    /// <summary>
    /// The Windows AI image scaler (<c>ImageScaler</c>, Copilot+ PCs) can upscale a picture here, or can once Windows has installed its model:
    /// true when its ready state is <c>Ready</c> or <c>NotReady</c> (supported, model not installed yet), false when it is disabled by the
    /// user, not supported, or the component cannot load (an unpackaged run). Gates the Upscale 2x tile.
    /// </summary>
    public bool AiUpscale { get; init; }

    /// <summary>Microsoft Word is installed (<c>HKCR\Word.Application</c>). Informational: MikeShift only drives Word when <see cref="WordCom"/> is true.</summary>
    public bool WordInstalled { get; init; }

    /// <summary>
    /// Word is installed AND the user allowed MikeShift to use it (the "Use Microsoft Word" setting; <c>--use-word</c> on the command line).
    /// Only then do legacy .doc input and Word's own DOCX-to-PDF export exist; otherwise MikeShift stays fully local and never starts Word
    /// (Word connects to Microsoft services on its own). The only flag the converters and the catalog consult for Word.
    /// </summary>
    public bool WordCom { get; init; }

    /// <summary>
    /// This snapshot with Word allowed or not: <see cref="WordCom"/> becomes <paramref name="allowed"/> and <see cref="WordInstalled"/>
    /// (never true when Word is not installed).
    /// </summary>
    public Capabilities WithWord(bool allowed)
    {
        var com = allowed && WordInstalled;
        return com == WordCom ? this : this with { WordCom = com };
    }

    /// <summary>Everything available. The default for tests and for <c>ActionCatalog</c> unit tests.</summary>
    public static Capabilities All { get; } = new()
    {
        HeifEncode = true, HeifDecode = true, WebpEncode = true, AvifDecode = true,
        Mp3Encode = true, FlacEncode = true, H264Encode = true, AacEncode = true,
        HevcDecode = true, Vp9Decode = true, Av1Decode = true, WebmOpen = true, Ocr = true, FaceDetect = true, AiUpscale = true,
        WordInstalled = true, WordCom = true,
    };

    /// <summary>Nothing optional available (every flag false).</summary>
    public static Capabilities None { get; } = new();

    private static readonly ProbeCache s_cache = new(Probe, Quick);

    /// <summary>The default of the "Use Microsoft Word" setting: off. MikeShift is fully local unless the user turns Word on.</summary>
    public const bool WordAllowedByDefault = false;

    private static volatile bool s_wordAllowed = WordAllowedByDefault;

    /// <summary>
    /// The host's permission to use Word, process-wide and off by default. The tray app sets it from the "Use Microsoft Word" preference
    /// (and again when that changes); the command line leaves it off and decides per run (<c>--use-word</c>). Every snapshot this class
    /// hands out (<see cref="Current"/>, <see cref="TryGetCurrent"/>, <see cref="Quick"/>, <see cref="Probe"/>) reports
    /// <see cref="WordCom"/> from it, so flipping it takes effect on the next catalog or job without re-probing codecs.
    /// </summary>
    public static bool WordAllowed
    {
        get => s_wordAllowed;
        set => s_wordAllowed = value;
    }

    /// <summary>
    /// The probe result for this machine, computed once per process (thread-safe). The first access runs the probe and can take
    /// a second or more (WIC HEIF encode, Media Foundation transforms), so UI and message-loop threads must not call this before
    /// <see cref="IsProbed"/> is true: use <see cref="TryGetCurrent"/> or <see cref="CurrentAsync"/> instead.
    /// </summary>
    public static Capabilities Current => ApplyWordPolicy(s_cache.Value);

    /// <summary>True once the machine probe has finished, so <see cref="Current"/> returns immediately.</summary>
    public static bool IsProbed => s_cache.IsReady;

    /// <summary>
    /// Never blocks. Returns true with the real probe result once <see cref="IsProbed"/>; until then it returns false and a
    /// conservative <paramref name="capabilities"/> snapshot (<see cref="Quick"/>) so callers can show something right away and
    /// refresh when <see cref="CurrentAsync"/> completes.
    /// </summary>
    public static bool TryGetCurrent(out Capabilities capabilities)
    {
        var probed = s_cache.TryGet(out var caps);
        capabilities = ApplyWordPolicy(caps);
        return probed;
    }

    /// <summary>The probe result, awaited. Starts the probe on a thread-pool thread if nothing has yet; completes when it is done.</summary>
    public static Task<Capabilities> CurrentAsync => WithPolicyAsync(s_cache.ValueAsync);

    /// <summary>
    /// What is known without running any codec probe: nothing optional is assumed to exist, except that Word is detected (one
    /// registry read). Tiles built from this are a safe subset of the real ones.
    /// </summary>
    public static Capabilities Quick() => WithDetectedWord(new Capabilities());

    /// <summary>
    /// Probes this machine now (uncached). Composes <see cref="ImageProbe.ProbeCapabilities"/>,
    /// <see cref="MediaProbe.ProbeCapabilities"/> and the Word COM check. Never throws:
    /// a probe that fails leaves its flags false.
    /// </summary>
    public static Capabilities Probe()
    {
        var caps = WithDetectedWord(new Capabilities());
        caps = Safe(() => ImageProbe.ProbeCapabilities(caps), caps);
        caps = Safe(() => MediaProbe.ProbeCapabilities(caps), caps);
        return caps;
    }

    private static Capabilities Safe(Func<Capabilities> probe, Capabilities fallback)
    {
        try { return probe(); }
        catch (Exception) { return fallback; }
    }

    private static Capabilities WithDetectedWord(Capabilities caps) =>
        (caps with { WordInstalled = DetectWord() }).WithWord(WordAllowed);

    private static async Task<Capabilities> WithPolicyAsync(Task<Capabilities> probe) =>
        ApplyWordPolicy(await probe.ConfigureAwait(false));

    /// <summary>
    /// Dev/test only (see <see cref="CapabilityOverrides"/>): removes capabilities from every snapshot, so a PC that has the HEIF extension can show
    /// what one without it sees, and can force <see cref="AiUpscale"/> on for a PC that is not a Copilot+ PC. Null (the default) changes nothing.
    /// </summary>
    public static Func<Capabilities, Capabilities>? Override { get; set; }

    // The cached probe was made when the permission may have been different.
    private static Capabilities ApplyWordPolicy(Capabilities cached)
    {
        var caps = cached.WithWord(WordAllowed);
        return Override is { } hook ? hook(caps) : caps;
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
