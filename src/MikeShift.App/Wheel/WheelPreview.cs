using System.Globalization;
using MikeShift.Core;
using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;
using SkiaSharp;
using Windows.UI;

namespace MikeShift.App.Wheel;

/// <summary>
/// Dev-only offscreen render of the wheel: <c>MikeShift.exe --render-wheel &lt;scenario&gt; &lt;out.png&gt;</c> (or <c>all &lt;outDir&gt;</c>, or <c>list</c>).
/// It uses the real <see cref="WheelRenderer"/>, <see cref="WheelModel"/>, <see cref="WheelCallout"/> and <see cref="WheelPlacement"/>, draws a synthetic busy wallpaper
/// (and a mock taskbar where a scenario has one), takes the glass from that wallpaper exactly as the live wheel takes it from the screen, and composites the layered
/// window's premultiplied pixels over the wallpaper. It never touches the real screen, the mouse or any other window, and starts no tray, hook or XAML.
/// </summary>
internal static class WheelPreview
{
    private sealed record Scenario(
        string Name,
        string Theme = "dark",             // dark | light | hc-black | hc-white
        double Dpi = 1,
        double TextScale = 1,
        string Backdrop = "warm",          // warm | bright | dark | busy
        string Files = "image",            // image | video | pdf | zip-only | click-images
        WheelPage Page = WheelPage.Format,
        string? Highlight = null,          // tile title to highlight
        bool Click = false,
        bool Virtual = false,
        PointD Pointer = default,          // pointer relative to the wheel centre, logical px
        TaskbarEdges Taskbar = TaskbarEdges.None,
        bool Quiet = false,                // hints after the first 20 wheels
        ArmKey Arm = ArmKey.Shift);        // the key that opened the wheel (changes the key hints)

    private static readonly Scenario[] Scenarios =
    [
        new("formats-dark", Pointer: new PointD(40, 70)),
        new("formats-dark-highlight", Highlight: "PNG", Pointer: new PointD(40, -90)),
        new("tools-dark-highlight", Page: WheelPage.Tool, Highlight: "Resize 50%", Backdrop: "busy", Pointer: new PointD(90, 60)),
        new("formats-light-highlight", Theme: "light", Backdrop: "bright", Highlight: "TIFF", Pointer: new PointD(-70, 80)),
        new("tools-light", Theme: "light", Page: WheelPage.Tool, Backdrop: "warm", Pointer: new PointD(0, 60)),
        new("virtual-files-dark", Virtual: true, Backdrop: "busy", Pointer: new PointD(0, 20)),
        new("single-zip-hover-dark", Files: "zip-only", Page: WheelPage.Tool, Highlight: "Zip", Backdrop: "busy"),
        new("click-mode-dark", Click: true, Files: "click-images", Highlight: "JPG", Backdrop: "busy"),
        new("hc-black-highlight", Theme: "hc-black", Highlight: "PNG", Backdrop: "bright", Pointer: new PointD(40, -90)),
        new("hc-white-tools", Theme: "hc-white", Page: WheelPage.Tool, Highlight: "Compress", Backdrop: "dark", Pointer: new PointD(0, 80)),
        new("dpi150-formats-dark", Dpi: 1.5, Highlight: "WebP", Pointer: new PointD(-60, 90)),
        new("dpi200-tools-light", Theme: "light", Dpi: 2, Page: WheelPage.Tool, Highlight: "Strip EXIF", Backdrop: "bright", Pointer: new PointD(-90, 40)),
        new("textscale140-dark", TextScale: 1.4, Highlight: "HEIC", Pointer: new PointD(70, 70)),
        new("taskbar-bottom-dark", Taskbar: TaskbarEdges.Bottom, Backdrop: "busy"),
        new("taskbar-left-light", Theme: "light", Taskbar: TaskbarEdges.Left, Backdrop: "warm", Page: WheelPage.Tool),
        new("quiet-hints-dark", Quiet: true, Backdrop: "dark", Pointer: new PointD(30, 90)),
        new("ctrl-mode-formats-dark", Arm: ArmKey.Ctrl, Pointer: new PointD(40, 70)),
        new("ctrl-mode-tools-dark", Arm: ArmKey.Ctrl, Page: WheelPage.Tool, Backdrop: "busy", Pointer: new PointD(90, 60)),
        new("heic-missing-drag-dark", Files: "heic-missing", Backdrop: "busy", Pointer: new PointD(0, 60)),
        new("heic-missing-hover-dark", Files: "heic-missing", Highlight: "Get HEIF", Backdrop: "busy", Pointer: new PointD(0, 20)),
        new("heic-missing-click-light", Theme: "light", Files: "heic-missing", Click: true, Backdrop: "bright"),
    ];

    public static int Run(string[] args)
    {
        try
        {
            WheelLog.Info("--render-wheel " + string.Join(' ', args));
            if (args.Length >= 1 && args[0] == "list")
            {
                foreach (var s in Scenarios) Console.WriteLine(s.Name);
                return 0;
            }
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: MikeShift.exe --render-wheel <scenario|all|list> <out.png|outDir>");
                return 2;
            }

            if (args[0] == "all")
            {
                Directory.CreateDirectory(args[1]);
                using var renderer = new WheelRenderer();
                foreach (var s in Scenarios) Render(renderer, s, Path.Combine(args[1], s.Name + ".png"));
                return 0;
            }

            // "twice:<scenario>": render the scenario twice in one process, so the second timing line is the warm-device, right-DPI cost.
            var repeat = args[0].StartsWith("twice:", StringComparison.Ordinal);
            var scenario = Scenarios.FirstOrDefault(s => s.Name == (repeat ? args[0][6..] : args[0]));
            if (scenario is null)
            {
                Console.Error.WriteLine("unknown scenario: " + args[0]);
                return 2;
            }
            using var one = new WheelRenderer();
            if (repeat) Render(one, scenario, args[1]);
            Render(one, scenario, args[1]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // ------------------------------------------------------------------ one scenario

    private static void Render(WheelRenderer renderer, Scenario sc, string outPath)
    {
        var dpi = sc.Dpi;
        // The "monitor": 560 x 470 logical px, 48 px taskbar on one edge when the scenario has one.
        var screenW = (int)Math.Ceiling(560 * dpi);
        var screenH = (int)Math.Ceiling(470 * dpi);
        var taskbarPx = (int)Math.Ceiling(48 * dpi);
        var bounds = new ScreenRect(0, 0, screenW, screenH);
        var work = bounds;
        if ((sc.Taskbar & TaskbarEdges.Bottom) != 0) work = work with { Bottom = screenH - taskbarPx };
        if ((sc.Taskbar & TaskbarEdges.Left) != 0) work = work with { Left = taskbarPx };
        if ((sc.Taskbar & TaskbarEdges.Top) != 0) work = work with { Top = taskbarPx };
        if ((sc.Taskbar & TaskbarEdges.Right) != 0) work = work with { Right = screenW - taskbarPx };

        // Where the pointer is when the wheel appears: the middle of the work area, or over the taskbar.
        PointPx entry = (sc.Taskbar) switch
        {
            TaskbarEdges.Bottom => new PointPx(work.Centre.X, work.Bottom + taskbarPx / 2),
            TaskbarEdges.Left => new PointPx(taskbarPx / 2, work.Centre.Y),
            TaskbarEdges.Top => new PointPx(work.Centre.X, taskbarPx / 2),
            TaskbarEdges.Right => new PointPx(work.Right + taskbarPx / 2, work.Centre.Y),
            _ => work.Centre,
        };

        var sizePx = WheelGeometry.WindowSizePx(dpi);
        var rect = sc.Click
            ? WheelPlacement.CentredIn(work, sizePx)
            : WheelPlacement.ForCursor(entry, sizePx, work, bounds, (int)Math.Ceiling(WheelPlacement.TaskbarAnchorGap * dpi));
        var centre = rect.Centre;
        var pointer = sc.Taskbar != TaskbarEdges.None
            ? entry
            : new PointPx(centre.X + (int)Math.Round(sc.Pointer.X * dpi), centre.Y + (int)Math.Round(sc.Pointer.Y * dpi));

        // The wallpaper (and mock taskbar), and the glass the live wheel would capture from under its window.
        using var screen = new SKBitmap(new SKImageInfo(screenW, screenH, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(screen))
        {
            PaintBackdrop(canvas, screenW, screenH, sc.Backdrop, dpi);
            if (sc.Taskbar != TaskbarEdges.None) PaintTaskbar(canvas, screenW, screenH, taskbarPx, sc.Taskbar, sc.Theme.Contains("light") || sc.Theme == "hc-white", dpi);
        }
        var glass = Crop(screen, rect);

        // Theme.
        var theme = MakeTheme(sc);
        // Model and tiles, built by the real catalog for the scenario's files.
        var (paths, kindOf) = FilesFor(sc.Files);
        var caps = sc.Files == "heic-missing" ? Capabilities.All with { HeifDecode = false } : Capabilities.All;
        var actions = sc.Virtual ? [] : ActionCatalog.For(paths, caps, kindOf);
        var note = Core.Feedback.CodecHelp.NoteFor(paths, caps, kindOf);
        var model = new WheelModel(sc.Click ? WheelMode.Click : WheelMode.Drag, ToolsKey.Ctrl, sc.Arm);
        model.SetActions(actions);
        if (sc.Page == WheelPage.Tool && model.Page != WheelPage.Tool) model.SetPage(WheelPage.Tool);
        if (sc.Highlight is { } title)
        {
            var index = model.CurrentActions.ToList().FindIndex(a => a.Title == title);
            if (index < 0) throw new InvalidOperationException($"no tile '{title}' on the {model.Page} page of '{sc.Name}'");
            model.Highlight(index);
        }

        var anim = new WheelAnimation(() => false);
        anim.Show(0);
        anim.TilesArrived(0);
        anim.HighlightChanged(0, model.HighlightedIndex);
        var scene = new WheelScene
        {
            SizePx = sizePx, DpiScale = dpi, Theme = theme, Animation = anim, Now = 1,
            Tiles = model.CurrentActions,
            CentreText = model.CentreTitle,
            CentreBold = model.CentreIsActionName,
            HubHint = model.HubHint,
            VirtualFiles = sc.Virtual,
            HubEmpty = false,
            Callouts = sc.Click
                ? WheelCallout.ForClick(model, SelectionSummary.For(paths, sc.Files == "click-images" ? Category.Image : null), note)
                : new WheelCallouts(WheelCallout.ForDrag(model, sc.Virtual, sc.Quiet ? HintEmphasis.Quiet : HintEmphasis.Prominent, note)),
            MainSide = sc.Click ? CalloutSide.Top : WheelCallout.SideFor((pointer.Y - centre.Y) / dpi),
        };

        renderer.EnsureDevice();
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!theme.HighContrast) renderer.SetGlass(glass, sizePx, dpi, theme.Palette);
        else renderer.ClearGlass();
        var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        var pixels = renderer.Render(scene);
        var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        renderer.Render(scene);   // a steady-state frame: what every animation frame costs
        var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        renderer.ClearGlass();
        static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"  timing: glass bake {Ms(t0, t1):0.0} ms, first frame {Ms(t1, t2):0.0} ms, next frame {Ms(t2, t3):0.0} ms");

        // Composite the layered window (premultiplied BGRA) over the wallpaper, as DWM does.
        var span = screen.GetPixelSpan();
        var pix = pixels;
        for (var y = 0; y < sizePx; y++)
        {
            var sy = rect.Top + y;
            if (sy < 0 || sy >= screenH) continue;
            for (var x = 0; x < sizePx; x++)
            {
                var sx = rect.Left + x;
                if (sx < 0 || sx >= screenW) continue;
                var s = (y * sizePx + x) * 4;
                var d = (sy * screenW + sx) * 4;
                var inv = 255 - pix[s + 3];
                span[d] = (byte)Math.Min(255, pix[s] + span[d] * inv / 255);
                span[d + 1] = (byte)Math.Min(255, pix[s + 1] + span[d + 1] * inv / 255);
                span[d + 2] = (byte)Math.Min(255, pix[s + 2] + span[d + 2] * inv / 255);
                span[d + 3] = 255;
            }
        }

        using (var canvas = new SKCanvas(screen))
        {
            if (!sc.Click && !sc.Virtual) PaintDragImage(canvas, pointer, dpi, sc.Highlight is not null, model.HighlightedAction?.DropDescription);
            if (sc.Virtual) PaintDragImage(canvas, pointer, dpi, false, WheelText.SaveFirst, blocked: true);
            if (!sc.Click) PaintPointer(canvas, pointer, dpi);
        }

        using var image = SKImage.FromBitmap(screen);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(outPath);
        data.SaveTo(file);
        Console.WriteLine($"{sc.Name}: {outPath} ({screenW}x{screenH}, wheel at {rect.Left},{rect.Top} {rect.Width}px, pointer {pointer.X},{pointer.Y}, pill {scene.MainSide})");
    }

    private static WheelTheme MakeTheme(Scenario sc)
    {
        var accent = Color.FromArgb(255, 0, 120, 212);
        WheelTheme Make(bool dark) => new()
        {
            Dark = dark,
            Accent = accent,
            AccentFill = dark ? Color.FromArgb(255, 96, 205, 255) : Color.FromArgb(255, 0, 103, 192),
            TextScale = WheelTypography.ClampScale(sc.TextScale),
            AnimationsEnabled = false,
        };
        return sc.Theme switch
        {
            "light" => Make(false),
            "hc-black" => WheelTheme.HighContrastScheme(
                Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 255, 255),
                Color.FromArgb(255, 26, 235, 255), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 63, 242, 63), dark: true),
            "hc-white" => WheelTheme.HighContrastScheme(
                Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 0, 0, 0),
                Color.FromArgb(255, 55, 0, 110), Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 96, 0, 0), dark: false),
            _ => Make(true),
        };
    }

    private static (IReadOnlyList<string> Paths, Func<string, Category> KindOf) FilesFor(string files) => files switch
    {
        "zip-only" => (["notes.xyz", "data.bin"], _ => Category.Other),
        "click-images" => (["holiday 1.png", "holiday 2.png", "holiday 3.png"], _ => Category.Image),
        "heic-missing" => (["IMG_0042.heic"], _ => Category.Other),   // a HEIC Windows cannot open, on a PC without the HEIF extension
        "video" => (["clip.mp4"], _ => Category.Video),
        "pdf" => (["report.pdf"], _ => Category.Pdf),
        _ => (["photo.avif"], _ => Category.Image),
    };

    // ------------------------------------------------------------------ the synthetic desktop

    private static byte[] Crop(SKBitmap screen, ScreenRect rect)
    {
        var width = rect.Width;
        var result = new byte[width * rect.Height * 4];
        var src = screen.GetPixelSpan();
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp(rect.Left + x, 0, screen.Width - 1);
                var sy = Math.Clamp(rect.Top + y, 0, screen.Height - 1);
                var s = (sy * screen.Width + sx) * 4;
                var d = (y * width + x) * 4;
                result[d] = src[s]; result[d + 1] = src[s + 1]; result[d + 2] = src[s + 2]; result[d + 3] = 255;
            }
        }
        return result;
    }

    private static void PaintBackdrop(SKCanvas canvas, int w, int h, string kind, double dpi)
    {
        var (c1, c2, ink) = kind switch
        {
            "bright" => (new SKColor(250, 244, 230), new SKColor(214, 232, 250), new SKColor(40, 40, 48)),
            "dark" => (new SKColor(18, 22, 34), new SKColor(40, 28, 60), new SKColor(215, 215, 225)),
            "busy" => (new SKColor(30, 120, 200), new SKColor(240, 190, 60), new SKColor(250, 250, 250)),
            _ => (new SKColor(196, 124, 52), new SKColor(120, 80, 44), new SKColor(255, 244, 224)),   // warm: the brown/olive wallpaper of the review
        };
        using (var paint = new SKPaint { IsAntialias = true })
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), [c1, c2], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, w, h, paint);
            paint.Shader = null;

            var rng = new Random(7);
            // Window-like blocks and "text" lines, the kind of detail that shows through bad glass.
            for (var i = 0; i < 9; i++)
            {
                paint.Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 150);
                canvas.DrawCircle(rng.Next(w), rng.Next(h), (float)(rng.Next(30, 90) * dpi), paint);
            }
            paint.Color = ink.WithAlpha(210);
            for (var y = (int)(14 * dpi); y < h; y += (int)(13 * dpi))
            {
                var x0 = (float)(rng.Next(0, 40) * dpi);
                var len = (float)(rng.Next(120, 520) * dpi);
                canvas.DrawRect(x0, y, len, (float)(5 * dpi), paint);
            }
            paint.Color = ink;
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), (float)(15 * dpi));
            for (var i = 0; i < 12; i++)
                canvas.DrawText("Quarterly results and plans 2026", rng.Next(0, w - 120), (float)(rng.Next(20, h - 20)), SKTextAlign.Left, font, paint);
        }
    }

    private static void PaintTaskbar(SKCanvas canvas, int w, int h, int thickness, TaskbarEdges edge, bool light, double dpi)
    {
        var rect = edge switch
        {
            TaskbarEdges.Bottom => new SKRect(0, h - thickness, w, h),
            TaskbarEdges.Top => new SKRect(0, 0, w, thickness),
            TaskbarEdges.Left => new SKRect(0, 0, thickness, h),
            _ => new SKRect(w - thickness, 0, w, h),
        };
        using var paint = new SKPaint { IsAntialias = true, Color = light ? new SKColor(236, 240, 246) : new SKColor(24, 26, 32) };
        canvas.DrawRect(rect, paint);
        paint.Color = light ? new SKColor(120, 130, 150, 200) : new SKColor(210, 220, 235, 200);
        var s = (float)(22 * dpi);
        var horizontal = edge is TaskbarEdges.Bottom or TaskbarEdges.Top;
        for (var i = 0; i < 6; i++)
        {
            var offset = (float)((i + 0.5) * 30 * dpi) + (horizontal ? w / 2f - (float)(90 * dpi) : h / 2f - (float)(90 * dpi));
            var cx = horizontal ? offset : rect.MidX;
            var cy = horizontal ? rect.MidY : offset;
            canvas.DrawRoundRect(cx - s / 2, cy - s / 2, s, s, 5, 5, paint);
        }
    }

    /// <summary>A stand-in for Explorer's drag image: a file-icon thumbnail down-right of the pointer, with the shell's own drop description beside it.</summary>
    private static void PaintDragImage(SKCanvas canvas, PointPx p, double dpi, bool hasText, string? description, bool blocked = false)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 200) };
        var size = (float)(56 * dpi);
        var x = p.X + (float)(6 * dpi);
        var y = p.Y + (float)(6 * dpi);
        canvas.DrawRoundRect(x, y, size, size, 6, 6, paint);
        paint.Color = new SKColor(70, 120, 200, 230);
        canvas.DrawRoundRect(x + size * 0.25f, y + size * 0.15f, size * 0.5f, size * 0.62f, 3, 3, paint);
        if (description is not null && (hasText || blocked))
        {
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), (float)(12 * dpi));
            var w = font.MeasureText(description) + (float)(16 * dpi);
            paint.Color = new SKColor(38, 38, 38, 235);
            canvas.DrawRoundRect(x + size + 4, y + size - (float)(22 * dpi), w, (float)(22 * dpi), 4, 4, paint);
            paint.Color = SKColors.White;
            canvas.DrawText(description, x + size + 4 + (float)(8 * dpi), y + size - (float)(7 * dpi), SKTextAlign.Left, font, paint);
        }
    }

    private static void PaintPointer(SKCanvas canvas, PointPx p, double dpi)
    {
        var path = new SKPathBuilder();
        var k = (float)dpi;
        path.MoveTo(p.X, p.Y);
        path.LineTo(p.X, p.Y + 17 * k);
        path.LineTo(p.X + 4 * k, p.Y + 13 * k);
        path.LineTo(p.X + 7 * k, p.Y + 20 * k);
        path.LineTo(p.X + 10 * k, p.Y + 19 * k);
        path.LineTo(p.X + 7 * k, p.Y + 12 * k);
        path.LineTo(p.X + 12 * k, p.Y + 12 * k);
        path.Close();
        using var built = path.Detach();
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawPath(built, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = SKColors.Black;
        paint.StrokeWidth = 1.2f * k;
        canvas.DrawPath(built, paint);
    }
}
