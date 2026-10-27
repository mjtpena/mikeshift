namespace MikeShift.Core.Feedback;

/// <summary>
/// The words of reading text from pictures and scanned PDFs (the picture TXT tile and the scan fallback of PDF TXT), in one place so tests can
/// check them. Text recognition is the Windows OCR engine, which reads the languages Windows has an OCR pack for; the Settings row and the scan
/// message say how to add one.
/// </summary>
public static class OcrCopy
{
    /// <summary>The Settings → Format support row.</summary>
    public const string FormatSupportName = "Text from pictures and scanned PDFs";

    /// <summary>How to get text recognition when the row says "Not installed".</summary>
    public const string FormatSupportHint =
        "Add a language in Settings > Time & language > Language & region, then make sure its language options include Optical character recognition.";

    /// <summary>A picture in which nothing readable was recognised.</summary>
    public const string NoTextInPicture = "Couldn't find any text in this picture.";

    /// <summary>Several pictures merged into a searchable PDF, none of which had readable text.</summary>
    public const string NoTextInPictures = "Couldn't find any text in these pictures.";

    /// <summary>Searchable PDF on a PDF that already has text on every page: there is nothing to add.</summary>
    public const string AlreadySearchable = "This PDF is already searchable: every page has text you can select and search.";

    /// <summary>A PDF without a text layer in which nothing readable was recognised either.</summary>
    public const string NoTextInPdf = "Couldn't find any text in this PDF, even on its scanned pages.";

    /// <summary>A PDF without a text layer on a PC that has no OCR language, so its pages could not be read as pictures.</summary>
    public const string ScanWithoutOcr =
        "No text found — this PDF is probably a scan. To read scans, add a language with Optical character recognition in Windows Settings.";

    /// <summary>The picture tile was used on a PC whose OCR languages were removed after MikeShift started.</summary>
    public const string NoLanguage =
        "Couldn't read text because Windows has no text recognition language. Add a language with Optical character recognition in Windows Settings.";

    /// <summary>Recognition itself went wrong (the engine threw), not "there was no text".</summary>
    public const string Unreadable = "Couldn't read the text in this picture.";

    /// <summary>A PDF page that could not be read as a picture.</summary>
    public static string UnreadablePage(int number) => $"Couldn't read the text on page {number}.";

    /// <summary>Hebrew that came out in reverse, refused like PDF text stored in reverse rather than written backwards.</summary>
    public const string ReversedText = "The Hebrew in this picture was read in reverse, so it can't be extracted reliably.";

    /// <summary>A multi-page TIFF or an animated picture: only single pictures are read as text.</summary>
    public static string MultiFrame(int frames, bool animated) => animated
        ? "Text can't be read from an animated picture."
        : $"This file has {frames} pages. Convert it to PDF first, then use TXT on the PDF to read every page.";
}
