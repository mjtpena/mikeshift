using System.Buffers.Binary;
using System.Text;

namespace MikeShift.Core.Images;

/// <summary>
/// Byte-level JPEG helpers: pull the Exif block out of a source file, make it describe upright pixels, put it into a new
/// JPEG, and scan/clean metadata segments (APP1 Exif/XMP, APP13 Photoshop). No pixel data is ever touched.
/// </summary>
internal static class JpegMeta
{
    private static readonly byte[] ExifPrefix = "Exif\0\0"u8.ToArray();

    internal readonly record struct Segment(byte Marker, int Offset, int Length)
    {
        /// <summary>Offset of the first payload byte (after marker and length).</summary>
        public int PayloadOffset => Offset + 4;

        /// <summary>Payload length (without the 2 length bytes).</summary>
        public int PayloadLength => Length - 2;
    }

    /// <summary>Lists the marker segments before the entropy-coded data (stops at SOS/EOI or on malformed data).</summary>
    public static List<Segment> Segments(ReadOnlySpan<byte> jpeg)
    {
        var list = new List<Segment>();
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return list;

        var pos = 2;
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF) break;
            var marker = jpeg[pos + 1];
            if (marker == 0xFF) { pos++; continue; }                 // fill byte
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { pos += 2; continue; } // no length
            if (marker is 0xD9 or 0xDA) break;                       // EOI / SOS: pixel data follows
            var len = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(pos + 2)..]);
            if (len < 2 || pos + 2 + len > jpeg.Length) break;
            list.Add(new Segment(marker, pos, len));
            pos += 2 + len;
        }
        return list;
    }

    /// <summary>The payload (starting with <c>Exif\0\0</c>) of the first Exif APP1 segment, or null.</summary>
    public static byte[]? ExtractExif(ReadOnlySpan<byte> jpeg)
    {
        foreach (var s in Segments(jpeg))
        {
            if (s.Marker != 0xE1 || s.PayloadLength <= ExifPrefix.Length) continue;
            var payload = jpeg.Slice(s.PayloadOffset, s.PayloadLength);
            if (payload[..ExifPrefix.Length].SequenceEqual(ExifPrefix))
                return payload.ToArray();
        }
        return null;
    }

    /// <summary>
    /// Returns a copy of an Exif payload that is correct for pixels that were already rotated upright:
    /// Orientation = 1, thumbnail IFD unlinked, pixel dimension tags updated. Null if the block is malformed
    /// (the caller then drops the metadata rather than keep a wrong orientation).
    /// </summary>
    public static byte[]? MakeUpright(byte[] exif, int width, int height)
    {
        try
        {
            var data = (byte[])exif.Clone();
            const int tiff = 6;
            if (data.Length < tiff + 8) return null;

            bool le;
            if (data[tiff] == 'I' && data[tiff + 1] == 'I') le = true;
            else if (data[tiff] == 'M' && data[tiff + 1] == 'M') le = false;
            else return null;

            ushort R16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o));
            uint R32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o));
            void W16(int o, int v) { if (le) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(o), (ushort)v); else BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(o), (ushort)v); }
            void W32(int o, uint v) { if (le) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o), v); else BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(o), v); }

            if (R16(tiff + 2) != 42) return null;
            var ifd0 = tiff + checked((int)R32(tiff + 4));
            if (ifd0 + 2 > data.Length) return null;

            var exifIfd = -1;
            var count = R16(ifd0);
            if (ifd0 + 2 + count * 12 + 4 > data.Length) return null;
            for (var i = 0; i < count; i++)
            {
                var e = ifd0 + 2 + i * 12;
                switch (R16(e))
                {
                    case 0x0112: // Orientation
                        W16(e + 8, 1);
                        break;
                    case 0x8769: // Exif IFD pointer
                        exifIfd = tiff + checked((int)R32(e + 8));
                        break;
                    case 0x0100: SetDimension(e, width); break;  // ImageWidth
                    case 0x0101: SetDimension(e, height); break; // ImageLength
                }
            }
            // The thumbnail (IFD1) shows the unrotated picture: unlink it.
            W32(ifd0 + 2 + count * 12, 0);

            if (exifIfd > 0 && exifIfd + 2 <= data.Length)
            {
                var n = R16(exifIfd);
                if (exifIfd + 2 + n * 12 <= data.Length)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var e = exifIfd + 2 + i * 12;
                        switch (R16(e))
                        {
                            case 0xA002: SetDimension(e, width); break;  // PixelXDimension
                            case 0xA003: SetDimension(e, height); break; // PixelYDimension
                        }
                    }
                }
            }
            return data;

            void SetDimension(int entry, int value)
            {
                var type = R16(entry + 2);
                if (type == 3) W16(entry + 8, value);
                else if (type == 4) W32(entry + 8, (uint)value);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Inserts an Exif APP1 segment (payload starting with <c>Exif\0\0</c>) right after SOI / a leading JFIF APP0.</summary>
    public static byte[] InsertExif(byte[] jpeg, byte[] exifPayload)
    {
        if (exifPayload.Length + 2 > ushort.MaxValue || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return jpeg;

        var insertAt = 2;
        var first = Segments(jpeg).FirstOrDefault();
        if (first.Length > 0 && first.Marker == 0xE0 && first.Offset == 2)
            insertAt = first.Offset + 2 + first.Length;

        var result = new byte[jpeg.Length + 4 + exifPayload.Length];
        Buffer.BlockCopy(jpeg, 0, result, 0, insertAt);
        result[insertAt] = 0xFF;
        result[insertAt + 1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(insertAt + 2), (ushort)(exifPayload.Length + 2));
        Buffer.BlockCopy(exifPayload, 0, result, insertAt + 4, exifPayload.Length);
        Buffer.BlockCopy(jpeg, insertAt, result, insertAt + 4 + exifPayload.Length, jpeg.Length - insertAt);
        return result;
    }

    /// <summary>True for segments that carry personal metadata: any APP1 (Exif, XMP) and APP13 (Photoshop/IPTC).</summary>
    public static bool IsMetadataSegment(Segment s) =>
        s.Marker == 0xE1 || s.Marker == 0xED;

    /// <summary>True if the JPEG still carries APP1 (Exif/XMP) or APP13 (Photoshop) segments.</summary>
    public static bool HasMetadataSegments(ReadOnlySpan<byte> jpeg) =>
        Segments(jpeg).Any(s => IsMetadataSegment(s));

    /// <summary>Removes APP1/APP13 segments (the last resort when an encoder wrote metadata anyway).</summary>
    public static byte[] RemoveMetadataSegments(byte[] jpeg)
    {
        var drop = Segments(jpeg).Where(s => IsMetadataSegment(s)).OrderBy(s => s.Offset).ToList();
        if (drop.Count == 0) return jpeg;

        using var ms = new MemoryStream(jpeg.Length);
        var pos = 0;
        foreach (var s in drop)
        {
            ms.Write(jpeg, pos, s.Offset - pos);
            pos = s.Offset + 2 + s.Length;
        }
        ms.Write(jpeg, pos, jpeg.Length - pos);
        return ms.ToArray();
    }

    /// <summary>True if an Exif payload carries a GPS IFD (tag 0x8825 in IFD0).</summary>
    public static bool HasGps(byte[] exif)
    {
        try
        {
            const int tiff = 6;
            var le = exif[tiff] == 'I';
            ushort R16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(exif.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(exif.AsSpan(o));
            uint R32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(exif.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(exif.AsSpan(o));
            var ifd0 = tiff + (int)R32(tiff + 4);
            var count = R16(ifd0);
            for (var i = 0; i < count; i++)
                if (R16(ifd0 + 2 + i * 12) == 0x8825) return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Reads the first Exif Orientation value (1-8) from a JPEG; 1 if absent.</summary>
    public static int ReadOrientation(ReadOnlySpan<byte> jpeg)
    {
        var exif = ExtractExif(jpeg);
        if (exif is null) return 1;
        try
        {
            const int tiff = 6;
            var le = exif[tiff] == 'I';
            ushort R16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(exif.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(exif.AsSpan(o));
            uint R32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(exif.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(exif.AsSpan(o));
            var ifd0 = tiff + (int)R32(tiff + 4);
            var count = R16(ifd0);
            for (var i = 0; i < count; i++)
            {
                var e = ifd0 + 2 + i * 12;
                if (R16(e) == 0x0112) return R16(e + 8);
            }
        }
        catch (Exception)
        {
            // fall through
        }
        return 1;
    }

    /// <summary>Human-readable marker list, for diagnostics in tests.</summary>
    public static string Describe(ReadOnlySpan<byte> jpeg)
    {
        var sb = new StringBuilder();
        foreach (var s in Segments(jpeg))
            sb.Append($"FF{s.Marker:X2}({s.Length}) ");
        return sb.ToString().Trim();
    }
}
