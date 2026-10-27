using MikeShift.Core.Actions;

namespace MikeShift.Core.Images;

/// <summary>
/// Writes a Windows icon (<c>.ico</c>) by hand, because WIC can read the format but has no encoder for it. The file is an <c>ICONDIR</c>,
/// one <c>ICONDIRENTRY</c> per size, then each image as PNG data (supported by Windows since Vista). Every size is scaled from the
/// source itself, never from the previous size, so the small ones stay sharp.
/// </summary>
internal static class IcoWriter
{
    /// <summary>The sizes in the file, smallest first: the ones Windows and browsers ask for.</summary>
    public static readonly IReadOnlyList<int> Sizes = [16, 24, 32, 48, 64, 128, 256];

    private const int HeaderLength = 6;
    private const int EntryLength = 16;

    /// <summary>True when the largest size would be enlarged from <paramref name="source"/> (its longer side is under 256 px).</summary>
    public static bool IsSoft(Raster source) => Math.Max(source.Width, source.Height) < Sizes[^1];

    /// <summary>The <c>.ico</c> file for <paramref name="source"/>: seven square images, the picture centred on transparent pixels.</summary>
    public static async Task<byte[]> WriteAsync(Raster source, CancellationToken ct)
    {
        var images = new List<byte[]>(Sizes.Count);
        foreach (var size in Sizes)
        {
            ct.ThrowIfCancellationRequested();
            var square = OnSquareCanvas(source, size);
            images.Add(await ImageCodec.EncodeAsync(square, ImageFormat.Png, 1.0, null, null, ct).ConfigureAwait(false));
        }

        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type: icon
        w.Write((ushort)images.Count);

        var offset = HeaderLength + EntryLength * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var side = (byte)(Sizes[i] == 256 ? 0 : Sizes[i]); // 0 means 256
            w.Write(side);
            w.Write(side);
            w.Write((byte)0);            // palette colours: none
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // colour planes
            w.Write((ushort)32);         // bits per pixel
            w.Write((uint)images[i].Length);
            w.Write((uint)offset);
            offset = checked(offset + images[i].Length);
        }
        foreach (var image in images)
            w.Write(image);
        w.Flush();
        return file.ToArray();
    }

    /// <summary>
    /// <paramref name="source"/> scaled to fit a <paramref name="size"/> x <paramref name="size"/> square (aspect ratio kept) and centred on a
    /// transparent canvas. The scaling reads the full-size source, so a 16 px icon never comes from a 24 px one.
    /// </summary>
    private static Raster OnSquareCanvas(Raster source, int size)
    {
        var scale = (double)size / Math.Max(source.Width, source.Height);
        var w = Math.Clamp((int)Math.Round(source.Width * scale), 1, size);
        var h = Math.Clamp((int)Math.Round(source.Height * scale), 1, size);
        var scaled = source.CropScale(new RectPx(0, 0, source.Width, source.Height), w, h);
        if (w == size && h == size) return scaled;

        var canvas = new byte[checked(size * size * 4)]; // all zero: transparent
        var left = (size - w) / 2;
        var top = (size - h) / 2;
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(scaled.Bgra, y * w * 4, canvas, ((top + y) * size + left) * 4, w * 4);
        return new Raster(size, size, canvas);
    }
}
