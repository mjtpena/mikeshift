using System.Text;
using MikeShift.Core.Feedback;
using MikeShift.Core.Pdf;

namespace MikeShift.Core.Images;

/// <summary>
/// Picture to text: the TXT tile of the Image category. Reads the picture with <see cref="TextRecognition"/> and writes <c>name.txt</c>
/// (UTF-8, one line per recognised line) through <see cref="Naming.CommitAsync"/>. Only offered when <see cref="Capabilities.Ocr"/>.
/// </summary>
internal static class ImageText
{
    /// <summary>
    /// Writes the text of <paramref name="input"/> to <c>name.txt</c> and returns its path. Refuses multi-page and animated pictures (only one
    /// frame would be read), pictures with no recognisable text (no empty file is written) and Hebrew that came out reversed.
    /// </summary>
    public static async Task<string> ToTextAsync(string input, CancellationToken ct)
    {
        var (frames, animated) = await ImageCodec.FrameInfoAsync(input, ct).ConfigureAwait(false);
        if (frames > 1 || animated) throw new ConversionException(OcrCopy.MultiFrame(frames, animated));

        var img = await ImageCodec.DecodeAsync(input, ImageCodec.MaxPixels, ct).ConfigureAwait(false);
        var text = (await TextRecognition.RecognizeAsync(img.Pixels, ct).ConfigureAwait(false)).PlainText;

        if (string.IsNullOrWhiteSpace(text)) throw new ConversionException(OcrCopy.NoTextInPicture);
        if (PdfText.LooksReversedHebrew(text)) throw new ConversionException(OcrCopy.ReversedText);

        var spec = Naming.Plan(input, ".txt");
        var result = await Naming.CommitAsync(spec,
            (temp, token) => File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token), ct).ConfigureAwait(false);
        return result.Path;
    }
}
