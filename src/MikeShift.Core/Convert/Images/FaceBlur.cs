using MikeShift.Core.Actions;

namespace MikeShift.Core.Images;

/// <summary>
/// Covers rectangles of a picture so that nothing of what was under them can be recovered: each box is pixelated at a coarse block size
/// (at most six blocks across its long side) and the blocks are then blurred into each other. The pixelation is what makes it irreversible;
/// a blur on its own can be undone to a surprising degree. Everything outside the boxes is copied as it is.
/// </summary>
internal static class FaceBlur
{
    /// <summary>A detected face box is grown to 130 % of its width and height (the detector's box is tight, so hairlines and ears are outside it).</summary>
    public const double Grow = 1.3;

    private const int BlocksAcross = 6;
    private const int MinBlock = 4;
    private const int SmoothingPasses = 2;

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

    /// <summary>A copy of <paramref name="source"/> with every rectangle of <paramref name="regions"/> (already clamped, see <see cref="Cover"/>) pixelated and blurred.</summary>
    public static Raster Apply(Raster source, IReadOnlyList<RectPx> regions)
    {
        var bgra = (byte[])source.Bgra.Clone();
        foreach (var region in regions) Obscure(bgra, source.Width, region);
        return new Raster(source.Width, source.Height, bgra);
    }

    private static void Obscure(byte[] bgra, int stride, RectPx r)
    {
        var count = r.W * r.H;
        // Colour channels are held multiplied by alpha, so that transparent pixels don't pull their (meaningless) colour into the average.
        var ch = new int[4][];
        for (var c = 0; c < 4; c++) ch[c] = new int[count];
        for (var y = 0; y < r.H; y++)
        {
            for (var x = 0; x < r.W; x++)
            {
                var s = ((r.Y + y) * stride + r.X + x) * 4;
                var d = y * r.W + x;
                int a = bgra[s + 3];
                ch[0][d] = bgra[s] * a;
                ch[1][d] = bgra[s + 1] * a;
                ch[2][d] = bgra[s + 2] * a;
                ch[3][d] = a;
            }
        }

        var block = Math.Max(MinBlock, (int)Math.Ceiling(Math.Max(r.W, r.H) / (double)BlocksAcross));
        var radius = Math.Max(1, block / 2);
        foreach (var plane in ch)
        {
            Pixelate(plane, r.W, r.H, block);
            for (var pass = 0; pass < SmoothingPasses; pass++)
            {
                BoxBlur(plane, r.W, r.H, radius, horizontal: true);
                BoxBlur(plane, r.W, r.H, radius, horizontal: false);
            }
        }

        for (var y = 0; y < r.H; y++)
        {
            for (var x = 0; x < r.W; x++)
            {
                var s = ((r.Y + y) * stride + r.X + x) * 4;
                var d = y * r.W + x;
                var a = ch[3][d];
                for (var c = 0; c < 3; c++)
                    bgra[s + c] = a == 0 ? (byte)0 : (byte)Math.Min(255, (ch[c][d] + a / 2) / a);
                bgra[s + 3] = (byte)a;
            }
        }
    }

    private static void Pixelate(int[] plane, int w, int h, int block)
    {
        for (var by = 0; by < h; by += block)
        {
            for (var bx = 0; bx < w; bx += block)
            {
                var bw = Math.Min(block, w - bx);
                var bh = Math.Min(block, h - by);
                long sum = 0;
                for (var y = by; y < by + bh; y++)
                    for (var x = bx; x < bx + bw; x++) sum += plane[y * w + x];
                var mean = (int)((sum + bw * bh / 2) / (bw * bh));
                for (var y = by; y < by + bh; y++)
                    for (var x = bx; x < bx + bw; x++) plane[y * w + x] = mean;
            }
        }
    }

    /// <summary>One box-blur pass along rows or columns; a window that would run past the edge of the box is cut at the edge, so nothing outside the box is read.</summary>
    private static void BoxBlur(int[] plane, int w, int h, int radius, bool horizontal)
    {
        var lines = horizontal ? h : w;
        var length = horizontal ? w : h;
        var step = horizontal ? 1 : w;
        var prefix = new long[length + 1];
        for (var l = 0; l < lines; l++)
        {
            var start = horizontal ? l * w : l;
            for (var i = 0; i < length; i++)
                prefix[i + 1] = prefix[i] + plane[start + i * step];
            for (var i = 0; i < length; i++)
            {
                var lo = Math.Max(0, i - radius);
                var hi = Math.Min(length - 1, i + radius);
                var n = hi - lo + 1;
                plane[start + i * step] = (int)((prefix[hi + 1] - prefix[lo] + n / 2) / n);
            }
        }
    }
}
