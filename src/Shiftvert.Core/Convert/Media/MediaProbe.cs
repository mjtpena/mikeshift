namespace Shiftvert.Core.Media;

/// <summary>
/// Audio/video openability and codec capability probing. OWNED BY W2c (Media): replace the stub bodies,
/// keep the signatures.
/// </summary>
public static class MediaProbe
{
    /// <summary>
    /// True if Media Foundation (or the built-in AIFF reader) can open <paramref name="path"/>
    /// (<c>MFCreateSourceReaderFromURL</c> succeeds). Must not decode; <c>Kind.Of</c> caches the verdict per path.
    /// </summary>
    // W2c: implement
    public static bool CanOpen(string path) => true;

    /// <summary>
    /// Fills in Mp3Encode, FlacEncode, H264Encode, AacEncode, HevcDecode, Vp9Decode, Av1Decode and WebmOpen on
    /// <paramref name="caps"/> (via <c>MFTEnumEx</c>) and returns the updated record.
    /// </summary>
    // W2c: implement (stub returns the input unchanged, i.e. all media flags false)
    public static Capabilities ProbeCapabilities(Capabilities caps) => caps;
}
