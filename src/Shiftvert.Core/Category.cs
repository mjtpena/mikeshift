namespace Shiftvert.Core;

/// <summary>
/// The kind of thing a selected path is. Decides which action list the wheel shows
/// (PLAN §1.1 multi-select rules, §1.2 feature matrix).
/// </summary>
public enum Category
{
    /// <summary>A raster image the platform can decode (PNG, JPG, HEIC, TIFF, GIF, WebP, BMP, AVIF, ICO...).</summary>
    Image,
    /// <summary>A video container Media Foundation can open.</summary>
    Video,
    /// <summary>An audio file Media Foundation (or our AIFF reader) can open.</summary>
    Audio,
    /// <summary>A PDF, detected by the <c>%PDF</c> magic number (also when misnamed).</summary>
    Pdf,
    /// <summary>A text-like document: DOCX, DOC, RTF, ODT, HTML, TXT, MD.</summary>
    Document,
    /// <summary>Anything else, including every directory. Only Zip is offered.</summary>
    Other,
}
