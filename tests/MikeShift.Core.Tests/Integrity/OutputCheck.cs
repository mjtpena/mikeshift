using System.IO.Compression;
using System.Text;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// A format-agnostic "is this output a valid file of the kind its extension says" check, built on the same independent readers as the
/// per-category tests. Used by the command-line tests, which run the real <c>mikeshift.exe</c> and only see output paths.
/// </summary>
internal static class OutputCheck
{
    /// <summary>Validates a file or a folder of files; returns a short description of what was found.</summary>
    public static async Task<string> ValidateAsync(string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(files);
            foreach (var f in files) await ValidateAsync(f);
            return $"folder of {files.Length} valid files";
        }

        Assert.True(File.Exists(path), "output does not exist: " + path);
        Assert.True(new FileInfo(path).Length > 0, "output is empty: " + path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        switch (ext)
        {
            case "png" or "jpg" or "jpeg" or "bmp" or "tif" or "tiff" or "webp" or "heic":
            {
                var wic = await PixDecode.WicAsync(path);
                Assert.True(wic.W > 0 && wic.H > 0);
                return $"{wic.W}x{wic.H} image";
            }
            case "gif":
            {
                var info = await PixDecode.WicInfoAsync(path);
                Assert.True(info.Width > 0);
                if (info.Frames > 1) return $"{info.Width}x{info.Height} GIF, {info.Frames} frames";
                var skia = PixDecode.Skia(path);
                Assert.NotNull(skia);
                return $"{info.Width}x{info.Height} GIF";
            }
            case "pdf":
            {
                var pages = PdfCheck.Read(path);
                Assert.NotEmpty(pages);
                var rendered = await PdfCheck.RenderAllAsync(path, 300);
                Assert.Equal(pages.Count, rendered.Count);
                return $"{pages.Count}-page PDF";
            }
            case "docx":
                Assert.Empty(DocCheck.ValidateDocx(path));
                Assert.NotEmpty(DocCheck.DocxOutline(path));
                return "valid DOCX";
            case "odt":
                DocCheck.AssertOdtPackage(path);
                Assert.NotEmpty(DocCheck.OdtOutline(path));
                return "valid ODT";
            case "html":
                Assert.NotEmpty(DocCheck.HtmlOutline(File.ReadAllText(path, Encoding.UTF8)));
                return "HTML";
            case "rtf":
                Assert.NotEmpty(DocCheck.RtfOutline(File.ReadAllText(path, Encoding.GetEncoding(1252))));
                return "RTF";
            case "txt":
                _ = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
                return "UTF-8 text";
            case "wav":
            {
                AudioCheck.AssertWavHeaderConsistent(path);
                var p = AudioCheck.ReadWav(path);
                return $"WAV {p.Seconds:0.00} s";
            }
            case "aif" or "aiff":
            {
                var p = AudioCheck.ReadAiff(path);
                return $"AIFF {p.Seconds:0.00} s";
            }
            case "flac":
            {
                var f = AudioCheck.ReadFlac(path);
                return $"FLAC {(double)f.TotalSamples / f.Rate:0.00} s";
            }
            case "mp3":
                return $"MP3 {AudioCheck.ReadMp3(path).Seconds:0.00} s";
            case "m4a" or "mp4" or "m4v":
            {
                var m = AudioCheck.ReadMp4(path);
                Assert.NotEmpty(m.Tracks);
                return $"MP4 container {m.MovieSeconds:0.00} s";
            }
            case "wmv":
                Assert.True(VideoCheck.IsAsf(path), "not ASF");
                return "ASF";
            case "zip":
            {
                using var z = ZipFile.OpenRead(path);
                foreach (var e in z.Entries.Where(e => !e.FullName.EndsWith('/')))
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    Assert.Equal(e.Length, ms.Length);
                }
                return $"zip, {z.Entries.Count} entries";
            }
            default:
                throw new Xunit.Sdk.XunitException("no validator for ." + ext);
        }
    }
}
