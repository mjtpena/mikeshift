using MikeShift.Core.Images;

namespace MikeShift.Core.Pdf;

/// <summary>
/// Reads the text of PDF pages that have no text layer (scans): each page is rendered with <see cref="PdfRender"/> at
/// <see cref="PdfRender.TextScale"/> and read by <see cref="TextRecognition"/>. The results keep the word boxes, in pixels of the rendered page
/// (<see cref="RecognizedText.Width"/> x <see cref="RecognizedText.Height"/> covers the whole page), so they can be placed back onto the page.
/// </summary>
internal static class PdfOcr
{
    /// <summary>Reads the pages at <paramref name="pageIndexes"/> (zero-based), in order. A page Windows cannot render fails the whole job.</summary>
    public static async Task<IReadOnlyDictionary<int, RecognizedText>> ReadPagesAsync(byte[] pdf, IReadOnlyList<int> pageIndexes, CancellationToken ct)
    {
        var doc = await PdfRender.OpenAsync(pdf).ConfigureAwait(false);
        var results = new Dictionary<int, RecognizedText>();
        foreach (var index in pageIndexes)
        {
            ct.ThrowIfCancellationRequested();
            if (index < 0 || index >= doc.PageCount) continue;
            var pixels = await PdfRender.RenderPageForTextAsync(doc, (uint)index).ConfigureAwait(false);
            results[index] = await TextRecognition.RecognizeAsync(pixels, ct).ConfigureAwait(false);
        }
        return results;
    }
}
