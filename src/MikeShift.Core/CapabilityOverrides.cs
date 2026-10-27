namespace MikeShift.Core;

/// <summary>
/// Pretends optional Windows extensions are missing, for screenshots and tests on a PC that has them (nobody can uninstall the HEIF extension just
/// to look at the "Get HEIF" tile). The app turns the dev switch <c>MIKESHIFT_CAPS=no-heif,no-hevc</c> into <see cref="Capabilities.Override"/>;
/// a shipped build never reads it (dev switches are ignored there, see docs/developer/architecture.md, "Developer switches"). It can only <em>remove</em> capabilities.
/// <para>Names: <c>no-heif</c> (HEIF decode and encode), <c>no-hevc</c> (HEVC decoder, which also ends HEIC encode), <c>no-av1</c> (AVIF and AV1),
/// <c>no-webp</c>, <c>no-ocr</c> (no text recognition language), <c>no-wic-webp</c> (Windows has no WebP decoder: WebP decoding goes through SkiaSharp, see <see cref="HidesWicDecoder"/>).</para>
/// </summary>
public static class CapabilityOverrides
{
    private static volatile bool _noWicWebp;

    /// <summary>
    /// Applies the dev switch text (null when dev switches are off, which clears everything): sets <see cref="Capabilities.Override"/> and the
    /// <c>no-wic-webp</c> flag read by <see cref="HidesWicDecoder"/>.
    /// </summary>
    public static void Apply(string? spec)
    {
        Capabilities.Override = Parse(spec);
        _noWicWebp = HasName(spec, "no-wic-webp");
    }

    /// <summary>
    /// True while <c>no-wic-webp</c> is on and <paramref name="path"/> is a WebP file: callers then skip Windows' own decoder, exactly as on a PC
    /// without the WebP Image Extension, and use the SkiaSharp fallback. Never throws: a file that cannot be read is left to the caller.
    /// </summary>
    public static bool HidesWicDecoder(string path) => _noWicWebp && IsWebp(path);

    private static bool IsWebp(string path)
    {
        try
        {
            return Images.ImageCodec.SniffFile(path) == Images.ImageContainer.Webp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasName(string? spec, string name) =>
        !string.IsNullOrWhiteSpace(spec)
        && spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses a comma-separated list such as <c>no-heif,no-hevc</c>; null for nothing usable. Unknown names are ignored.</summary>
    public static Func<Capabilities, Capabilities>? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var names = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (!names.Overlaps(["no-heif", "no-hevc", "no-av1", "no-webp", "no-ocr"])) return null;

        return caps =>
        {
            if (names.Contains("no-heif")) caps = caps with { HeifDecode = false, HeifEncode = false };
            if (names.Contains("no-hevc")) caps = caps with { HevcDecode = false, HeifEncode = false };
            if (names.Contains("no-av1")) caps = caps with { AvifDecode = false, Av1Decode = false };
            if (names.Contains("no-webp")) caps = caps with { WebpEncode = false };
            if (names.Contains("no-ocr")) caps = caps with { Ocr = false };
            return caps;
        };
    }
}
