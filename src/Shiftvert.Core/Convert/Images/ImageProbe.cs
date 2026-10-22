namespace Shiftvert.Core.Images;

/// <summary>
/// Image decodability and codec capability probing. OWNED BY W2a (Images): replace the stub bodies,
/// keep the signatures.
/// </summary>
public static class ImageProbe
{
    /// <summary>
    /// True if a decoder can be created for <paramref name="path"/> (WIC, SkiaSharp fallback). Must be cheap and must
    /// not decode pixels; it is called lazily for each image-extension path while the wheel is populated.
    /// <c>Kind.Of</c> caches the verdict per path.
    /// </summary>
    // W2a: implement
    public static bool CanDecode(string path) => true;

    /// <summary>
    /// Fills in <see cref="Capabilities.HeifEncode"/>, <see cref="Capabilities.WebpEncode"/> and
    /// <see cref="Capabilities.AvifDecode"/> on <paramref name="caps"/> and returns the updated record.
    /// </summary>
    // W2a: implement (stub returns the input unchanged, i.e. all image flags false)
    public static Capabilities ProbeCapabilities(Capabilities caps) => caps;
}
