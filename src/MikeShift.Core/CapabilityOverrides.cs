namespace MikeShift.Core;

/// <summary>
/// Pretends optional Windows extensions are missing, for screenshots and tests on a PC that has them (nobody can uninstall the HEIF extension just
/// to look at the "Get HEIF" tile). The app turns the dev switch <c>MIKESHIFT_CAPS=no-heif,no-hevc</c> into <see cref="Capabilities.Override"/>;
/// a shipped build never reads it (dev switches are ignored there, see docs/developer/architecture.md, "Developer switches"). It can only <em>remove</em> capabilities.
/// <para>Names: <c>no-heif</c> (HEIF decode and encode), <c>no-hevc</c> (HEVC decoder, which also ends HEIC encode), <c>no-av1</c> (AVIF and AV1),
/// <c>no-webp</c>.</para>
/// </summary>
public static class CapabilityOverrides
{
    /// <summary>Parses a comma-separated list such as <c>no-heif,no-hevc</c>; null for nothing usable. Unknown names are ignored.</summary>
    public static Func<Capabilities, Capabilities>? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var names = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (!names.Overlaps(["no-heif", "no-hevc", "no-av1", "no-webp"])) return null;

        return caps =>
        {
            if (names.Contains("no-heif")) caps = caps with { HeifDecode = false, HeifEncode = false };
            if (names.Contains("no-hevc")) caps = caps with { HevcDecode = false, HeifEncode = false };
            if (names.Contains("no-av1")) caps = caps with { AvifDecode = false, Av1Decode = false };
            if (names.Contains("no-webp")) caps = caps with { WebpEncode = false };
            return caps;
        };
    }
}
