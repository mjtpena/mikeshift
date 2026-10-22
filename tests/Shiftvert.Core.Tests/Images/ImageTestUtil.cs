using Shiftvert.Core.Images;

namespace Shiftvert.Core.Tests.Images;

/// <summary>Helpers for looking at converter output: decode it again and compare dimensions, format and colours.</summary>
internal static class ImageTestUtil
{
    public static readonly (byte R, byte G, byte B) Red = (220, 30, 30);
    public static readonly (byte R, byte G, byte B) Green = (30, 200, 30);
    public static readonly (byte R, byte G, byte B) Blue = (30, 30, 220);
    public static readonly (byte R, byte G, byte B) Yellow = (230, 230, 30);
    public static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    /// <summary>Decodes a file with the shipped decoder (WIC, upright).</summary>
    public static Raster Decode(string path) =>
        ImageCodec.DecodeAsync(path, ImageCodec.MaxPixels, CancellationToken.None).GetAwaiter().GetResult().Pixels;

    public static DecodedImage DecodeFull(string path) =>
        ImageCodec.DecodeAsync(path, ImageCodec.MaxPixels, CancellationToken.None).GetAwaiter().GetResult();

    public static ImageContainer ContainerOf(string path) => ImageCodec.Sniff(ImageCodec.ReadHead(path, 64));

    public static (byte R, byte G, byte B, byte A) At(Raster r, int x, int y)
    {
        var i = (y * r.Width + x) * 4;
        return (r.Bgra[i + 2], r.Bgra[i + 1], r.Bgra[i], r.Bgra[i + 3]);
    }

    /// <summary>True if the pixel at (<paramref name="x"/>, <paramref name="y"/>) is within <paramref name="tol"/> of the colour on every channel.</summary>
    public static bool Near(Raster r, int x, int y, (byte R, byte G, byte B) expected, int tol = 24)
    {
        var (pr, pg, pb, _) = At(r, x, y);
        return Math.Abs(pr - expected.R) <= tol && Math.Abs(pg - expected.G) <= tol && Math.Abs(pb - expected.B) <= tol;
    }

    /// <summary>Asserts the four quadrant colours at the middle of each quadrant (any size).</summary>
    public static void AssertQuadrants(Raster r, int tol = 24)
    {
        var qx = r.Width / 4;
        var qy = r.Height / 4;
        Assert.True(Near(r, qx, qy, Red, tol), $"top-left was {At(r, qx, qy)}");
        Assert.True(Near(r, r.Width - qx, qy, Green, tol), $"top-right was {At(r, r.Width - qx, qy)}");
        Assert.True(Near(r, qx, r.Height - qy, Blue, tol), $"bottom-left was {At(r, qx, r.Height - qy)}");
        Assert.True(Near(r, r.Width - qx, r.Height - qy, Yellow, tol), $"bottom-right was {At(r, r.Width - qx, r.Height - qy)}");
    }

    /// <summary>Names of the files directly inside <paramref name="dir"/>, sorted.</summary>
    public static string[] FileNames(string dir) =>
        Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Write(TempDir tmp, string name, byte[] bytes) => tmp.WriteFile(name, bytes);
}
