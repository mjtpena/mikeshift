using System.Buffers.Binary;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>Byte-level facts about image files, read without any decoder: what the file REALLY is, and which JPEG segments it holds.</summary>
internal static class ImageBytes
{
    /// <summary>"png", "jpeg", "gif", "bmp", "tiff", "webp", "heic", "avif" or "unknown", from the magic numbers.</summary>
    public static string Sniff(string path)
    {
        using var fs = File.OpenRead(path);
        var h = new byte[32];
        var n = fs.Read(h, 0, h.Length);
        if (n >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return "png";
        if (n >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpeg";
        if (n >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return "gif";
        if (n >= 2 && h[0] == 'B' && h[1] == 'M') return "bmp";
        if (n >= 4 && ((h[0] == 'I' && h[1] == 'I' && h[2] == 42 && h[3] == 0) || (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && h[3] == 42))) return "tiff";
        if (n >= 12 && h.AsSpan(0, 4).SequenceEqual("RIFF"u8) && h.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        if (n >= 12 && h.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            var brands = System.Text.Encoding.ASCII.GetString(h, 8, n - 8);
            if (brands.Contains("avif") || brands.Contains("avis")) return "avif";
            return "heic";
        }
        return "unknown";
    }

    public sealed record JpegSegment(byte Marker, int Offset, int Length);

    /// <summary>The marker segments of a JPEG up to the start of scan (APPn, DQT, SOF...). Throws if the structure is broken.</summary>
    public static IReadOnlyList<JpegSegment> JpegMarkers(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 4 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "no SOI");
        var list = new List<JpegSegment>();
        var pos = 2;
        while (pos + 4 <= jpeg.Length)
        {
            Assert.Equal(0xFF, jpeg[pos]);
            var m = jpeg[pos + 1];
            if (m == 0xDA) { list.Add(new JpegSegment(m, pos, 0)); return list; } // SOS: entropy data follows
            if (m == 0xD9) return list;
            var len = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 2));
            list.Add(new JpegSegment(m, pos, len));
            pos += 2 + len;
        }
        return list;
    }

    public static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
