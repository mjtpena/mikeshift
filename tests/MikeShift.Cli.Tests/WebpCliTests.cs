using MikeShift.Core.Tests;
using MikeShift.Core.Tests.Integrity;

namespace MikeShift.Cli.Tests;

/// <summary>WebP through the real exe: the outputs must be valid whether or not Windows has a WebP decoder.</summary>
public class WebpCliTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Cropping_a_webp_through_the_exe_gives_a_picture_of_the_cropped_size()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("shot.webp", Fixtures.Webp(64, 48));
        var sha = Kit.Sha256(input);

        var r = CliProcess.Run(Limit, "--run", "Crop", "--crop", "8,4,20,16", input);

        Assert.True(r.ExitCode == 0, $"exit {r.ExitCode}: {r.Stderr}");
        var output = Assert.Single(r.StdoutLines);
        await OutputCheck.ValidateAsync(output);
        var px = PixDecode.WicReads(output) ? await PixDecode.WicAsync(output) : PixDecode.Skia(output)!;
        Assert.Equal((20, 16), (px.W, px.H));
        Assert.Equal(sha, Kit.Sha256(input));
    }

    [Fact]
    public async Task Converting_a_webp_to_pdf_through_the_exe_gives_one_valid_page()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("shot.webp", Fixtures.Webp(64, 48));
        var sha = Kit.Sha256(input);

        var r = CliProcess.Run(Limit, "--run", "PDF", input);

        Assert.True(r.ExitCode == 0, $"exit {r.ExitCode}: {r.Stderr}");
        var output = Assert.Single(r.StdoutLines);
        await OutputCheck.ValidateAsync(output);
        var page = Assert.Single(PdfCheck.Read(output));
        Assert.Equal(1, page.Images);
        Assert.Equal(sha, Kit.Sha256(input));
    }
}
