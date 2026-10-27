namespace MikeShift.Core.Images;

/// <summary>
/// Image decodability and codec capability probing.
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
    /// "HEIF Image Extensions" and "HEVC Video Extensions"), <see cref="Capabilities.WebpEncode"/> (SkiaSharp loads and encodes),
    /// <see cref="Capabilities.AvifDecode"/> (a WIC decoder claims <c>.avif</c>) and <see cref="Capabilities.Ocr"/> (Windows can create a text
    /// recognizer for one of the user's languages).
    /// </summary>
    public static Capabilities ProbeCapabilities(Capabilities caps) => caps with
    {
        HeifEncode = ImageCodec.CanEncodeHeif(),
        HeifDecode = ImageCodec.CanDecodeHeif(),
        WebpEncode = ImageCodec.CanEncodeWebp(),
        AvifDecode = ImageCodec.CanDecodeAvif(),
        Ocr = TextRecognition.IsAvailable(),
    };
}
