using System.Buffers.Binary;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace MikeShift.Core.Tests.Pdf;

internal static class PdfTestHelpers
{
    /// <summary>Opens an (unencrypted) PDF for reading with PDFsharp.</summary>
    public static PdfDocument Open(string path) => PdfReader.Open(path, PdfDocumentOpenMode.Import);

    public static int PageCount(string path)
    {
        using var doc = Open(path);
        return doc.PageCount;
    }

    /// <summary>Page size in points (as stored; rotation not applied).</summary>
    public static (double Width, double Height) PageSize(string path, int index = 0)
    {
        using var doc = Open(path);
        return (doc.Pages[index].Width.Point, doc.Pages[index].Height.Point);
    }

    /// <summary>Width/height from a PNG's IHDR.</summary>
    public static (int Width, int Height) PngSize(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.True(b.Length > 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G', path + " is not a PNG");
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)));
    }

    public static bool IsJpeg(string path)
    {
        var b = File.ReadAllBytes(path);
        return b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
    }

    /// <summary>The page size images get: A4 in metric regions, Letter elsewhere (portrait).</summary>
    public static (double Width, double Height) ExpectedPortraitPage() =>
        System.Globalization.RegionInfo.CurrentRegion.IsMetric ? (595.276, 841.89) : (612, 792);
}
