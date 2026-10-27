using SkiaSharp;

namespace MikeShift.Core.Tests;

/// <summary>WebP variants the stock fixtures do not cover (animated, lossy with alpha, solid colours), all built at test time.</summary>
internal static class WebpFixtures
{
    private static readonly byte[] Riff = [(byte)'R', (byte)'I', (byte)'F', (byte)'F'];
    private static readonly byte[] WebpTag = [(byte)'W', (byte)'E', (byte)'B', (byte)'P'];
    private static readonly byte[] Vp8x = [(byte)'V', (byte)'P', (byte)'8', (byte)'X'];
    private static readonly byte[] Anim = [(byte)'A', (byte)'N', (byte)'I', (byte)'M'];
    private static readonly byte[] Anmf = [(byte)'A', (byte)'N', (byte)'M', (byte)'F'];

    /// <summary>A solid-colour picture encoded as WebP (lossless or lossy), with straight alpha.</summary>
    public static byte[] Solid(int width, int height, SKColor colour, bool lossy = false, int quality = 90)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bmp.Erase(colour);
        using var pixmap = bmp.PeekPixels();
        var options = new SKWebpEncoderOptions(lossy ? SKWebpEncoderCompression.Lossy : SKWebpEncoderCompression.Lossless, quality);
        using var data = pixmap.Encode(options)!;
        return data.ToArray();
    }

    /// <summary>
    /// An animated WebP (VP8X + ANIM + one ANMF per frame) made by wrapping still WebPs of the same size; the first frame is the
    /// first colour.
    /// </summary>
    public static byte[] Animated(int width, int height, params SKColor[] frameColours)
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body);
        w.Write(WebpTag);

        w.Write(Vp8x);
        w.Write(10u);
        w.Write((byte)(0x02 | 0x10)); // animation + alpha
        w.Write(new byte[3]);
        WriteU24(w, width - 1);
        WriteU24(w, height - 1);

        w.Write(Anim);
        w.Write(6u);
        w.Write(0xFFFFFFFFu); // background
        w.Write((ushort)0);   // loop forever

        foreach (var colour in frameColours)
        {
            var frameChunks = StillImageChunks(Solid(width, height, colour));
            w.Write(Anmf);
            w.Write((uint)(16 + frameChunks.Length));
            WriteU24(w, 0); WriteU24(w, 0);
            WriteU24(w, width - 1); WriteU24(w, height - 1);
            WriteU24(w, 100);
            w.Write((byte)0);
            w.Write(frameChunks);
            if (frameChunks.Length % 2 == 1) w.Write((byte)0);
        }

        w.Flush();
        var payload = body.ToArray();
        using var file = new MemoryStream();
        file.Write(Riff);
        file.Write(BitConverter.GetBytes((uint)payload.Length));
        file.Write(payload);
        return file.ToArray();
    }

    /// <summary>The image chunks (everything after "WEBP") of a still WebP file.</summary>
    private static byte[] StillImageChunks(byte[] still)
    {
        var chunks = still.AsSpan(12);
        // Skip a leading VP8X header chunk, if the encoder wrote one.
        if (chunks[..4].SequenceEqual(Vp8x))
            chunks = chunks[(8 + (int)BitConverter.ToUInt32(chunks[4..8]))..];
        return chunks.ToArray();
    }

    private static void WriteU24(BinaryWriter w, int v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
    }
}
