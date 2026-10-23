using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;

namespace IconGen;

/// <summary>
/// Renders every MikeShift icon from <c>assets/brand/mikeshift.svg</c>.
///   IconGen [--repo &lt;repo root&gt;]
/// Writes (relative to the repo root):
///   src/MikeShift.App/Assets/  Square44x44Logo / Square150x150Logo / Wide310x150Logo / StoreLogo / SplashScreen (scale and targetsize variants),
///                              MikeShift.ico (colour app icon), tray-dark-taskbar.ico (white glyph), tray-light-taskbar.ico (dark glyph)
///   docs/screenshots/brand/icon-sheet.png   the legibility preview (all sizes on light and dark backgrounds)
/// The output is deterministic, so re-running it on an unchanged SVG leaves git clean.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var repo = FindRepoRoot(args);
        var svgPath = Path.Combine(repo, "assets", "brand", "mikeshift.svg");
        var assets = Path.Combine(repo, "src", "MikeShift.App", "Assets");
        var sheetPath = Path.Combine(repo, "docs", "screenshots", "brand", "icon-sheet.png");

        var mark = Mark.Load(svgPath);
        Directory.CreateDirectory(assets);

        foreach (var old in new[] { "Square44x44Logo.png", "Square150x150Logo.png", "StoreLogo.png" })
        {
            var p = Path.Combine(assets, old);
            if (File.Exists(p)) File.Delete(p); // replaced by the scale-/targetsize- variants
        }

        WriteMsixLogos(mark, assets);
        WriteIcons(mark, assets);
        Directory.CreateDirectory(Path.GetDirectoryName(sheetPath)!);
        Sheet.Write(mark, sheetPath);

        Console.WriteLine($"Icons written under {assets}");
        Console.WriteLine($"Preview: {sheetPath}");
        return 0;
    }

    private static string FindRepoRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--repo") return Path.GetFullPath(args[i + 1]);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "assets", "brand", "mikeshift.svg"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository or pass --repo <root>.");
    }

    // ---- MSIX logos ----------------------------------------------------------------------------------------------------------

    private static readonly (int Scale, double Factor)[] Scales = [(100, 1.0), (125, 1.25), (150, 1.5), (200, 2.0), (400, 4.0)];
    private static readonly int[] TargetSizes = [16, 24, 32, 48, 256];

    private static void WriteMsixLogos(Mark mark, string dir)
    {
        foreach (var (scale, f) in Scales)
        {
            Save(mark.Render(Px(44, f), Px(44, f), Look.Plate, rounded: true), dir, $"Square44x44Logo.scale-{scale}.png");
            Save(mark.Render(Px(150, f), Px(150, f), Look.Plate, rounded: false), dir, $"Square150x150Logo.scale-{scale}.png");
            Save(mark.Render(Px(310, f), Px(150, f), Look.Plate, rounded: false, glyphFraction: 0.56), dir, $"Wide310x150Logo.scale-{scale}.png");
            Save(mark.Render(Px(50, f), Px(50, f), Look.Plate, rounded: true), dir, $"StoreLogo.scale-{scale}.png");
            Save(mark.Render(Px(620, f), Px(300, f), Look.Plate, rounded: false, glyphFraction: 0.50), dir, $"SplashScreen.scale-{scale}.png");
        }

        foreach (var size in TargetSizes)
        {
            using var bmp = mark.Render(size, size, Look.Plate, rounded: true);
            Save(bmp, dir, $"Square44x44Logo.targetsize-{size}.png");
            Save(bmp, dir, $"Square44x44Logo.targetsize-{size}_altform-unplated.png");
            Save(bmp, dir, $"Square44x44Logo.targetsize-{size}_altform-lightunplated.png");
        }
    }

    private static int Px(int baseSize, double factor) => (int)Math.Round(baseSize * factor);

    private static void Save(SKBitmap bmp, string dir, string name)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, name), data.ToArray());
    }

    // ---- .ico files ----------------------------------------------------------------------------------------------------------

    /// <summary>Sizes in the tray icons: Windows picks the frame closest to SM_CXSMICON (16 at 100 %, 20 at 125 %, 24 at 150 %, 32 at 200 %, ...).</summary>
    public static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48, 64];
    public static readonly int[] AppIconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static void WriteIcons(Mark mark, string dir)
    {
        Ico.Write(Path.Combine(dir, "MikeShift.ico"), AppIconSizes.Select(s => mark.Render(s, s, Look.Plate, rounded: true)).ToList());
        Ico.Write(Path.Combine(dir, "tray-dark-taskbar.ico"), TraySizes.Select(s => mark.Render(s, s, Look.White, rounded: false)).ToList());
        Ico.Write(Path.Combine(dir, "tray-light-taskbar.ico"), TraySizes.Select(s => mark.Render(s, s, Look.Dark, rounded: false)).ToList());
    }
}

internal enum Look
{
    /// <summary>Blue Fluent plate, white glyph (app, Store and tile logos).</summary>
    Plate,
    /// <summary>Monochrome white glyph, transparent background (tray icon for a dark taskbar).</summary>
    White,
    /// <summary>Monochrome near-black glyph, transparent background (tray icon for a light taskbar).</summary>
    Dark,
}

/// <summary>The parsed SVG: plate gradient and the six segment paths.</summary>
internal sealed class Mark
{
    private const double GlyphDiameter = 240.0; // outer radius 120 in the 256 box
    private const double PlateRadius = 56.0 / 256.0;

    private readonly List<(SKPath Path, float Alpha)> _segments = [];
    private SKColor _from, _to;

    public static Mark Load(string svgPath)
    {
        var doc = XDocument.Load(svgPath);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var mark = new Mark();

        var stops = doc.Descendants(ns + "linearGradient").First().Elements(ns + "stop").ToList();
        mark._from = SKColor.Parse(stops[0].Attribute("stop-color")!.Value);
        mark._to = SKColor.Parse(stops[^1].Attribute("stop-color")!.Value);

        var glyph = doc.Descendants(ns + "g").First(g => (string?)g.Attribute("id") == "glyph");
        foreach (var p in glyph.Elements(ns + "path"))
        {
            var classes = ((string?)p.Attribute("class") ?? "").Split(' ');
            if (!classes.Contains("seg")) continue;
            var path = SKPath.ParseSvgPathData(p.Attribute("d")!.Value) ?? throw new InvalidDataException("Bad path data in the SVG.");
            var opacity = classes.Contains("hot") ? 1f : float.Parse((string?)p.Attribute("fill-opacity") ?? "1", CultureInfo.InvariantCulture);
            mark._segments.Add((path, opacity));
        }
        if (mark._segments.Count != 6) throw new InvalidDataException($"Expected 6 segments, found {mark._segments.Count}.");
        return mark;
    }

    /// <summary>
    /// Renders the mark at <paramref name="w"/> x <paramref name="h"/>. <paramref name="glyphFraction"/> is the glyph's diameter as a fraction of the
    /// smaller side; by default it shrinks with the size (small plates need a bigger glyph to stay legible).
    /// </summary>
    public SKBitmap Render(int w, int h, Look look, bool rounded, double? glyphFraction = null)
    {
        var min = Math.Min(w, h);
        var fraction = glyphFraction ?? (look != Look.Plate ? 0.94 : min <= 24 ? 0.80 : min <= 48 ? 0.72 : 0.64);

        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        if (look == Look.Plate)
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), [_from, _to], SKShaderTileMode.Clamp);
            using var plate = new SKPaint { IsAntialias = true, Shader = shader };
            if (rounded)
            {
                var r = (float)(min * PlateRadius);
                canvas.DrawRoundRect(new SKRect(0, 0, w, h), r, r, plate);
            }
            else
            {
                canvas.DrawRect(new SKRect(0, 0, w, h), plate);
            }
        }

        var glyphColor = look == Look.Dark ? new SKColor(0x1A, 0x1A, 0x1A) : SKColors.White;
        var scale = (float)(fraction * min / GlyphDiameter);
        canvas.Translate(w / 2f, h / 2f);
        canvas.Scale(scale);
        canvas.Translate(-128f, -128f);
        foreach (var (path, alpha) in _segments)
        {
            using var paint = new SKPaint { IsAntialias = true, Color = glyphColor.WithAlpha((byte)Math.Round(alpha * 255)) };
            canvas.DrawPath(path, paint);
        }
        canvas.Flush();
        return bmp;
    }
}

/// <summary>Minimal .ico writer: one PNG-compressed frame per bitmap (supported since Windows Vista).</summary>
internal static class Ico
{
    public static void Write(string path, IReadOnlyList<SKBitmap> frames)
    {
        var pngs = new List<byte[]>();
        foreach (var f in frames)
        {
            using var image = SKImage.FromBitmap(f);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            pngs.Add(data.ToArray());
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        for (var i = 0; i < frames.Count; i++)
        {
            w.Write((byte)(frames[i].Width >= 256 ? 0 : frames[i].Width));
            w.Write((byte)(frames[i].Height >= 256 ? 0 : frames[i].Height));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
        foreach (var f in frames) f.Dispose();
    }
}

/// <summary>The preview sheet: every size on light and dark backgrounds, actual size plus magnified copies of the small ones.</summary>
internal static class Sheet
{
    public static void Write(Mark mark, string path)
    {
        const int panelW = 760, panelH = 1010;
        using var sheet = new SKBitmap(panelW * 2, panelH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(sheet);
        Panel(canvas, mark, 0, panelW, panelH, dark: false);
        Panel(canvas, mark, panelW, panelW, panelH, dark: true);
        canvas.Flush();
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void Panel(SKCanvas canvas, Mark mark, int x0, int w, int h, bool dark)
    {
        var bg = dark ? new SKColor(0x20, 0x20, 0x20) : new SKColor(0xF3, 0xF3, 0xF3);
        var bar = dark ? new SKColor(0x1C, 0x1C, 0x1C) : new SKColor(0xE9, 0xEE, 0xF7);
        var text = dark ? new SKColor(0xF0, 0xF0, 0xF0) : new SKColor(0x1A, 0x1A, 0x1A);
        var sub = dark ? new SKColor(0xA0, 0xA0, 0xA0) : new SKColor(0x60, 0x60, 0x60);
        var trayLook = dark ? Look.White : Look.Dark;

        using (var fill = new SKPaint { Color = bg }) canvas.DrawRect(x0, 0, w, h, fill);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using var title = new SKFont(typeface, 20);
        using var label = new SKFont(typeface, 13);
        using var textPaint = new SKPaint { IsAntialias = true, Color = text };
        using var subPaint = new SKPaint { IsAntialias = true, Color = sub };

        canvas.DrawText(dark ? "Dark theme" : "Light theme", x0 + 24, 36, SKTextAlign.Left, title, textPaint);

        // 1. Tray icons, actual size, on a taskbar-coloured strip.
        var y = 60;
        canvas.DrawText($"Tray icon (monochrome, {(dark ? "white" : "dark")} glyph), actual size", x0 + 24, y, SKTextAlign.Left, label, subPaint);
        y += 12;
        using (var barPaint = new SKPaint { Color = bar }) canvas.DrawRect(x0 + 24, y, w - 48, 64, barPaint);
        var x = x0 + 40;
        foreach (var s in Program.TraySizes)
        {
            using var bmp = mark.Render(s, s, trayLook, rounded: false);
            Blit(canvas, bmp, x, y + (64 - s) / 2f);
            canvas.DrawText(s.ToString(CultureInfo.InvariantCulture), x, y + 60, SKTextAlign.Left, label, subPaint);
            x += s + 34;
        }
        y += 64 + 24;

        // 2. Tray icons magnified (nearest neighbour) so single pixels are visible.
        canvas.DrawText("Tray icon 16 / 24 / 32 px, magnified 6x (nearest neighbour)", x0 + 24, y, SKTextAlign.Left, label, subPaint);
        y += 12;
        x = x0 + 24;
        foreach (var s in new[] { 16, 24, 32 })
        {
            using var bmp = mark.Render(s, s, trayLook, rounded: false);
            var big = s * 6;
            using var backing = new SKPaint { Color = bar };
            canvas.DrawRect(x, y, big, big, backing);
            Magnify(canvas, bmp, x, y, big);
            x += big + 20;
        }
        y += 32 * 6 + 28;

        // 3. Colour app icon, actual size.
        canvas.DrawText("App / Store icon (colour), actual size", x0 + 24, y, SKTextAlign.Left, label, subPaint);
        y += 12;
        x = x0 + 24;
        var rowH = 0;
        foreach (var s in new[] { 16, 20, 24, 32, 40, 48, 64, 96 })
        {
            using var bmp = mark.Render(s, s, Look.Plate, rounded: true);
            Blit(canvas, bmp, x, y);
            canvas.DrawText(s.ToString(CultureInfo.InvariantCulture), x, y + s + 16, SKTextAlign.Left, label, subPaint);
            x += s + 26;
            rowH = Math.Max(rowH, s);
        }
        y += rowH + 36;

        // 4. Colour icon magnified, and the wide tile.
        canvas.DrawText("Colour 16 / 24 px magnified 6x  |  Wide310x150", x0 + 24, y, SKTextAlign.Left, label, subPaint);
        y += 12;
        x = x0 + 24;
        foreach (var s in new[] { 16, 24 })
        {
            using var bmp = mark.Render(s, s, Look.Plate, rounded: true);
            Magnify(canvas, bmp, x, y, s * 6);
            x += s * 6 + 20;
        }
        using (var wide = mark.Render(310, 150, Look.Plate, rounded: false, glyphFraction: 0.56))
            Blit(canvas, wide, x + 4, y);
        y += 150 + 28;

        // 5. Large mark.
        canvas.DrawText("256 px", x0 + 24, y, SKTextAlign.Left, label, subPaint);
        using (var big = mark.Render(256, 256, Look.Plate, rounded: true))
            Blit(canvas, big, x0 + 24, y + 12);
        using (var big = mark.Render(256, 256, trayLook, rounded: false))
        {
            using var backing = new SKPaint { Color = bar };
            canvas.DrawRect(x0 + 24 + 256 + 24, y + 12, 256, 256, backing);
            Blit(canvas, big, x0 + 24 + 256 + 24, y + 12);
        }
    }

    private static void Blit(SKCanvas canvas, SKBitmap bmp, float x, float y)
    {
        using var image = SKImage.FromBitmap(bmp);
        canvas.DrawImage(image, x, y, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
    }

    private static void Magnify(SKCanvas canvas, SKBitmap bmp, float x, float y, int size)
    {
        using var image = SKImage.FromBitmap(bmp);
        canvas.DrawImage(image, new SKRect(x, y, x + size, y + size), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
    }
}
