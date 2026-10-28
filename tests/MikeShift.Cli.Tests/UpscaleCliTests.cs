using MikeShift.Core;

namespace MikeShift.Cli.Tests;

/// <summary>
/// Upscale 2x through the real executable. This PC may not be a Copilot+ PC, so the flag is simulated with the dev switch
/// (<c>MIKESHIFT_CAPS=ai-upscale</c> / <c>no-ai-upscale</c>, honoured by a Debug CLI and by an unpackaged Release CLI started with <c>--dev</c>).
/// The real scaler is not needed: the tile's visibility, the refusal when it is hidden and the failure of corrupt input all happen before it.
/// </summary>
public class UpscaleCliTests
{
    [Fact]
    public void With_the_flag_on_the_tile_is_listed_for_a_picture_after_Blur_faces_and_with_it_off_it_is_not()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var on = CliProcess.RunWithCaps("ai-upscale", "--list", png);
        var off = CliProcess.RunWithCaps("no-ai-upscale", "--list", png);

        Assert.Equal(0, on.ExitCode);
        Assert.Equal(0, off.ExitCode);
        var onTitles = on.Stdout.Trim().Split(", ");
        var offTitles = off.Stdout.Trim().Split(", ");
        Assert.Contains("Upscale 2x", onTitles);
        Assert.DoesNotContain("Upscale 2x", offTitles);
        Assert.Equal(offTitles.Concat(["Upscale 2x"]).Order(), onTitles.Order());   // nothing else changes
        Assert.Equal("Zip", onTitles[^1]);
    }

    [Fact]
    public void The_tile_is_not_listed_for_other_kinds_of_files_even_with_the_flag_on()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(1));
        var txt = tmp.WriteFile("notes.txt", "hello"u8.ToArray());

        Assert.DoesNotContain("Upscale 2x", CliProcess.RunWithCaps("ai-upscale", "--list", pdf).Stdout);
        Assert.DoesNotContain("Upscale 2x", CliProcess.RunWithCaps("ai-upscale", "--list", txt).Stdout);
    }

    [Fact]
    public void Without_a_switch_the_list_follows_what_this_PC_can_do()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var listed = CliProcess.Run("--list", png).Stdout.Trim().Split(", ");

        Assert.Equal(Capabilities.Current.AiUpscale, listed.Contains("Upscale 2x"));
    }

    [Fact]
    public void Running_the_hidden_tile_says_it_is_unknown_for_these_files_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("pic.png", Fixtures.PngRgb());

        var r = CliProcess.RunWithCaps("no-ai-upscale", "--run", "Upscale 2x", png);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("error: unknown action \"Upscale 2x\"", r.Stderr);
        Assert.DoesNotContain("Upscale 2x,", r.Stderr.Split("Available:")[1]);
        Assert.Equal(["pic.png"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("truncated.png", 0.5)]
    [InlineData("tiny.png", 0.1)]
    [InlineData("empty.png", 0.0)]
    public void A_broken_picture_ends_in_an_error_line_and_no_stray_file(string name, double keep)
    {
        using var tmp = new TempDir();
        var good = Fixtures.PngRgb(48, 32);
        var path = tmp.WriteFile(name, good[..(int)(good.Length * keep)]);
        using var guard = OriginalsGuard.Watch(path);

        var r = CliProcess.RunWithCaps("ai-upscale", "--run", "Upscale 2x", path);

        Assert.True(r.ExitCode is 0 or 1, r.Stderr);
        if (r.ExitCode == 1) Assert.Contains("error:", r.Stderr);
        Assert.DoesNotContain("   at ", r.Stderr);
        if (r.ExitCode == 1) Assert.Equal([name], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
        guard.AssertUnchanged();
    }

    [Fact]
    public void A_misnamed_file_is_not_a_picture_so_the_tile_is_not_offered()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("pdf-bytes.png", Fixtures.PdfText(1));

        var r = CliProcess.RunWithCaps("ai-upscale", "--run", "Upscale 2x", path);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("error:", r.Stderr);
        Assert.Equal(["pdf-bytes.png"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void On_a_good_picture_the_run_either_makes_the_2x_file_or_says_plainly_why_not()
    {
        using var tmp = new TempDir();
        var png = tmp.WriteFile("logo.png", Fixtures.PngRgb(64, 48));

        var r = CliProcess.RunWithCaps("ai-upscale", "--run", "Upscale 2x", png);

        if (r.ExitCode == 0)
        {
            // A Copilot+ PC: the real scaler ran.
            Assert.Equal(tmp.Combine("logo 2x.png"), Assert.Single(r.StdoutLines));
            return;
        }
        // Any other PC: the forced flag is only a simulation, so the run reports that the scaler is not there and writes nothing.
        Assert.Equal(1, r.ExitCode);
        Assert.Matches("error: Couldn't upscale this picture", r.Stderr);
        Assert.Equal(["logo.png"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void A_file_called_dev_after_the_terminator_is_still_a_file_name()
    {
        // Stripped as a flag it would leave "--list --" and succeed with nothing to list; as a file name it is reported missing.
        var r = CliProcess.Run("--list", "--", "--dev");

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("error: not found: --dev", r.Stderr);
    }
}
