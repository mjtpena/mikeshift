namespace MikeShift.Core.Actions;

/// <summary>
/// Segoe Fluent Icons code points for the wheel's tiles (one-character strings; font family "Segoe Fluent Icons",
/// fallback "Segoe MDL2 Assets" on Windows 10). A unit test checks each exists in the installed icon font.
/// <para>
/// Metaphors (UX review W-07): Resize 50 % is the inward-pointing arrows (it shrinks), Compress is an archive box (a different idea from
/// shrinking the pixel size), Strip EXIF is the crossed-out eye (hide the metadata), never an X (which reads as "close").
/// Format tiles carry the glyph of their output's category (W-06).
/// </para>
/// </summary>
public static class Glyphs
{
    // Tools
    public const string Compress = "";   // Archive (a box)
    public const string Resize = "";     // BackToWindow (arrows pointing inward)
    public const string Crop = "";       // Crop
    public const string StripExif = "";  // Hide (an eye with a slash)
    public const string Trim = "";       // Cut
    public const string Split = "";      // Split page
    public const string Merge = "";      // Combine
    public const string Zip = "";        // ZipFolder
    public const string MergePdf = Merge;

    // Format categories
    public const string Image = "";      // Photo2
    public const string Document = "";   // Page
    public const string Pdf = "";        // PDF
    public const string Video = "";      // Video
    public const string Audio = "";      // MusicNote

    /// <summary>The Info glyph (caution pill, hub of a virtual-file drag).</summary>
    public const string Info = "";

    /// <summary>Every tool glyph, for tests.</summary>
    public static IReadOnlyList<string> All { get; } = [Compress, Resize, Crop, StripExif, Trim, Split, Merge, Zip];

    /// <summary>Every glyph the wheel can draw (tool, category and info glyphs), for the font-coverage test.</summary>
    public static IReadOnlyList<string> Everything { get; } = [.. All, Image, Document, Pdf, Video, Audio, Info];

    /// <summary>
    /// The category glyph of a format tile, by the format it produces: pictures (PNG, JPG, HEIC, TIFF, GIF, BMP, WebP) get the photo glyph,
    /// "PDF" the PDF glyph, text documents (DOCX, RTF, TXT, HTML, ODT) the page glyph, MP4/M4V/WMV the video glyph, MP3/M4A/WAV/FLAC/AIFF the note.
    /// Unknown titles get the page glyph.
    /// </summary>
    public static string ForFormat(string title) => title.ToUpperInvariant() switch
    {
        "PNG" or "JPG" or "JPEG" or "TIFF" or "TIF" or "GIF" or "BMP" or "HEIC" or "WEBP" => Image,
        "PDF" => Pdf,
        "MP4" or "M4V" or "WMV" or "MOV" or "WEBM" => Video,
        "MP3" or "M4A" or "WAV" or "FLAC" or "AIFF" or "AAC" => Audio,
        _ => Document,
    };
}
