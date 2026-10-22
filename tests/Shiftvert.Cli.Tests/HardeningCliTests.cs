namespace Shiftvert.Cli.Tests;

/// <summary>
/// PLAN Phase 6 through the real executable: truncated, misnamed and empty inputs must end in a clean exit code
/// (0 or 1, never a crash code), an <c>error:</c> line on failure, no hang, no stray file next to the original, and an untouched original.
/// The exhaustive matrix (every offered action on every corrupt input) is <c>Hardening.HardeningTests</c> in Core.Tests, in-process.
/// </summary>
public class HardeningCliTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public static TheoryData<string, string, string> Cases()
    {
        var png = Convert.ToBase64String(Fixtures.PngRgb(48, 32));
        var jpg = Convert.ToBase64String(Fixtures.QuadrantJpeg(64, 48));
        var pdf = Convert.ToBase64String(Fixtures.PdfText(2));
        var docx = Convert.ToBase64String(Fixtures.DocxSample());
        string Half(string b64) { var b = Convert.FromBase64String(b64); return Convert.ToBase64String(b[..(b.Length / 2)]); }
        string Noise() => Convert.ToBase64String(Fixtures.Noise(4096, 5));

        return new TheoryData<string, string, string>
        {
            { "truncated.png", Half(png), "JPG" },
            { "truncated.jpg", Half(jpg), "PNG" },
            { "truncated.jpg", Half(jpg), "Compress" },
            { "truncated.pdf", Half(pdf), "PNG" },
            { "truncated.pdf", Half(pdf), "TXT" },
            { "truncated.pdf", Half(pdf), "Split" },
            { "truncated.docx", Half(docx), "TXT" },
            { "truncated.docx", Half(docx), "PDF" },
            { "png-bytes.pdf", png, "TXT" },
            { "pdf-bytes.png", pdf, "JPG" },
            { "random.docx", Noise(), "PDF" },
            { "random.docx", Noise(), "RTF" },
            { "random.png", Noise(), "JPG" },
            { "random.mp4", Noise(), "MP3" },
            { "empty.png", "", "JPG" },
            { "empty.pdf", "", "PNG" },
            { "empty.docx", "", "TXT" },
            { "empty.mp4", "", "GIF" },
        };
    }

    [Theory, MemberData(nameof(Cases))]
    public void Corrupt_input_fails_cleanly(string name, string base64, string action)
    {
        using var tmp = new TempDir();
        var dir = tmp.CreateDir("in");
        var path = tmp.WriteFile(Path.Combine("in", name), Convert.FromBase64String(base64));
        var before = Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order().ToArray();
        using var guard = OriginalsGuard.Watch(path);

        // Not every action is offered for every corrupt file (e.g. random.png is "Zip only"): the CLI then says so and exits 1.
        var r = CliProcess.Run(Limit, "--run", action, path);

        Assert.True(r.ExitCode is 0 or 1, $"exit code {r.ExitCode}: {r.Stderr}");
        if (r.ExitCode == 1)
        {
            Assert.Contains("error:", r.Stderr);
            Assert.DoesNotContain("   at ", r.Stderr); // no stack trace
        }

        var after = Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order().ToArray();
        var created = after.Except(before).ToArray();
        var reported = r.StdoutLines.Select(Path.GetFullPath).ToArray();
        foreach (var c in created)
            Assert.True(reported.Any(o => string.Equals(o, c, StringComparison.OrdinalIgnoreCase)
                                          || c.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                $"stray file left behind: {Path.GetFileName(c)}");
        if (r.ExitCode == 1) Assert.Empty(created);
    }

    [Fact]
    public void A_zero_byte_file_is_zip_only()
    {
        using var tmp = new TempDir();
        var r = CliProcess.Run(Limit, "--list", tmp.WriteFile("empty.png", Array.Empty<byte>()));
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("Zip", r.Stdout.Trim());
    }
}
