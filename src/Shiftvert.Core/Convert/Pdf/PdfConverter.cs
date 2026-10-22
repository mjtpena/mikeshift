using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Shiftvert.Core.Pdf;

/// <summary>Raster format for PDF page export.</summary>
public enum PdfPageFormat
{
    Png,
    Jpg,
}

/// <summary>
/// PDF conversions (PLAN §1.2 PDF rows and image-to-PDF, §3.4 PDF).
/// <para>Every method returns the path it wrote (a folder for pages/split) via <c>Naming.CommitAsync</c>, throws
/// <see cref="ConversionException"/> with a user-facing message for expected failures, and never modifies inputs.</para>
/// </summary>
public static class PdfConverter
{
    /// <summary>A4 in points.</summary>
    internal static readonly (double Width, double Height) A4 = (595.276, 841.89);

    /// <summary>US Letter in points.</summary>
    internal static readonly (double Width, double Height) Letter = (612, 792);

    /// <summary>The portrait page size used for images: A4 in metric regions, Letter elsewhere.</summary>
    internal static (double Width, double Height) DefaultPageSize() =>
        RegionInfo.CurrentRegion.IsMetric ? A4 : Letter;

    // ------------------------------------------------------------------ images -> PDF

    /// <summary>Image to single- or multi-page PDF (each TIFF frame is a page); output <c>name.pdf</c>.</summary>
    public static async Task<string> ImageToPdfAsync(string input, CancellationToken ct)
    {
        var spec = Naming.Plan(input, ".pdf");
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync([input], temp, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>Several images into one <c>Merged.pdf</c> beside the first image (one page per image / TIFF frame).</summary>
    public static async Task<string> MergeImagesAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        RequireInputs(inputs);
        var spec = MergedSpec(inputs[0]);
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync(inputs, temp, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    private static async Task WriteImagePdfAsync(IReadOnlyList<string> inputs, string tempPath, CancellationToken ct)
    {
        var portrait = DefaultPageSize();
        using var doc = new PdfDocument();
        doc.Info.Creator = "Shiftvert";

        var sources = new List<MemoryStream>();
        try
        {
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();
                var pages = await PdfImageSource.LoadAsync(input, ct).ConfigureAwait(false);
                foreach (var img in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    var landscape = img.Width > img.Height;
                    var pageW = landscape ? portrait.Height : portrait.Width;
                    var pageH = landscape ? portrait.Width : portrait.Height;

                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(pageW);
                    page.Height = XUnit.FromPoint(pageH);

                    // Fit inside the page, centred, aspect ratio kept.
                    var scale = Math.Min(pageW / img.Width, pageH / img.Height);
                    double w = img.Width * scale, h = img.Height * scale;

                    var ms = new MemoryStream(img.Jpeg, writable: false);
                    sources.Add(ms);
                    using var gfx = XGraphics.FromPdfPage(page);
                    using var xi = XImage.FromStream(ms);
                    gfx.DrawImage(xi, (pageW - w) / 2, (pageH - h) / 2, w, h);
                }
            }

            if (doc.PageCount == 0) throw new ConversionException("There were no images to put into a PDF.");
            doc.Save(tempPath);
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
    }

    // ------------------------------------------------------------------ PDF -> images

    /// <summary>Renders pages to PNG/JPG. One page: <c>name.png</c>; several: folder <c>name pages\name 01.png...</c>. Returns the file or folder.</summary>
    public static async Task<string> ToImagesAsync(string input, PdfPageFormat format, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        var doc = await PdfRender.OpenAsync(bytes).ConfigureAwait(false);
        var ext = format == PdfPageFormat.Png ? ".png" : ".jpg";
        var count = (int)doc.PageCount;

        if (count == 1)
        {
            var spec = Naming.Plan(input, ext);
            var single = await Naming.CommitAsync(spec, async (temp, token) =>
            {
                var data = await PdfRender.RenderPageAsync(doc, 0, format).ConfigureAwait(false);
                await File.WriteAllBytesAsync(temp, data, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            return single.Path;
        }

        var folder = Naming.Plan(input, "", " pages");
        var baseName = Path.GetFileNameWithoutExtension(input);
        var result = await Naming.CommitAsync(folder, async (tempDir, token) =>
        {
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var data = await PdfRender.RenderPageAsync(doc, (uint)i, format).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(tempDir, NumberedName(baseName, i + 1, count, ext)), data, token).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary><c>name 01.ext</c>: the number is zero-padded to the width of the page count (at least two digits).</summary>
    internal static string NumberedName(string baseName, int number, int total, string ext)
    {
        var width = Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length);
        return $"{baseName} {number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}{ext}";
    }

    // ------------------------------------------------------------------ PDF -> text

    /// <summary>Extracts text to <c>name.txt</c> (refuses scans and reversed Hebrew).</summary>
    public static async Task<string> ToTextAsync(string input, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        var text = await Task.Run(() => PdfText.Extract(bytes, ct), ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text)) throw new ConversionException(PdfText.ScanMessage);
        if (PdfText.LooksReversedHebrew(text)) throw new ConversionException(PdfText.ReversedMessage);

        var spec = Naming.Plan(input, ".txt");
        var result = await Naming.CommitAsync(spec,
            (temp, token) => File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token), ct).ConfigureAwait(false);
        return result.Path;
    }

    // ------------------------------------------------------------------ compress

    /// <summary>Re-saves with image recompression, " compressed"; keep only if smaller; refuses encrypted files.</summary>
    public static async Task<string> CompressAsync(string input, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        var spec = Naming.Plan(input, ".pdf", " compressed");

        var result = await Naming.CommitAsync(spec, async (temp, token) =>
        {
            using var doc = PdfGuard.OpenModify(bytes);
            await PdfImageCompressor.RecompressAsync(doc, token).ConfigureAwait(false);
            doc.Options.CompressContentStreams = true;
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Save(temp);

            if (new FileInfo(temp).Length >= bytes.Length)
                throw new ConversionException("This PDF is already small — compressing it wouldn't make it any smaller.");
        }, ct).ConfigureAwait(false);
        return result.Path;
    }

    // ------------------------------------------------------------------ split / merge

    /// <summary>One PDF per page in folder <c>name split\</c>. Returns the folder.</summary>
    public static async Task<string> SplitAsync(string input, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        var baseName = Path.GetFileNameWithoutExtension(input);
        var spec = Naming.Plan(input, "", " split");

        var result = await Naming.CommitAsync(spec, (tempDir, token) => Task.Run(() =>
        {
            using var source = PdfGuard.OpenImport(bytes);
            var total = source.PageCount;
            for (var i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                using var single = new PdfDocument();
                single.Info.Creator = "Shiftvert";
                single.AddPage(source.Pages[i]);
                single.Save(Path.Combine(tempDir, NumberedName(baseName, i + 1, total, ".pdf")));
            }
        }, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>Several PDFs into <c>Merged.pdf</c> beside the first file.</summary>
    public static async Task<string> MergeAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        RequireInputs(inputs);
        var spec = MergedSpec(inputs[0]);

        var result = await Naming.CommitAsync(spec, (temp, token) => Task.Run(() =>
        {
            var sources = new List<PdfDocument>();
            try
            {
                using var merged = new PdfDocument();
                merged.Info.Creator = "Shiftvert";
                foreach (var input in inputs)
                {
                    token.ThrowIfCancellationRequested();
                    PdfDocument source;
                    try
                    {
                        source = PdfGuard.OpenImport(PdfGuard.ReadBytes(input));
                    }
                    catch (ConversionException ex)
                    {
                        throw new ConversionException($"{Path.GetFileName(input)}: {ex.Message}", ex);
                    }
                    sources.Add(source);
                    for (var i = 0; i < source.PageCount; i++)
                        merged.AddPage(source.Pages[i]);
                }
                merged.Save(temp);
            }
            finally
            {
                foreach (var s in sources) s.Dispose();
            }
        }, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    // ------------------------------------------------------------------ helpers

    private static OutputSpec MergedSpec(string first)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(first)) ?? ".";
        return new OutputSpec(dir, "Merged", ".pdf");
    }

    private static void RequireInputs(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) throw new ConversionException("Nothing was selected.");
    }
}
