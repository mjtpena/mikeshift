using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Windows.Graphics.DirectX;
using System.Runtime.InteropServices.WindowsRuntime;
using Shiftvert.Core.Actions;
using Shiftvert.Core.Interaction;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Shiftvert.App.Wheel;

/// <summary>Everything the renderer needs to draw one frame.</summary>
internal sealed class WheelScene
{
    /// <summary>Output bitmap edge in physical px (380·dpi normally; larger while collapsing).</summary>
    public required int SizePx { get; init; }
    public required double DpiScale { get; init; }
    public required WheelTheme Theme { get; init; }
    public required WheelAnimation Animation { get; init; }
    public required double Now { get; init; }
    /// <summary>The tiles of the current page.</summary>
    public required IReadOnlyList<ActionDef> Tiles { get; init; }
    /// <summary>The previous page's tiles while a page switch is playing.</summary>
    public IReadOnlyList<ActionDef> Outgoing { get; init; } = [];
    public string CentreText { get; init; } = "";
    public bool CentreBold { get; init; }
    public string HubHint { get; init; } = "";
    /// <summary>A message shown in the hub instead of the pill ("Save the file first").</summary>
    public string? Message { get; init; }
    /// <summary>The hub shows nothing (pending state: the disc only).</summary>
    public bool HubEmpty { get; init; }
}

/// <summary>
/// Draws the wheel with Win2D into a premultiplied-BGRA <see cref="CanvasRenderTarget"/> and hands back the pixels
/// (PLAN §3.2). Geometry constants come from <see cref="WheelGeometry"/>, animation values from <see cref="WheelAnimation"/>. The drawing is in
/// logical (DIP) units: the render target's DPI is 96·scale, so 1 unit = 1 logical px.
/// </summary>
internal sealed class WheelRenderer : IDisposable
{
    // The device (D3D11 + D2D + the driver's heaps and the effect shaders) is by far the biggest thing the wheel owns, so it is created
    // on demand and handed back by Release() once the wheel has been hidden for a while (PLAN §6: resident memory).
    private CanvasDevice? _device;
    private bool _software;
    private CanvasRenderTarget? _target;
    private CanvasRenderTarget? _glass;
    private int _glassPx;
    private byte[] _pixels = [];
    private Windows.Storage.Streams.IBuffer? _pixelBuffer;
    private readonly Dictionary<(int N, int I), TileShape> _shapes = [];
    private readonly string _textFont;
    private readonly string _iconFont;
    private readonly Dictionary<string, CanvasTextFormat> _formats = [];

    private sealed record TileShape(CanvasGeometry Geometry, Rect Bounds);

    public WheelRenderer()
    {
        var families = new HashSet<string>(CanvasTextFormat.GetSystemFontFamilies(), StringComparer.OrdinalIgnoreCase);
        _textFont = families.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        _iconFont = families.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    /// <summary>True while a Win2D device exists (the next frame needs no start-up cost).</summary>
    public bool IsWarm => _device is not null;

    /// <summary>True if the Win2D device renders in software (no usable GPU). Valid after the first device was created.</summary>
    public bool IsSoftware => _software;

    /// <summary>The device, created on first use.</summary>
    private CanvasDevice Device
    {
        get
        {
            if (_device is null)
            {
                _device = CreateDevice();
                _software = _device.ForceSoftwareRenderer;
                WheelLog.Info($"Win2D device ready (hardware accelerated: {!_software}), fonts: {_textFont} / {_iconFont}");
            }
            return _device;
        }
    }

    /// <summary>Creates the device now (no-op if it exists).</summary>
    public void EnsureDevice() => _ = Device;

    private static CanvasDevice CreateDevice()
    {
        try
        {
            return new CanvasDevice(false);
        }
        catch (Exception ex)
        {
            WheelLog.Error("hardware Win2D device unavailable, using the software renderer", ex);
            return new CanvasDevice(true);
        }
    }

    /// <summary>Builds a fresh device after a device-lost error.</summary>
    public void RecreateDevice()
    {
        WheelLog.Info("recreating the Win2D device");
        Release();
        EnsureDevice();
    }

    /// <summary>Disposes the device, the render targets, cached geometry and the pixel buffers; the next frame builds them again.</summary>
    public void Release()
    {
        ReleaseResources();
        _device?.Dispose();
        _device = null;
        _pixels = [];
        _pixelBuffer = null;
    }

    private void ReleaseResources()
    {
        _target?.Dispose();
        _target = null;
        _glass?.Dispose();
        _glass = null;
        foreach (var shape in _shapes.Values) shape.Geometry.Dispose();
        _shapes.Clear();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
    }

    // ------------------------------------------------------------------ glass

    /// <summary>Blurs the screen snapshot (<paramref name="bgra"/>, <paramref name="sizePx"/> square) once; every frame then just draws it.</summary>
    public void SetGlass(byte[] bgra, int sizePx, double dpiScale)
    {
        ClearGlass();
        var dpi = (float)(96 * dpiScale);
        using var bitmap = CanvasBitmap.CreateFromBytes(Device, bgra, sizePx, sizePx, DirectXPixelFormat.B8G8R8A8UIntNormalized, dpi, CanvasAlphaMode.Ignore);
        var dips = (float)(sizePx / dpiScale);
        _glass = new CanvasRenderTarget(Device, dips, dips, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using var blur = new GaussianBlurEffect
        {
            Source = bitmap,
            BlurAmount = 9f,
            BorderMode = EffectBorderMode.Hard,
        };
        using var ds = _glass.CreateDrawingSession();
        ds.DrawImage(blur);
        _glassPx = sizePx;
    }

    public void ClearGlass()
    {
        _glass?.Dispose();
        _glass = null;
        _glassPx = 0;
    }

    public bool HasGlass => _glass is not null;

    // ------------------------------------------------------------------ frame

    /// <summary>Draws <paramref name="scene"/>; the returned span is valid until the next call. Size is <c>SizePx × SizePx</c>.</summary>
    public ReadOnlySpan<byte> Render(WheelScene scene)
    {
        var size = scene.SizePx;
        var dpi = (float)(96 * scene.DpiScale);
        if (_target is null || _target.SizeInPixels.Width != size)
        {
            _target?.Dispose();
            var dips = (float)(size / scene.DpiScale);
            _target = new CanvasRenderTarget(Device, dips, dips, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
            _pixels = new byte[size * size * 4];
            _pixelBuffer = _pixels.AsBuffer();
        }

        using (var ds = _target.CreateDrawingSession())
        {
            Draw(ds, scene);
        }
        _target.GetPixelBytes(_pixelBuffer!);
        return _pixels;
    }

    private void Draw(CanvasDrawingSession ds, WheelScene s)
    {
        var theme = s.Theme;
        var anim = s.Animation;
        var now = s.Now;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
        ds.TextAntialiasing = CanvasTextAntialiasing.Auto;
        // Every pixel of the window keeps alpha >= 1 so the whole square is an OLE / mouse hit target.
        ds.Clear(Color.FromArgb(1, 0, 0, 0));

        var opacity = (float)Math.Clamp(anim.Opacity(now), 0, 1);
        if (opacity <= 0.001f) return;

        var logical = (float)(s.SizePx / s.DpiScale);
        var c = logical / 2;
        var centre = new Vector2(c, c);
        var scale = (float)anim.Scale(now);
        var discToWorld = Matrix3x2.CreateScale(scale, centre);

        using var layer = ds.CreateLayer(opacity);
        ds.Transform = discToWorld;

        DrawDisc(ds, s, centre, scale, discToWorld);
        DrawHub(ds, s, centre);

        var tiles = s.Tiles;
        var n = tiles.Count;
        if (n > 0 || s.Outgoing.Count > 0)
        {
            var toWorld = Matrix3x2.CreateTranslation(centre) * discToWorld;
            var switching = anim.PageSwitching(now);
            if (switching)
            {
                var outOpacity = anim.OutgoingOpacity(now);
                if (outOpacity > 0.001 && s.Outgoing.Count > 0)
                    DrawTiles(ds, s, s.Outgoing, toWorld, tilePop: _ => 1, highlight: _ => 0, alpha: outOpacity, slide: 0, pulse: -1);
                var inOpacity = anim.IncomingOpacity(now);
                if (inOpacity > 0.001 && n > 0)
                    DrawTiles(ds, s, tiles, toWorld, tilePop: _ => 1, highlight: _ => 0, alpha: inOpacity, slide: anim.IncomingOffset(now), pulse: -1);
            }
            else if (n > 0)
            {
                DrawTiles(ds, s, tiles, toWorld, tilePop: i => anim.TilePop(i, now), highlight: i => anim.Highlight(i, now), alpha: 1,
                    slide: 0, pulse: anim.PickedIndex);
            }
        }

        ds.Transform = Matrix3x2.Identity;
    }

    // ------------------------------------------------------------------ disc and hub

    private void DrawDisc(CanvasDrawingSession ds, WheelScene s, Vector2 centre, float scale, Matrix3x2 discToWorld)
    {
        var t = s.Theme;
        var r = (float)WheelGeometry.DiscRadius;

        if (t.HighContrast)
        {
            ds.FillCircle(centre, r, t.HcWindow);
            ds.DrawCircle(centre, r - 1, t.HcWindowText, 2);
            return;
        }

        // Soft shadow under the disc.
        using (var commands = new CanvasCommandList(ds))
        {
            using (var cds = commands.CreateDrawingSession())
                cds.FillCircle(centre, r, Colors.Black);
            using var shadow = new ShadowEffect { Source = commands, BlurAmount = 10f, ShadowColor = Color.FromArgb(t.Dark ? (byte)110 : (byte)70, 0, 0, 0) };
            ds.DrawImage(shadow, 0, 4);
        }

        using (var disc = CanvasGeometry.CreateEllipse(ds, centre, r, r))
        {
            if (_glass is not null)
            {
                using var clip = ds.CreateLayer(1f, disc);
                var saved = ds.Transform;
                ds.Transform = Matrix3x2.Identity;
                var offset = (float)((s.SizePx - _glassPx) / 2.0 / s.DpiScale);
                ds.DrawImage(_glass, offset, offset);
                ds.Transform = saved;
            }
            else
            {
                ds.FillGeometry(disc, t.Dark ? Color.FromArgb(255, 32, 32, 36) : Color.FromArgb(255, 236, 236, 240));
            }
        }

        // Darken in dark theme so light backdrops do not wash the white tiles out; then the spec'd tint (dark 5 % white, light 45 % white).
        if (t.Dark) ds.FillCircle(centre, r, Color.FromArgb(140, 0, 0, 0));
        ds.FillCircle(centre, r, WheelTheme.WithAlpha(Colors.White, t.Dark ? 0.05 : 0.45));
        ds.DrawCircle(centre, r - 0.5f, t.Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), 1f);
    }

    private void DrawHub(CanvasDrawingSession ds, WheelScene s, Vector2 centre)
    {
        var t = s.Theme;
        var hub = (float)WheelGeometry.HubRadius;

        if (t.HighContrast)
        {
            ds.FillCircle(centre, hub, t.HcWindow);
            ds.DrawCircle(centre, hub - 1, t.HcWindowText, 2);
        }
        else
        {
            ds.FillCircle(centre, hub, t.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255));
            ds.DrawCircle(centre, hub - 0.5f, t.Dark ? Color.FromArgb(36, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), 1f);
        }

        var text = t.HighContrast ? t.HcWindowText : t.Dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(220, 20, 20, 20);
        var secondary = t.HighContrast ? t.HcWindowText : t.Dark ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(135, 0, 0, 0);

        if (s.HubEmpty) return;

        if (s.Message is { } message)
        {
            DrawCentred(ds, message, centre.X, centre.Y, 84, 40, text, Format(11, FontWeights.SemiBold, wrap: true));
            return;
        }

        var hasHint = s.HubHint.Length > 0;
        var pillY = centre.Y + (hasHint ? -6 : 0);
        var weight = s.CentreBold ? FontWeights.Bold : FontWeights.SemiBold;
        var format = Format(11, weight);
        using var layout = new CanvasTextLayout(ds, s.CentreText, format, 200, 30);
        layout.SetCharacterSpacing(0, s.CentreText.Length, s.CentreBold ? 0.2f : 1.0f, s.CentreBold ? 0.2f : 1.0f, 0f);
        var textWidth = (float)layout.LayoutBounds.Width;
        var pillWidth = Math.Min(92f, textWidth + 20f);
        if (textWidth > 80f)
        {
            // Long action names: shrink to fit the hub.
            layout.SetFontSize(0, s.CentreText.Length, 11f * 80f / textWidth);
            textWidth = (float)layout.LayoutBounds.Width;
            pillWidth = Math.Min(92f, textWidth + 20f);
        }

        if (!t.HighContrast)
        {
            var pill = new Rect(centre.X - pillWidth / 2, pillY - 11, pillWidth, 22);
            ds.FillRoundedRectangle(pill, 11, 11, t.Dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0));
        }
        ds.DrawTextLayout(layout, centre.X - 100, pillY - 15, text); // the layout is 200x30 with centred alignment

        if (hasHint)
            DrawCentred(ds, s.HubHint, centre.X, centre.Y + 18, 90, 14, secondary, Format(9.5f, FontWeights.Normal));
    }

    // ------------------------------------------------------------------ tiles

    private void DrawTiles(
        CanvasDrawingSession ds, WheelScene s, IReadOnlyList<ActionDef> tiles, Matrix3x2 toWorld,
        Func<int, double> tilePop, Func<int, double> highlight, double alpha, double slide, int pulse)
    {
        var n = tiles.Count;
        var t = s.Theme;
        for (var i = 0; i < n; i++)
        {
            var pop = tilePop(i);
            if (pop <= 0.001) continue;
            var hl = (float)Math.Clamp(highlight(i), 0, 1);
            var tileAlpha = alpha * Math.Clamp(pop, 0, 1);

            var radial = hl * WheelGeometry.HighlightPush + slide + (i == pulse ? s.Animation.PickPush(s.Now) : 0);
            var push = n == 1 ? default : WheelGeometry.PushOffset(i, n, radial);
            var popScale = (float)(0.55 + 0.45 * pop);
            var tileToWorld = Matrix3x2.CreateScale(popScale)
                              * Matrix3x2.CreateTranslation((float)push.X, (float)push.Y)
                              * toWorld;
            ds.Transform = tileToWorld;

            var shape = ShapeFor(ds, i, n);
            DrawTileBody(ds, t, shape, hl, tileAlpha);
            DrawTileLabel(ds, t, tiles[i], i, n, hl, tileAlpha);
        }
    }

    private void DrawTileBody(CanvasDrawingSession ds, WheelTheme t, TileShape shape, float hl, double alpha)
    {
        Color top, bottom, rim;
        if (t.HighContrast)
        {
            top = bottom = Lerp(t.HcWindow, t.HcHighlight, hl);
            rim = t.HcWindowText;
        }
        else
        {
            var accentTop = WheelTheme.Mix(t.Accent, Colors.White, 0.16);
            var accentBottom = WheelTheme.Mix(t.Accent, Colors.Black, 0.22);
            var normalTop = t.Dark ? Color.FromArgb((byte)(0.17 * 255), 255, 255, 255) : Color.FromArgb((byte)(0.97 * 255), 255, 255, 255);
            var normalBottom = t.Dark ? Color.FromArgb((byte)(0.08 * 255), 255, 255, 255) : Color.FromArgb((byte)(0.92 * 255), 255, 255, 255);
            top = Lerp(normalTop, accentTop, hl);
            bottom = Lerp(normalBottom, accentBottom, hl);
            var normalRim = t.Dark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(34, 0, 0, 0);
            rim = Lerp(normalRim, Color.FromArgb(120, 255, 255, 255), hl);
        }

        using var brush = new CanvasLinearGradientBrush(ds, Scale(top, alpha), Scale(bottom, alpha))
        {
            StartPoint = new Vector2((float)shape.Bounds.X, (float)shape.Bounds.Y),
            EndPoint = new Vector2((float)shape.Bounds.X, (float)(shape.Bounds.Y + shape.Bounds.Height)),
        };
        ds.FillGeometry(shape.Geometry, brush);
        ds.DrawGeometry(shape.Geometry, Scale(rim, alpha), t.HighContrast ? 2f : 1f);
    }

    private void DrawTileLabel(CanvasDrawingSession ds, WheelTheme t, ActionDef action, int index, int n, float hl, double alpha)
    {
        var label = n == 1 ? new PointD(0, -WheelGeometry.LabelRadius) : WheelGeometry.LabelCentre(index, n);
        float x = (float)label.X, y = (float)label.Y;

        Color normal = t.HighContrast ? t.HcWindowText : t.Dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(225, 24, 24, 24);
        Color selected = t.HighContrast ? t.HcHighlightText : Colors.White;
        var color = Scale(Lerp(normal, selected, hl), alpha);

        var w = (float)WheelGeometry.LabelWidth;
        var h = (float)WheelGeometry.LabelHeight;

        if (action.Page == WheelPage.Format || action.Glyph is null)
        {
            var title = action.Title;
            var size = title.Length > 4 ? 12f : 13.5f;
            DrawCentred(ds, title, x, y, w, h, color, Format(size, FontWeights.Bold));
        }
        else
        {
            DrawCentred(ds, action.Glyph, x, y - 8, w, 20, color, IconFormat(15));
            DrawCentred(ds, action.Title, x, y + 11, w, 14, color, Format(8.5f, FontWeights.SemiBold));
        }
    }

    private TileShape ShapeFor(CanvasDrawingSession ds, int index, int n)
    {
        if (_shapes.TryGetValue((n, index), out var cached)) return cached;

        var inset = WheelGeometry.TileCornerRadius;
        var seg = WheelGeometry.Segment(index, n, inset);
        CanvasGeometry core;
        if (seg.IsFullRing)
        {
            using var outer = CanvasGeometry.CreateCircle(Device, Vector2.Zero, (float)seg.OuterRadius);
            using var inner = CanvasGeometry.CreateCircle(Device, Vector2.Zero, (float)seg.InnerRadius);
            core = outer.CombineWith(inner, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        }
        else
        {
            using var path = new CanvasPathBuilder(Device);
            path.BeginFigure(P(seg.OuterStart));
            path.AddArc(P(seg.OuterEnd), (float)seg.OuterRadius, (float)seg.OuterRadius, 0, CanvasSweepDirection.Clockwise,
                seg.OuterIsLargeArc ? CanvasArcSize.Large : CanvasArcSize.Small);
            path.AddLine(P(seg.InnerEnd));
            path.AddArc(P(seg.InnerStart), (float)seg.InnerRadius, (float)seg.InnerRadius, 0, CanvasSweepDirection.CounterClockwise,
                seg.InnerIsLargeArc ? CanvasArcSize.Large : CanvasArcSize.Small);
            path.EndFigure(CanvasFigureLoop.Closed);
            core = CanvasGeometry.CreatePath(path);
        }

        // Rounded corners without fillet maths: the inset shape stroked with a round join of width 2·inset, united with itself.
        using var style = new CanvasStrokeStyle { LineJoin = CanvasLineJoin.Round, StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
        using var outline = core.Stroke((float)(2 * inset), style);
        var rounded = core.CombineWith(outline, Matrix3x2.Identity, CanvasGeometryCombine.Union);
        core.Dispose();

        var shape = new TileShape(rounded, rounded.ComputeBounds());
        _shapes[(n, index)] = shape;
        return shape;
    }

    private static Vector2 P(PointD p) => new((float)p.X, (float)p.Y);

    // ------------------------------------------------------------------ text helpers

    private CanvasTextFormat Format(float size, FontWeight weight, bool wrap = false)
    {
        var key = $"t{size}/{weight.Weight}/{wrap}";
        if (_formats.TryGetValue(key, out var existing)) return existing;
        return _formats[key] = new CanvasTextFormat
        {
            FontFamily = _textFont,
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = wrap ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap,
        };
    }

    private CanvasTextFormat IconFormat(float size)
    {
        var key = $"i{size}";
        if (_formats.TryGetValue(key, out var existing)) return existing;
        return _formats[key] = new CanvasTextFormat
        {
            FontFamily = _iconFont,
            FontSize = size,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
    }

    private static void DrawCentred(CanvasDrawingSession ds, string text, float cx, float cy, float width, float height, Color color, CanvasTextFormat format) =>
        ds.DrawText(text, cx - width / 2, cy - height / 2, width, height, color, format);

    // ------------------------------------------------------------------ colour helpers

    private static Color Lerp(Color a, Color b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    private static Color Scale(Color c, double alpha) => alpha >= 0.999 ? c : Color.FromArgb((byte)Math.Round(c.A * alpha), c.R, c.G, c.B);

    public void Dispose()
    {
        ReleaseResources();
        _device?.Dispose();
    }
}
