using Shiftvert.Core;

namespace Shiftvert.Core.Tests;

public class KindTests
{
    public static TheoryData<string> ImageExt() => [.. Kind.ImageExtensions.Order()];
    public static TheoryData<string> VideoExt() => [.. Kind.VideoExtensions.Where(e => e is not ("ts" or "mts" or "m2ts")).Order()];
    public static TheoryData<string> AudioExt() => [.. Kind.AudioExtensions.Order()];
    public static TheoryData<string> DocExt() => [.. Kind.DocumentExtensions.Order()];

    [Theory, MemberData(nameof(ImageExt))]
    public void Image_extensions_are_images(string ext) => Assert.Equal(Category.Image, OfFile($"x.{ext}", Fixtures.Noise(64)));

    // Audio/video are only classified as such when Media Foundation can open the file (real probes since W2c), so
    // these extension-table tests use the placeholder path (extension verdict only). Real files: Media/MediaProbeTests.
    [Theory, MemberData(nameof(VideoExt))]
    public void Video_extensions_are_video(string ext) => Assert.Equal(Category.Video, ExtensionVerdict($"x.{ext}"));

    [Theory, MemberData(nameof(AudioExt))]
    public void Audio_extensions_are_audio(string ext) => Assert.Equal(Category.Audio, ExtensionVerdict($"x.{ext}"));

    [Theory, MemberData(nameof(DocExt))]
    public void Document_extensions_are_documents(string ext) => Assert.Equal(Category.Document, OfFile($"x.{ext}", Fixtures.Noise(64)));

    [Fact]
    public void Matrix_extensions_from_the_plan_are_all_covered()
    {
        foreach (var e in new[] { "png", "jpg", "jpeg", "jfif", "heic", "tiff", "tif", "gif", "webp", "bmp", "avif", "ico" })
            Assert.Contains(e, Kind.ImageExtensions);
        foreach (var e in new[] { "mp4", "mov", "m4v", "avi", "wmv", "mkv", "webm" })
            Assert.Contains(e, Kind.VideoExtensions);
        foreach (var e in new[] { "mp3", "wav", "m4a", "aac", "flac", "wma", "alac", "aiff", "aif" })
            Assert.Contains(e, Kind.AudioExtensions);
        foreach (var e in new[] { "docx", "doc", "rtf", "odt", "html", "txt", "md" })
            Assert.Contains(e, Kind.DocumentExtensions);
    }

    [Fact]
    public void Extensions_are_case_insensitive()
    {
        Assert.Equal(Category.Image, OfFile("SHOUT.PNG", Fixtures.Png()));
        Assert.Equal(Category.Document, OfFile("Notes.TXT", "hi"u8.ToArray()));
    }

    [Fact]
    public void Real_png_is_an_image_and_a_pdf_is_a_pdf()
    {
        Assert.Equal(Category.Image, OfFile("a.png", Fixtures.Png()));
        Assert.Equal(Category.Pdf, OfFile("a.pdf", Fixtures.PdfStub()));
    }

    [Fact]
    public void Directories_are_other_even_with_a_media_looking_name()
    {
        using var tmp = new TempDir();
        Assert.Equal(Category.Other, Kind.Of(tmp.CreateDir("holiday.mp4")));
        Assert.Equal(Category.Other, Kind.Of(tmp.Path));
    }

    [Fact]
    public void Unknown_missing_and_empty_extension_are_other()
    {
        Assert.Equal(Category.Other, OfFile("x.xyz", Fixtures.Noise(10)));
        Assert.Equal(Category.Other, OfFile("noextension", Fixtures.Noise(10)));
        using var tmp = new TempDir();
        Assert.Equal(Category.Other, Kind.Of(tmp.Combine("does-not-exist.png")));
    }

    [Fact]
    public void Ts_with_transport_stream_sync_bytes_is_video()
    {
        // Sync bytes are necessary but, since W2c, no longer sufficient: Media Foundation must also be able to open the
        // stream, and the synthetic skeleton has no programs. The verdict therefore follows the real probe.
        using var tmp = new TempDir();
        foreach (var name in new[] { "clip.ts", "clip.mts" })
        {
            var path = tmp.WriteFile(name, Fixtures.MpegTs(8));
            var expected = Shiftvert.Core.Media.MediaProbe.CanOpen(path) ? Category.Video : Category.Other;
            Assert.Equal(expected, Kind.Of(path));
        }
    }

    [Fact]
    public void Ts_that_is_typescript_source_is_other()
    {
        var source = "export const answer: number = 42;\nconsole.log(answer);\n".PadRight(600, ' ');
        Assert.Equal(Category.Other, OfFile("app.ts", System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Equal(Category.Other, OfFile("app.mts", System.Text.Encoding.UTF8.GetBytes(source)));
    }

    [Fact]
    public void Ts_needs_sync_bytes_at_every_188_bytes_not_just_at_the_start()
    {
        var data = Fixtures.MpegTs(8);
        data[188 * 2] = 0x00; // broken third packet
        Assert.Equal(Category.Other, OfFile("broken.ts", data));

        Assert.Equal(Category.Other, OfFile("tiny.ts", [0x47]));
        Assert.Equal(Category.Other, OfFile("empty.ts", []));
    }

    [Fact]
    public void Pdf_is_detected_by_magic_even_when_misnamed()
    {
        Assert.Equal(Category.Pdf, OfFile("scan.dat", Fixtures.PdfStub()));
        Assert.Equal(Category.Pdf, OfFile("report.jpg", Fixtures.PdfStub()));
        Assert.Equal(Category.Pdf, OfFile("noextension", Fixtures.PdfStub()));
    }

    [Fact]
    public void A_pdf_extension_without_the_magic_is_not_a_pdf()
    {
        Assert.Equal(Category.Other, OfFile("fake.pdf", "not a pdf at all"u8.ToArray()));
        Assert.Equal(Category.Other, OfFile("empty.pdf", []));
    }

    [Fact]
    public void Png_bytes_named_jpg_is_still_an_image_decided_by_the_decoder_probe()
    {
        Assert.Equal(Category.Image, OfFile("really-a-png.jpg", Fixtures.Png()));
    }

    [Fact]
    public void Cloud_placeholders_keep_the_extension_verdict_without_reading_content()
    {
        using var tmp = new TempDir();
        // A ".pdf" without the PDF magic is Other when the content can be read...
        var fakePdf = tmp.WriteFile("remote.pdf", "online only");
        Assert.Equal(Category.Other, Kind.Classify(fakePdf, FileAttributes.Normal));
        // ...but a recall-on-data-access placeholder is judged by extension alone.
        Assert.Equal(Category.Pdf, Kind.Classify(fakePdf, Kind.RecallOnDataAccess));
        Assert.Equal(Category.Pdf, Kind.Classify(fakePdf, Kind.RecallOnOpen));

        var cloud = tmp.WriteFile("cloud.mp4", "x");
        Assert.Equal(Category.Video, Kind.Classify(cloud, Kind.RecallOnDataAccess | FileAttributes.Archive));

        // Ambiguous "ts" cannot be verified without hydrating: stays Other.
        var ts = tmp.WriteFile("remote.ts", "x");
        Assert.Equal(Category.Other, Kind.Classify(ts, Kind.RecallOnDataAccess));
        Assert.Equal(Category.Video, Kind.Classify(tmp.WriteFile("remote.m2ts", "x"), Kind.RecallOnDataAccess));
    }

    [Fact]
    public void Placeholder_attribute_constant_matches_win32()
    {
        Assert.Equal(0x400000, (int)Kind.RecallOnDataAccess);
    }

    [Fact]
    public void Verdicts_are_cached_per_path_and_invalidated_when_the_file_changes()
    {
        using var tmp = new TempDir();
        var p = tmp.WriteFile("flip.dat", Fixtures.Noise(32));
        Assert.Equal(Category.Other, Kind.Of(p));
        Assert.Equal(Category.Other, Kind.Of(p)); // cached

        File.WriteAllBytes(p, Fixtures.PdfStub()); // size changes -> new key
        Assert.Equal(Category.Pdf, Kind.Of(p));

        Kind.ClearCache();
        Assert.Equal(Category.Pdf, Kind.Of(p));
    }

    [Fact]
    public void ExtensionOf_is_lowercase_without_dot()
    {
        Assert.Equal("png", Kind.ExtensionOf(@"C:\a\B.PNG"));
        Assert.Equal("gz", Kind.ExtensionOf("archive.tar.gz"));
        Assert.Equal("", Kind.ExtensionOf("README"));
        Assert.Equal("", Kind.ExtensionOf("trailingdot."));
    }

    private static Category ExtensionVerdict(string name)
    {
        using var tmp = new TempDir();
        return Kind.Classify(tmp.WriteFile(name, Fixtures.Noise(64)), Kind.RecallOnDataAccess);
    }

    private static Category OfFile(string name, byte[] content)
    {
        using var tmp = new TempDir();
        return Kind.Of(tmp.WriteFile(name, content));
    }
}
