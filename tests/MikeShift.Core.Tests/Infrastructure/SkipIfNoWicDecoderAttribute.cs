using MikeShift.Core.Tests.Integrity;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// A <c>[Fact]</c> that is skipped (reported as skipped, not deleted) when Windows has no WIC decoder for a file extension. The
/// product never needs one for WebP (it decodes WebP with SkiaSharp), but a test that checks WIC against SkiaSharp does:
/// <code>[SkipIfNoWicDecoder(".webp")] public Task The_webp_fixture_decodes_the_same_in_wic_and_skia() { ... }</code>
/// The WebP decoder comes with the free WebP Image Extension, which Windows Server (the CI runner) does not have.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipIfNoWicDecoderAttribute : FactAttribute
{
    public SkipIfNoWicDecoderAttribute(string extension)
    {
        if (!PixDecode.WicDecodes(extension)) Skip = $"No WIC decoder for {extension} on this PC";
    }
}
