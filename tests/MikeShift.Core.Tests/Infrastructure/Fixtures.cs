using System.Buffers.Binary;
using System.IO.Compression;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Test fixtures, generated at test time (nothing binary is checked in).
/// <para>This file is the SHARED part (W1): tiny dependency-free generators used by Kind/catalog/zip tests.
/// Each converter package adds its own generators in its own partial file and must not edit this one:</para>
/// <list type="bullet">
/// <item><c>Fixtures.Images.cs</c> (W2a), <c>Fixtures.Pdf.cs</c> (W2b), <c>Fixtures.Media.cs</c> (W2c), <c>Fixtures.Documents.cs</c> (W2d).</item>
/// </list>
/// To include your fixtures in <c>scripts/make-fixtures.ps1</c> output, implement the matching hook in your file, e.g.
/// <c>static partial void WriteImagesFixtures(string dir) { ... }</c>.
/// </summary>
public static partial class Fixtures
{
    /// <summary>Hook: W2a writes image fixtures into <paramref name="dir"/> for manual testing.</summary>
    static partial void WriteImagesFixtures(string dir);

    /// <summary>Hook: W2b writes PDF fixtures into <paramref name="dir"/> for manual testing.</summary>
    static partial void WritePdfFixtures(string dir);

    /// <summary>Hook: W2c writes audio/video fixtures into <paramref name="dir"/> for manual testing.</summary>
    static partial void WriteMediaFixtures(string dir);

    /// <summary>Hook: W2d writes document fixtures into <paramref name="dir"/> for manual testing.</summary>
    static partial void WriteDocumentsFixtures(string dir);

    /// <summary>Writes the complete fixture set (shared + every package's) into <paramref name="dir"/>. Used by <c>scripts/make-fixtures.ps1</c>.</summary>
    public static void WriteAll(string dir)
    {
        Directory.CreateDirectory(dir);
        WriteSharedFixtures(dir);
        WriteImagesFixtures(dir);
        WritePdfFixtures(dir);
        WriteMediaFixtures(dir);
        WriteDocumentsFixtures(dir);
    }

    private static void WriteSharedFixtures(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "sample.png"), Png(64, 48));
        File.WriteAllBytes(Path.Combine(dir, "minimal.pdf"), PdfStub());
        File.WriteAllBytes(Path.Combine(dir, "stream.ts"), MpegTs(16));
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "Hello from MikeShift.\n");
        File.WriteAllText(Path.Combine(dir, "readme.md"), "# Title\n\nSome *text*.\n");
        File.WriteAllText(Path.Combine(dir, "program.ts"), "export const x: number = 1;\n");
        File.WriteAllBytes(Path.Combine(dir, "mystery.bin"), Noise(512, seed: 7));
        var folder = Path.Combine(dir, "folder");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "b");
    }

    /// <summary>Deterministic pseudo-random bytes (not a valid file of any kind, unlikely to start with a magic number).</summary>
    public static byte[] Noise(int length, int seed = 1)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        // Never accidentally look like "%PDF" or a TS sync byte at offset 0.
        if (length > 0) data[0] = 0x00;
        return data;
    }

    /// <summary>A minimal, valid 8-bit RGBA PNG of a solid colour (default opaque blue), built without any imaging library.</summary>
    public static byte[] Png(int width = 8, int height = 8, byte r = 0x20, byte g = 0x60, byte b = 0xE0, byte a = 0xFF)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            for (var x = 0; x < width; x++)
            {
                raw.WriteByte(r); raw.WriteByte(g); raw.WriteByte(b); raw.WriteByte(a);
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A tiny PDF whose first four bytes are <c>%PDF</c>. Enough for magic-number tests; not guaranteed to open in a viewer.</summary>
    public static byte[] PdfStub() => System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>MPEG transport stream skeleton: <paramref name="packets"/> 188-byte packets each starting with the 0x47 sync byte.</summary>
    public static byte[] MpegTs(int packets = 8)
    {
        var data = new byte[packets * 188];
        for (var i = 0; i < packets; i++)
        {
            data[i * 188] = 0x47;
            data[i * 188 + 1] = 0x1F;
            data[i * 188 + 2] = 0xFF;
            data[i * 188 + 3] = 0x10;
            for (var j = 4; j < 188; j++) data[i * 188 + j] = 0xFF;
        }
        return data;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
