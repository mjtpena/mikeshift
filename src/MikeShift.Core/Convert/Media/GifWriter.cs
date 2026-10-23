namespace MikeShift.Core.Media;

/// <summary>
/// A small animated-GIF89a writer: per-frame median-cut palette (15-bit histogram), variable-width LZW, per-frame delay
/// (graphic control extension, centiseconds) and the NETSCAPE2.0 extension so the animation loops forever.
/// </summary>
/// <remarks>
/// Written by hand rather than through WIC because the WIC GIF encoder gives no dependable control over the loop
/// extension and palette on every Windows build, and a GIF is a few hundred lines of well-known format.
/// </remarks>
internal sealed class GifWriter
{
    private readonly Stream _out;
    private readonly int _w;
    private readonly int _h;
    private readonly int[] _hist = new int[32768];

    public GifWriter(Stream output, int width, int height)
    {
        if (width is < 1 or > 65535 || height is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(width));
        _out = output;
        _w = width;
        _h = height;

        Write("GIF89a"u8);
        WriteU16(width);
        WriteU16(height);
        _out.WriteByte(0x70); // no global colour table, 8 bits of colour resolution
        _out.WriteByte(0);    // background
        _out.WriteByte(0);    // pixel aspect ratio
        // NETSCAPE2.0 application extension: loop count 0 = forever.
        Write([0x21, 0xFF, 0x0B]);
        Write("NETSCAPE2.0"u8);
        Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    /// <summary>Adds a full-size frame. <paramref name="rgb"/> is <c>width*height*3</c> bytes (R, G, B), top-down.</summary>
    /// <param name="delayCentiseconds">Frame delay in 1/100 s (10 = 10 fps).</param>
    public void AddFrame(byte[] rgb, int delayCentiseconds)
    {
        if (rgb.Length < _w * _h * 3) throw new ArgumentException("Frame buffer too small.", nameof(rgb));

        var (palette, paletteCount, indices) = Quantize(rgb);

        // Graphic control extension: disposal "do not dispose" (1), no transparency.
        Write([0x21, 0xF9, 0x04, 0x04]);
        WriteU16(Math.Clamp(delayCentiseconds, 1, 65535));
        Write([0x00, 0x00]);

        // Image descriptor with a local colour table.
        var tableBits = 1;
        while ((1 << tableBits) < paletteCount) tableBits++;
        _out.WriteByte(0x2C);
        WriteU16(0); WriteU16(0); WriteU16(_w); WriteU16(_h);
        _out.WriteByte((byte)(0x80 | (tableBits - 1)));
        _out.Write(palette, 0, (1 << tableBits) * 3);

        var minCode = Math.Max(2, tableBits);
        _out.WriteByte((byte)minCode);
        Lzw(indices, minCode);
        _out.WriteByte(0); // block terminator
    }

    /// <summary>Writes the trailer.</summary>
    public void Finish() => _out.WriteByte(0x3B);

    // ------------------------------------------------------------ quantisation (median cut)

    private (byte[] Palette, int Count, byte[] Indices) Quantize(byte[] rgb)
    {
        Array.Clear(_hist);
        var pixels = _w * _h;
        for (var i = 0; i < pixels; i++)
        {
            var o = i * 3;
            _hist[((rgb[o] >> 3) << 10) | ((rgb[o + 1] >> 3) << 5) | (rgb[o + 2] >> 3)]++;
        }

        var colors = new List<int>(2048);
        for (var c = 0; c < _hist.Length; c++)
            if (_hist[c] > 0) colors.Add(c);
        var arr = colors.ToArray();

        var boxes = new List<(int Start, int End)> { (0, arr.Length) };
        while (boxes.Count < 256)
        {
            // Split the most populated box that still holds more than one colour.
            var best = -1;
            long bestPop = 0;
            for (var b = 0; b < boxes.Count; b++)
            {
                var (s, e) = boxes[b];
                if (e - s < 2) continue;
                long pop = 0;
                for (var k = s; k < e; k++) pop += _hist[arr[k]];
                if (pop > bestPop) { bestPop = pop; best = b; }
            }
            if (best < 0) break;

            var (start, end) = boxes[best];
            int rMin = 31, rMax = 0, gMin = 31, gMax = 0, bMin = 31, bMax = 0;
            for (var k = start; k < end; k++)
            {
                var c = arr[k];
                int r = c >> 10, g = (c >> 5) & 31, bl = c & 31;
                if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                if (bl < bMin) bMin = bl; if (bl > bMax) bMax = bl;
            }
            int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
            var axis = rr >= gr && rr >= br ? 0 : gr >= br ? 1 : 2;
            Array.Sort(arr, start, end - start, Comparer<int>.Create((x, y) => Component(x, axis).CompareTo(Component(y, axis))));

            var half = bestPop / 2;
            long acc = 0;
            var split = start + 1;
            for (var k = start; k < end - 1; k++)
            {
                acc += _hist[arr[k]];
                split = k + 1;
                if (acc >= half) break;
            }
            boxes[best] = (start, split);
            boxes.Add((split, end));
        }

        var palette = new byte[256 * 3];
        var lut = new byte[32768];
        for (var b = 0; b < boxes.Count; b++)
        {
            var (s, e) = boxes[b];
            long r = 0, g = 0, bl = 0, pop = 0;
            for (var k = s; k < e; k++)
            {
                var c = arr[k];
                var n = _hist[c];
                r += (long)(c >> 10) * n;
                g += (long)((c >> 5) & 31) * n;
                bl += (long)(c & 31) * n;
                pop += n;
                lut[c] = (byte)b;
            }
            if (pop == 0) pop = 1;
            palette[b * 3] = Expand5((int)(r / pop));
            palette[b * 3 + 1] = Expand5((int)(g / pop));
            palette[b * 3 + 2] = Expand5((int)(bl / pop));
        }

        var indices = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            var o = i * 3;
            indices[i] = lut[((rgb[o] >> 3) << 10) | ((rgb[o + 1] >> 3) << 5) | (rgb[o + 2] >> 3)];
        }
        return (palette, boxes.Count, indices);
    }

    private static int Component(int c, int axis) => axis == 0 ? c >> 10 : axis == 1 ? (c >> 5) & 31 : c & 31;

    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));

    // ------------------------------------------------------------ LZW (GIF flavour, after Kevin Weiner's GIFEncoder)

    private const int HashSize = 9973;
    private const int MaxCodes = 4096;

    private void Lzw(byte[] data, int minCode)
    {
        var clear = 1 << minCode;
        var eoi = clear + 1;
        var initBits = minCode + 1;

        var keys = new int[HashSize];
        var vals = new int[HashSize];
        Array.Fill(keys, -1);

        var nBits = initBits;
        var maxCode = (1 << nBits) - 1;
        var free = clear + 2;
        var clearFlag = false;

        uint cur = 0;
        var curBits = 0;
        var block = new byte[255];
        var blockLen = 0;

        void FlushBlock()
        {
            if (blockLen == 0) return;
            _out.WriteByte((byte)blockLen);
            _out.Write(block, 0, blockLen);
            blockLen = 0;
        }

        void PutByte(byte b)
        {
            block[blockLen++] = b;
            if (blockLen == 255) FlushBlock();
        }

        void Output(int code)
        {
            cur |= (uint)code << curBits;
            curBits += nBits;
            while (curBits >= 8)
            {
                PutByte((byte)(cur & 0xFF));
                cur >>= 8;
                curBits -= 8;
            }
            if (clearFlag)
            {
                nBits = initBits;
                maxCode = (1 << nBits) - 1;
                clearFlag = false;
            }
            else if (free > maxCode)
            {
                nBits++;
                maxCode = nBits == 12 ? MaxCodes : (1 << nBits) - 1;
            }
        }

        void ResetTable()
        {
            Array.Fill(keys, -1);
            free = clear + 2;
            clearFlag = true;
            Output(clear);
        }

        clearFlag = true; // the opening clear code is written with the initial width and keeps it
        Output(clear);

        int ent = data[0];
        for (var i = 1; i < data.Length; i++)
        {
            int c = data[i];
            var key = (ent << 8) | c;
            var h = (int)(((uint)((c << 12) ^ ent) * 2654435761u) % (uint)HashSize);
            var found = -1;
            while (keys[h] != -1)
            {
                if (keys[h] == key) { found = vals[h]; break; }
                h = (h + 1) % HashSize;
            }
            if (found >= 0) { ent = found; continue; }

            Output(ent);
            if (free < MaxCodes)
            {
                keys[h] = key;
                vals[h] = free++;
            }
            else
            {
                ResetTable();
            }
            ent = c;
        }

        Output(ent);
        Output(eoi);
        if (curBits > 0) PutByte((byte)(cur & 0xFF));
        FlushBlock();
    }

    private void Write(ReadOnlySpan<byte> bytes) => _out.Write(bytes);

    private void WriteU16(int v)
    {
        _out.WriteByte((byte)(v & 0xFF));
        _out.WriteByte((byte)((v >> 8) & 0xFF));
    }
}
