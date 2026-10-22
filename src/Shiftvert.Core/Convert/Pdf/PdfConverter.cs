using Shiftvert.Core.Actions;

namespace Shiftvert.Core.Pdf;

/// <summary>Raster format for PDF page export.</summary>
public enum PdfPageFormat
{
    Png,
    Jpg,
}

/// <summary>
/// PDF conversions (PLAN §1.2 PDF rows and image-to-PDF, §3.4 PDF). OWNED BY W2b (PDF): implement the bodies; you may
/// change signatures inside this file (and the matching lambdas in <c>Actions/Catalog/PdfActions.cs</c> and the
/// "PDF"/"Merge PDF" lambdas in <c>Actions/Catalog/ImageActions.cs</c>, the one sanctioned cross-package touch point).
/// <para>Every method returns the path it wrote (a folder for pages/split) via <c>Naming.CommitAsync</c>, throws
/// <see cref="ConversionException"/> with a user-facing message for expected failures, and never modifies inputs.</para>
/// </summary>
public static class PdfConverter
{
    /// <summary>Image to single- or multi-page PDF (each TIFF frame is a page); output <c>name.pdf</c>.</summary>
    public static Task<string> ImageToPdfAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>Several images into one <c>Merged.pdf</c> beside the first image (one page per image / TIFF frame).</summary>
    public static Task<string> MergeImagesAsync(IReadOnlyList<string> inputs, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>Renders pages to PNG/JPG. One page: <c>name.png</c>; several: folder <c>name pages\name 01.png...</c>. Returns the file or folder.</summary>
    public static Task<string> ToImagesAsync(string input, PdfPageFormat format, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>Extracts text to <c>name.txt</c> (refuses scans and reversed Hebrew).</summary>
    public static Task<string> ToTextAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>Re-saves with image recompression, " compressed"; keep only if smaller; refuses encrypted files.</summary>
    public static Task<string> CompressAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>One PDF per page in folder <c>name split\</c>. Returns the folder.</summary>
    public static Task<string> SplitAsync(string input, CancellationToken ct) =>
        throw new NotImplementedException("W2b");

    /// <summary>Several PDFs into <c>Merged.pdf</c> beside the first file.</summary>
    public static Task<string> MergeAsync(IReadOnlyList<string> inputs, CancellationToken ct) =>
        throw new NotImplementedException("W2b");
}
