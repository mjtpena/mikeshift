using MikeShift.Core.Actions;

namespace MikeShift.Core.Feedback;

/// <summary>Which optional Windows extension a file or the first-run check is missing.</summary>
public enum CodecNeed
{
    /// <summary>"HEIF Image Extensions" (free): lets Windows open HEIC/HEIF photos.</summary>
    Heif,
    /// <summary>"HEVC Video Extensions": the video decoder inside HEIC photos (paid in the Store, or preinstalled "from Device Manufacturer").</summary>
    Hevc,
    /// <summary>"AV1 Video Extension" (free): lets Windows open AVIF photos.</summary>
    Av1,
    /// <summary>"WebP Image Extensions" (free). MikeShift decodes WebP itself, so this is only offered when even that is unavailable.</summary>
    Webp,
}

/// <summary>
/// One missing extension, with every string the wheel, the first-run card and the failure dialog use for it.
/// </summary>
/// <param name="Need">Which extension.</param>
/// <param name="Title">The short tile label ("Get HEIF"). Fits a wheel tile.</param>
/// <param name="Description">The tile's full name for the hub, the shell and screen readers ("Get the free HEIF extension").</param>
/// <param name="Note">The caution pill: why this file cannot be converted yet.</param>
/// <param name="StoreName">The extension's name in the Microsoft Store.</param>
/// <param name="StoreProductId">Its Microsoft Store product id.</param>
/// <param name="Free">False when the Store sells it (HEVC Video Extensions).</param>
public sealed record CodecPrompt(CodecNeed Need, string Title, string Description, string Note, string StoreName, string StoreProductId, bool Free = true)
{
    /// <summary><c>ms-windows-store://pdp/?ProductId=...</c>: opens the extension's page in the Microsoft Store app.</summary>
    public string StoreUri => CodecHelp.StoreUri(StoreProductId);

    /// <summary>The sentence a failed conversion carries: what Windows cannot open yet and the one thing to install.</summary>
    public string FailureMessage =>
        $"{CodecHelp.CantOpenPrefix} {NounFor(Need)} yet. Install {(Free ? "the free " : "")}\"{StoreName}\" from the Microsoft Store, then restart MikeShift.";

    private static string NounFor(CodecNeed need) => need switch
    {
        CodecNeed.Heif or CodecNeed.Hevc => "HEIC photos",
        CodecNeed.Av1 => "AVIF photos",
        _ => "WebP images",
    };
}

/// <summary>
/// The HEIC dead end (positioning P0-3): a HEIC/AVIF photo that Windows cannot decode because an extension is missing used to be "Zip only".
/// This turns it into one actionable thing: the wheel offers a tile that opens the right Microsoft Store page, a pill says why, a failed
/// conversion says the same, and the welcome window offers the extensions up front. Pure functions of <see cref="Capabilities"/>.
/// </summary>
public static class CodecHelp
{
    /// <summary>Store product id of the free "HEIF Image Extensions".</summary>
    public const string HeifProductId = "9PMMSR1CGPWG";

    /// <summary>
    /// Store product id of "HEVC Video Extensions from Device Manufacturer" (checked on apps.microsoft.com, 2026-10-24). Its sibling "HEVC Video
    /// Extensions" (9NMZLZ57R3T7) is the one Microsoft sells; the Settings list has always linked this one.
    /// </summary>
    public const string HevcProductId = "9N4WGH0Z6VHQ";

    /// <summary>Store product id of the free "AV1 Video Extension".</summary>
    public const string Av1ProductId = "9MVZQVXJBQ9V";

    /// <summary>Store product id of the free "WebP Image Extensions".</summary>
    public const string WebpProductId = "9PG2DK419DRG";

    /// <summary>Every codec failure message starts with this (so a dialog can recognise one and offer the Store button).</summary>
    public const string CantOpenPrefix = "Windows can't open";

    /// <summary>Wheel tile glyph (Segoe Fluent Icons "Download").</summary>
    public const string Glyph = "";

    public static string StoreUri(string productId) => $"ms-windows-store://pdp/?ProductId={productId}";

    public static CodecPrompt Heif { get; } = new(CodecNeed.Heif, "Get HEIF", "Get the free HEIF extension",
        "HEIC photos need the free HEIF extension", "HEIF Image Extensions", HeifProductId);

    public static CodecPrompt Hevc { get; } = new(CodecNeed.Hevc, "Get HEVC", "Get the HEVC video extension",
        "HEIC photos also need the HEVC extension", "HEVC Video Extensions from Device Manufacturer", HevcProductId, Free: false);

    public static CodecPrompt Av1 { get; } = new(CodecNeed.Av1, "Get AV1", "Get the free AV1 extension",
        "AVIF photos need the free AV1 extension", "AV1 Video Extension", Av1ProductId);

    public static CodecPrompt Webp { get; } = new(CodecNeed.Webp, "Get WebP", "Get the free WebP extension",
        "WebP images need the free WebP extension", "WebP Image Extensions", WebpProductId);

    /// <summary>
    /// The extension that would let Windows open a file with extension <paramref name="ext"/> (lower case, no dot) on this machine, or null
    /// when the extension is not one that depends on an add-on or the add-ons are all there (so the file is simply damaged).
    /// </summary>
    public static CodecPrompt? ForExtension(string ext, Capabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        return ext switch
        {
            "heic" or "heif" => !caps.HeifDecode ? Heif : !caps.HevcDecode ? Hevc : null,
            "avif" => !caps.AvifDecode ? Av1 : null,
            _ => null,
        };
    }

    /// <summary>How many files of a long selection are looked at (an evenly spread sample), so thousands of undecodable files cost no time.</summary>
    private const int SampleSize = 12;

    /// <summary>
    /// The extension to offer for a selection: every file has an extension that depends on the same missing add-on and none of them could be
    /// classified by <paramref name="kindOf"/> (<see cref="Category.Other"/>, i.e. Windows cannot open it). Null otherwise (a mixed selection,
    /// a damaged file while the add-ons are installed, a PDF...). <paramref name="kindOf"/> is asked about an evenly spread sample only.
    /// </summary>
    public static CodecPrompt? ForSelection(IReadOnlyList<string> paths, Capabilities caps, Func<string, Category> kindOf)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(kindOf);
        if (paths.Count == 0) return null;

        CodecPrompt? prompt = null;
        foreach (var p in paths)
        {
            var wanted = ForExtension(Kind.ExtensionOf(p), caps);
            if (wanted is null || (prompt is not null && wanted.Need != prompt.Need)) return null;
            prompt = wanted;
        }

        var step = Math.Max(1, paths.Count / SampleSize);
        for (var i = 0; i < paths.Count; i += step)
            if (kindOf(paths[i]) != Category.Other) return null;
        return kindOf(paths[^1]) == Category.Other ? prompt : null;
    }

    /// <summary>
    /// <see cref="ForSelection"/> without looking at the files: the extension to offer when EVERY selected file has an extension that depends on
    /// the same missing add-on. This is what the Explorer menu can know without opening anything (it runs on Explorer's UI thread), and what the
    /// tray app asks again, with its live capabilities, when a "Get ..." line is picked, so the two always agree. Null for a mixed selection or
    /// when the add-ons are there.
    /// </summary>
    public static CodecPrompt? ForExtensions(IReadOnlyList<string> paths, Capabilities caps)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        if (paths.Count == 0) return null;

        CodecPrompt? prompt = null;
        foreach (var p in paths)
        {
            var wanted = ForExtension(Kind.ExtensionOf(p), caps);
            if (wanted is null || (prompt is not null && wanted.Need != prompt.Need)) return null;
            prompt = wanted;
        }
        return prompt;
    }

    /// <summary>The prompt whose tile is called <paramref name="title"/> ("Get HEIF"), or null for any other action title.</summary>
    public static CodecPrompt? FindByTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var wanted = title.Trim();
        foreach (var p in new[] { Heif, Hevc, Av1, Webp })
            if (string.Equals(p.Title, wanted, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    /// <summary>
    /// What the tray app does when the Explorer menu's "Get ..." line named <paramref name="title"/> is picked for <paramref name="paths"/>: the
    /// prompt whose Store page to open, decided with the live capabilities by the same rule the menu used (<see cref="ForExtensions"/>), or null
    /// when the title is not a "Get ..." line or the extension has been installed since the menu was drawn (the caller then resolves it normally).
    /// </summary>
    public static CodecPrompt? ResolvePick(string? title, IReadOnlyList<string> paths, Capabilities caps)
    {
        var asked = FindByTitle(title);
        return asked is not null && ForExtensions(paths, caps) is { } now && now.Need == asked.Need ? now : null;
    }

    /// <summary>The wheel tile for <paramref name="prompt"/>: on the Tools page next to Zip. The app opens <see cref="ActionDef.OpensUri"/> instead of running a job; run anyway (CLI), it fails with the same words as the pill.</summary>
    public static ActionDef TileFor(CodecPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return new ActionDef(prompt.Title, WheelPage.Tool,
            (paths, _, _) => Task.FromResult(ActionResult.Failed(paths.Count, paths.Count > 0 ? paths[0] : "", prompt.FailureMessage)))
        {
            Glyph = Glyph,
            Description = prompt.Description,
            OpensUri = prompt.StoreUri,
        };
    }

    /// <summary>The caution pill for a selection that offers a codec tile: <see cref="CodecPrompt.Note"/>, or null when the selection has no such tile.</summary>
    public static string? NoteFor(IReadOnlyList<string> paths, Capabilities caps, Func<string, Category> kindOf) =>
        ForSelection(paths, caps, kindOf)?.Note;

    /// <summary>The extensions to suggest at first run, most important first: only those this PC lacks.</summary>
    public static IReadOnlyList<CodecPrompt> Missing(Capabilities caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        var list = new List<CodecPrompt>();
        if (!caps.HeifDecode) list.Add(Heif);
        if (!caps.HevcDecode) list.Add(Hevc);
        if (!caps.AvifDecode) list.Add(Av1);
        if (!caps.WebpEncode) list.Add(Webp);
        return list;
    }

    /// <summary>
    /// The Store page for a failure <paramref name="message"/> produced by <see cref="CodecPrompt.FailureMessage"/>, or null if the message
    /// is about something else.
    /// </summary>
    public static string? StoreUriInMessage(string? message)
    {
        if (message is null || !message.StartsWith(CantOpenPrefix, StringComparison.Ordinal)) return null;
        foreach (var p in new[] { Heif, Hevc, Av1, Webp })
            if (message.Contains(p.StoreName, StringComparison.Ordinal)) return p.StoreUri;
        return null;
    }

    /// <summary>
    /// What to say when a HEIC/AVIF photo could not be decoded and the cause is a missing extension: the matching
    /// <see cref="CodecPrompt.FailureMessage"/>, or null if the extensions are there (the file is damaged). The three arguments are live
    /// checks, so a conversion needs no full capability probe.
    /// </summary>
    public static string? DecodeFailureMessage(string ext, bool hasHeifDecoder, Func<bool> hasHevcDecoder, bool hasAvifDecoder)
    {
        ArgumentNullException.ThrowIfNull(hasHevcDecoder);
        var heic = ext is "heic" or "heif";
        var caps = Capabilities.All with
        {
            HeifDecode = hasHeifDecoder,
            AvifDecode = hasAvifDecoder,
            HevcDecode = !heic || !hasHeifDecoder || hasHevcDecoder(),
        };
        return ForExtension(ext, caps)?.FailureMessage;
    }
}
