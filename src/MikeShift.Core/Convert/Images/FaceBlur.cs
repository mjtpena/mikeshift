using MikeShift.Core.Actions;

namespace MikeShift.Core.Images;

/// <summary>
/// Covers rectangles of a picture so that nothing of what was under them can be recovered: each box is reduced to the average colours of at
/// most six blocks across its long side, and the box is then painted from those few averages with smooth transitions between them (a pixelation
/// blurred into itself). The pixelation is what makes it irreversible; a blur on its own can be undone to a surprising degree. Everything
/// outside the boxes is copied as it is. Working memory is a handful of block averages, however large a box is.
/// </summary>
internal static class FaceBlur
{
    /// <summary>A detected face box is grown to 130 % of its width and height (the detector's box is tight, so hairlines and ears are outside it).</summary>
    public const double Grow = 1.3;

    private const int BlocksAcross = 6;
    private const int MinBlock = 4;

    /// <summary>
    /// <paramref name="boxes"/> grown by <see cref="Grow"/> around their centres and clamped to a picture of this size; boxes that end up with
    /// no area are dropped.
    /// </summary>
    public static List<RectPx> Cover(IEnumerable<RectPx> boxes, int width, int height)
    {
        var covered = new List<RectPx>();
        foreach (var box in boxes)
        {
            var growW = box.W * (Grow - 1) / 2;
            var growH = box.H * (Grow - 1) / 2;
            var x0 = Math.Clamp((int)Math.Floor(box.X - growW), 0, width);
            var y0 = Math.Clamp((int)Math.Floor(box.Y - growH), 0, height);
            var x1 = Math.Clamp((int)Math.Ceiling(box.X + box.W + growW), 0, width);
            var y1 = Math.Clamp((int)Math.Ceiling(box.Y + box.H + growH), 0, height);
            if (x1 > x0 && y1 > y0) covered.Add(new RectPx(x0, y0, x1 - x0, y1 - y0));
        }
        return covered;
    }

    /// <summary>
    /// A copy of <paramref name="source"/> with every rectangle of <paramref name="regions"/> (already clamped, see <see cref="Cover"/>) pixelated and
    /// blurred. Checks <paramref name="ct"/> before copying and once per row of blocks and per row of pixels.
    /// </summary>
    public static Raster Apply(Raster source, IReadOnlyList<RectPx> regions, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bgra = (byte[])source.Bgra.Clone();
        foreach (var region in regions) Obscure(bgra, source.Width, region, ct);
        return new Raster(source.Width, source.Height, bgra);
    }

    private static void Obscure(byte[] bgra, int stride, RectPx r, CancellationToken ct)
    {
        var block = Math.Max(MinBlock, (int)Math.Ceiling(Math.Max(r.W, r.H) / (double)BlocksAcross));
        var nx = (r.W + block - 1) / block;
        var ny = (r.H + block - 1) / block;

        // Average of each block, colour channels multiplied by alpha so that transparent pixels don't pull their (meaningless) colour in.
        var mean = new double[ny, nx, 4];
        for (var by = 0; by < ny; by++)
        {
            ct.ThrowIfCancellationRequested();
            for (var bx = 0; bx < nx; bx++)
            {
                var x0 = bx * block;
                var y0 = by * block;
                var x1 = Math.Min(x0 + block, r.W);
                var y1 = Math.Min(y0 + block, r.H);
                double b = 0, g = 0, red = 0, a = 0;
                for (var y = y0; y < y1; y++)
                {
                    var s = ((r.Y + y) * stride + r.X + x0) * 4;
                    for (var x = x0; x < x1; x++, s += 4)
                    {
                        int alpha = bgra[s + 3];
                        b += bgra[s] * alpha;
                        g += bgra[s + 1] * alpha;
                        red += bgra[s + 2] * alpha;
                        a += alpha;
                    }
                }
                var n = (x1 - x0) * (y1 - y0);
                mean[by, bx, 0] = b / n;
                mean[by, bx, 1] = g / n;
                mean[by, bx, 2] = red / n;
                mean[by, bx, 3] = a / n;
            }
        }

        // Paint every pixel from the block averages, interpolating between the centres of neighbouring blocks.
        var cols = new (int Lo, int Hi, double T)[r.W];
        for (var x = 0; x < r.W; x++) cols[x] = Locate(x, block, nx, r.W);
        for (var y = 0; y < r.H; y++)
        {
            ct.ThrowIfCancellationRequested();
            var (ylo, yhi, ty) = Locate(y, block, ny, r.H);
            var d = ((r.Y + y) * stride + r.X) * 4;
            for (var x = 0; x < r.W; x++, d += 4)
            {
                var (xlo, xhi, tx) = cols[x];
                double Mix(int c) =>
                    (mean[ylo, xlo, c] * (1 - tx) + mean[ylo, xhi, c] * tx) * (1 - ty)
                    + (mean[yhi, xlo, c] * (1 - tx) + mean[yhi, xhi, c] * tx) * ty;
                var alpha = Mix(3);
                for (var c = 0; c < 3; c++)
                    bgra[d + c] = alpha < 1e-9 ? (byte)0 : (byte)Math.Clamp((int)Math.Round(Mix(c) / alpha), 0, 255);
                bgra[d + 3] = (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);
            }
        }
    }

    /// <summary>The two block centres around position <paramref name="p"/> (the last block may be shorter) and how far between them it lies.</summary>
    private static (int Lo, int Hi, double T) Locate(int p, int block, int count, int length)
    {
        double Centre(int i) => i * block + (Math.Min((i + 1) * block, length) - i * block) / 2.0;
        var at = p + 0.5;
        var lo = Math.Clamp((int)Math.Floor((at - block / 2.0) / block), 0, count - 1);
        var hi = Math.Min(lo + 1, count - 1);
        if (hi == lo) return (lo, hi, 0);
        var c0 = Centre(lo);
        var c1 = Centre(hi);
        return (lo, hi, Math.Clamp((at - c0) / (c1 - c0), 0, 1));
    }
}
