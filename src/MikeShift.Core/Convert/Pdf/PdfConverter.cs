using System.Globalization;
using System.Text;
using MikeShift.Core.Feedback;
using MikeShift.Core.Images;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MikeShift.Core.Pdf;

/// <summary>Raster format for PDF page export.</summary>
public enum PdfPageFormat
{
    Png,
    Jpg,
}

/// <summary>
/// PDF conversions.
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
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync([input], temp, searchable: false, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>
    /// Image to PDF with the text in it read by text recognition and laid over the picture, invisibly (<see cref="PdfSearchable"/>); output
    /// <c>name searchable.pdf</c>. A picture in which nothing readable was found is refused and writes no file.
    /// </summary>
    public static async Task<string> ImageToSearchablePdfAsync(string input, CancellationToken ct)
    {
        var spec = Naming.Plan(input, ".pdf", " searchable");
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync([input], temp, searchable: true, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>As <see cref="MergeImagesAsync"/> (same order and page sizes), with every page searchable: <c>Merged searchable.pdf</c> beside the first image.</summary>
    public static async Task<string> MergeImagesSearchableAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        RequireInputs(inputs);
        var spec = Naming.PlanNamed(inputs[0], "Merged searchable", ".pdf");
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync(inputs, temp, searchable: true, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>
    /// Adds a text layer to the pages of a scanned PDF (<c>name searchable.pdf</c>): the pages without text are rendered, read by text
    /// recognition, and the words are laid over the original page, which is kept as it is. Pages that already have text are left alone. A PDF
    /// with text on every page, or one in which nothing readable is found, is refused. Encrypted PDFs are refused.
    /// </summary>
    public static async Task<string> MakeSearchableAsync(string input, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        PdfGuard.RequireNotEncrypted(bytes);
        var pages = await Task.Run(() => PdfText.ExtractPages(bytes, ct), ct).ConfigureAwait(false);
        var scanned = Enumerable.Range(0, pages.Count).Where(i => pages[i].Length == 0).ToList();
        if (scanned.Count == 0) throw new ConversionException(OcrCopy.AlreadySearchable);

        var read = await PdfOcr.ReadPagesAsync(bytes, scanned, ct).ConfigureAwait(false);
        if (read.Values.All(r => r.Lines.Count == 0)) throw new ConversionException(OcrCopy.NoTextInPdf);

        var spec = Naming.Plan(input, ".pdf", " searchable");
        var result = await Naming.CommitAsync(spec, (temp, token) => Task.Run(() =>
        {
            using var doc = PdfGuard.OpenModify(bytes);
            var added = 0;
            foreach (var (index, text) in read)
            {
                token.ThrowIfCancellationRequested();
                var page = doc.Pages[index];
                var rotation = ((page.Rotate % 360) + 360) % 360;
                var visible = PdfSearchable.VisibleRegion(page);
                var sideways = rotation is 90 or 270;
                var area = new XRect(0, 0, sideways ? visible.Height : visible.Width, sideways ? visible.Width : visible.Height);
                added += PdfSearchable.AddInvisibleText(page, text, area, rotation, visible);
            }
            if (added == 0) throw new ConversionException(OcrCopy.NoTextInPdf);
            doc.Save(temp);
        }, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    /// <summary>Several images into one <c>Merged.pdf</c> beside the first image (one page per image / TIFF frame).</summary>
    public static async Task<string> MergeImagesAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        RequireInputs(inputs);
        var spec = MergedSpec(inputs[0]);
        var result = await Naming.CommitAsync(spec, (temp, token) => WriteImagePdfAsync(inputs, temp, searchable: false, token), ct).ConfigureAwait(false);
        return result.Path;
    }

    private static async Task WriteImagePdfAsync(IReadOnlyList<string> inputs, string tempPath, bool searchable, CancellationToken ct)
    {
        var portrait = DefaultPageSize();
        using var doc = new PdfDocument();
        doc.Info.Creator = "MikeShift";

        var sources = new List<MemoryStream>();
        var words = 0;
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
                    var area = new XRect((pageW - w) / 2, (pageH - h) / 2, w, h);
                    using (var gfx = XGraphics.FromPdfPage(page))
                    using (var xi = XImage.FromStream(ms))
                        gfx.DrawImage(xi, area.X, area.Y, area.Width, area.Height);

                    if (searchable)
                    {
                        // Read the very pixels that were placed, so the word boxes map onto the picture's rectangle.
                        var decoded = await PdfPixels.DecodeBytesAsync(img.Jpeg, channels: 4, maxLongSide: 0, colorManage: false).ConfigureAwait(false);
                        var text = await TextRecognition.RecognizeAsync(new Raster(decoded.Width, decoded.Height, decoded.Data), ct).ConfigureAwait(false);
                        words += PdfSearchable.AddInvisibleText(page, text, area);
                    }
                }
            }

            if (doc.PageCount == 0) throw new ConversionException("There were no images to put into a PDF.");
            if (searchable && words == 0) throw new ConversionException(inputs.Count == 1 ? OcrCopy.NoTextInPicture : OcrCopy.NoTextInPictures);
            doc.Save(tempPath);
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
    }

    // ------------------------------------------------------------------ PDF -> images

    /// <summary>Renders pages to PNG/JPG. One page: <c>name.png</c>; several: folder <c>name pages\name 01.png...</c>. Returns the file or folder.</summary>
    public static Task<string> ToImagesAsync(string input, PdfPageFormat format, CancellationToken ct) =>
        ToImagesAsync(input, format, ConversionOptions.Default, ct);

    /// <summary>As above; JPG pages are encoded at <paramref name="options"/>'s <see cref="ConversionOptions.Quality"/>.</summary>
    public static async Task<string> ToImagesAsync(string input, PdfPageFormat format, ConversionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var quality = options.QualityFraction;
        var bytes = PdfGuard.ReadBytes(input);
        PdfGuard.RequireNotEncrypted(bytes);
        var doc = await PdfRender.OpenAsync(bytes).ConfigureAwait(false);
        var ext = format == PdfPageFormat.Png ? ".png" : ".jpg";
        var count = (int)doc.PageCount;

        if (count == 1)
        {
            var spec = Naming.Plan(input, ext);
            var single = await Naming.CommitAsync(spec, async (temp, token) =>
            {
                var data = await PdfRender.RenderPageAsync(doc, 0, format, quality).ConfigureAwait(false);
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
                var data = await PdfRender.RenderPageAsync(doc, (uint)i, format, quality).ConfigureAwait(false);
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

    /// <summary>Extracts text to <c>name.txt</c>, reading scanned pages when this PC has a text recognition language.</summary>
    public static Task<string> ToTextAsync(string input, CancellationToken ct) =>
        ToTextAsync(input, TextRecognition.IsAvailable(), ct);

    /// <summary>
    /// Extracts text to <c>name.txt</c>. Pages with a text layer are read by PdfPig; when <paramref name="readScans"/> (<see cref="Capabilities.Ocr"/>),
    /// pages without one are rendered and read by text recognition instead of being left out. Refuses a PDF with no text at all (no empty file
    /// is written) and reversed Hebrew.
    /// </summary>
    public static async Task<string> ToTextAsync(string input, bool readScans, CancellationToken ct)
    {
        var bytes = PdfGuard.ReadBytes(input);
        PdfGuard.RequireNotEncrypted(bytes);
        var pages = await Task.Run(() => PdfText.ExtractPages(bytes, ct), ct).ConfigureAwait(false);

        var scanned = Enumerable.Range(0, pages.Count).Where(i => pages[i].Length == 0).ToList();
        if (readScans && scanned.Count > 0)
        {
            var read = await PdfOcr.ReadPagesAsync(bytes, scanned, ct).ConfigureAwait(false);
            foreach (var (index, recognized) in read) pages[index] = recognized.PlainText.Trim();
        }

        var text = PdfText.Join(pages);
        if (string.IsNullOrWhiteSpace(text)) throw new ConversionException(readScans ? OcrCopy.NoTextInPdf : OcrCopy.ScanWithoutOcr);
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
                single.Info.Creator = "MikeShift";
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
                merged.Info.Creator = "MikeShift";
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

    private static OutputSpec MergedSpec(string first) => Naming.PlanNamed(first, "Merged", ".pdf");

    private static void RequireInputs(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) throw new ConversionException("Nothing was selected.");
    }
}
