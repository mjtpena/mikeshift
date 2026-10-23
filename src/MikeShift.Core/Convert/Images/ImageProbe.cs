namespace MikeShift.Core.Images;

/// <summary>
/// Image decodability and codec capability probing (PLAN §1.2 Image rows, §3.4 Capabilities probe).
/// </summary>
public static class ImageProbe
{
    /// <summary>
    /// True if a decoder can be created for <paramref name="path"/> (WIC first, SkiaSharp fallback). Reads the header only,
    /// never decodes pixels; <c>Kind.Of</c> caches the verdict per path. Never throws.
    /// </summary>
    public static bool CanDecode(string path)
    {
        try
        {
            return ImageCodec.CanDecodeHeader(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Sets <see cref="Capabilities.HeifEncode"/> (a WIC HEIF encoder can really encode a tiny picture: needs the Store
    /// "HEIF Image Extensions" and "HEVC Video Extensions"), <see cref="Capabilities.WebpEncode"/> (SkiaSharp loads and encodes)
    /// and <see cref="Capabilities.AvifDecode"/> (a WIC decoder claims <c>.avif</c>).
    /// </summary>
    public static Capabilities ProbeCapabilities(Capabilities caps) => caps with
    {
        HeifEncode = ImageCodec.CanEncodeHeif(),
        WebpEncode = ImageCodec.CanEncodeWebp(),
        AvifDecode = ImageCodec.CanDecodeAvif(),
    };
}
