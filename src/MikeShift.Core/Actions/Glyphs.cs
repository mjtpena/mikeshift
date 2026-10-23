namespace MikeShift.Core.Actions;

/// <summary>
/// Segoe Fluent Icons code points for the tool tiles (one-character strings; font family "Segoe Fluent Icons",
/// fallback "Segoe MDL2 Assets" on Windows 10). A unit test checks each exists in the installed icon font.
/// </summary>
public static class Glyphs
{
    public const string Compress = "";   // BackToWindow (arrows pointing inward)
    public const string Resize = "";     // FullScreen
    public const string Crop = "";       // Crop
    public const string StripExif = "";  // Clear
    public const string Trim = "";       // Cut
    public const string Split = "";      // Split page
    public const string Merge = "";      // Combine
    public const string Zip = "";        // ZipFolder
    public const string MergePdf = Merge;

    /// <summary>Every glyph, for tests.</summary>
    public static IReadOnlyList<string> All { get; } = [Compress, Resize, Crop, StripExif, Trim, Split, Merge, Zip];
}
