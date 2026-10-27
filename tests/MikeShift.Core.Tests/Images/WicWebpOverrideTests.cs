using MikeShift.Core;
using MikeShift.Core.Images;
using MikeShift.Core.Pdf;
using static MikeShift.Core.Tests.Pdf.PdfTestHelpers;

namespace MikeShift.Core.Tests.Images;

[CollectionDefinition("no-wic-webp", DisableParallelization = true)]
public class NoWicWebpCollection;

/// <summary>The <c>no-wic-webp</c> dev switch: Windows' WebP decoder is hidden, so every WebP decode takes the SkiaSharp route.</summary>
[Collection("no-wic-webp")]
public sealed class WicWebpOverrideTests : IDisposable
{
    public void Dispose() => CapabilityOverrides.Apply(null);

    [Fact]
    public void The_switch_hides_the_wic_decoder_for_webp_only()
    {
        using var tmp = new TempDir();
        var webp = tmp.WriteFile("a.webp", Fixtures.Webp(16, 16));
        var png = tmp.WriteFile("a.png", Fixtures.PdfSourcePng(16, 16));

        Assert.False(CapabilityOverrides.HidesWicDecoder(webp));

        CapabilityOverrides.Apply("no-heif, NO-WIC-WEBP");

        Assert.True(CapabilityOverrides.HidesWicDecoder(webp));
        Assert.False(CapabilityOverrides.HidesWicDecoder(png));
    }

    [Fact]
    public void Without_the_switch_text_the_decoder_stays_visible()
    {
        using var tmp = new TempDir();
        var webp = tmp.WriteFile("a.webp", Fixtures.Webp(16, 16));

        CapabilityOverrides.Apply("no-heif");
        Assert.False(CapabilityOverrides.HidesWicDecoder(webp));

        CapabilityOverrides.Apply("no-wic-webp");
        CapabilityOverrides.Apply(null); // what the app passes when dev switches are not allowed
        Assert.False(CapabilityOverrides.HidesWicDecoder(webp));
    }

    [Fact]
    public async Task A_webp_still_converts_to_png_without_a_wic_decoder()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.webp", Fixtures.Webp(64, 48));
        using var guard = OriginalsGuard.Watch(input);
        CapabilityOverrides.Apply("no-wic-webp");

        var output = await ImageConverter.ToFormatAsync(input, ImageFormat.Png, default);

        Assert.Equal(tmp.Combine("a.png"), output);
        Assert.Equal(ImageContainer.Png, ImageCodec.SniffFile(output));
        Assert.True(ImageCodec.CanDecodeHeader(input));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task A_webp_still_becomes_a_pdf_page_without_a_wic_decoder()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.webp", Fixtures.Webp(64, 48));
        using var guard = OriginalsGuard.Watch(input);
        CapabilityOverrides.Apply("no-wic-webp");

        var output = await PdfConverter.ImageToPdfAsync(input, default);

        Assert.Equal(tmp.Combine("a.pdf"), output);
        Assert.Equal(1, PageCount(output));
        guard.AssertUnchanged();
    }

    [Fact]
    public async Task A_webp_preview_still_decodes_without_a_wic_decoder()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("a.webp", Fixtures.Webp(64, 48));
        CapabilityOverrides.Apply("no-wic-webp");

        var px = await ImagePreview.DecodeAsync(path, 1600);

        Assert.Equal((64, 48), (px.Width, px.Height));
    }
}
