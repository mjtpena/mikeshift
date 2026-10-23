using System.Text;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Not a real test: <c>scripts/verify-offline.ps1</c> sets <c>MIKESHIFT_INTEGRITY_FIXTURES_OUT</c> and runs this to write the whole
/// integrity fixture set (every image, PDF, audio, video, document and folder kind) into one folder, so the offline check can run
/// the real command line over all of them. Without the variable it does nothing.
/// </summary>
public class IntegrityFixtureExport
{
    [Fact]
    public async Task Export_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("MIKESHIFT_INTEGRITY_FIXTURES_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;

        string D(string sub) { var p = Path.Combine(dir, sub); Directory.CreateDirectory(p); return p; }

        foreach (var id in ImgSource.Ids)
        {
            var src = ImgSource.Get(id);
            if (src.Make() is { } bytes) File.WriteAllBytes(Path.Combine(D("images"), src.FileName), bytes);
        }

        File.WriteAllBytes(Path.Combine(D("pdf"), "text-3-pages.pdf"), Fixtures.PdfText(3));
        File.WriteAllBytes(Path.Combine(D("pdf"), "other-2-pages.pdf"), Fixtures.PdfText(2));
        File.WriteAllBytes(Path.Combine(D("pdf"), "photos-3-pages.pdf"), Fixtures.PdfImageHeavy(3));

        foreach (var id in AudioSource.Ids)
        {
            var src = AudioSource.Get(id);
            if (src.Available) File.Copy(await src.MasterAsync(), Path.Combine(D("audio"), src.FileName), overwrite: true);
        }

        if (Kit.Caps.H264Encode && Kit.Caps.AacEncode)
        {
            var mp4 = Path.Combine(D("video"), "clip.mp4");
            await VideoCheck.WriteMp4Async(mp4);
            await VideoCheck.WriteWmvAsync(mp4, Path.Combine(D("video"), "clip.wmv"));
        }

        var docs = D("docs");
        File.WriteAllBytes(Path.Combine(docs, "report.docx"), Fixtures.DocxSample());
        File.WriteAllBytes(Path.Combine(docs, "report.rtf"), Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        File.WriteAllBytes(Path.Combine(docs, "report.odt"), Fixtures.OdtSample());
        File.WriteAllBytes(Path.Combine(docs, "report.html"), new UTF8Encoding(false).GetBytes(Fixtures.HtmlSample()));
        File.WriteAllBytes(Path.Combine(docs, "notes.txt"), Fixtures.DocTxtUtf8());
        File.WriteAllBytes(Path.Combine(docs, "notes.md"), new UTF8Encoding(false).GetBytes(Fixtures.MarkdownSample()));
        File.WriteAllBytes(Path.Combine(docs, "legacy.doc"), Encoding.ASCII.GetBytes(Fixtures.RtfSample()));

        var tree = D(Path.Combine("folder", "Project"));
        File.WriteAllText(Path.Combine(tree, "readme.txt"), string.Concat(Enumerable.Repeat("Hello MikeShift. ", 200)));
        File.WriteAllBytes(Path.Combine(tree, "random.bin"), Fixtures.Noise(100_000, 21));
        Directory.CreateDirectory(Path.Combine(tree, "empty folder"));
        Directory.CreateDirectory(Path.Combine(tree, "café"));
        File.WriteAllText(Path.Combine(tree, "café", "naïve.txt"), "x");
    }
}
