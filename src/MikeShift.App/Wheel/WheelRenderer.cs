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
using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace MikeShift.App.Wheel;

/// <summary>Everything the renderer needs to draw one frame.</summary>
internal sealed class WheelScene
{
    /// <summary>Output bitmap edge in physical px (380·dpi).</summary>
    public required int SizePx { get; init; }
    public required double DpiScale { get; init; }
    public required WheelTheme Theme { get; init; }
    public required WheelAnimation Animation { get; init; }
    public required double Now { get; init; }
    /// <summary>The tiles of the current page.</summary>
    public required IReadOnlyList<ActionDef> Tiles { get; init; }
    /// <summary>The previous page's tiles while a page switch is playing.</summary>
    public IReadOnlyList<ActionDef> Outgoing { get; init; } = [];
    /// <summary>Hub text: the page title ("Convert to", "Tools") or the hovered tile ("Convert to PNG").</summary>
    public string CentreText { get; init; } = "";
    public bool CentreBold { get; init; }
    /// <summary>The key hint under the hub title ("Ctrl  Tools"); empty for none.</summary>
    public string HubHint { get; init; } = "";
    /// <summary>The drag carries virtual files: the hub shows the Info glyph, the rim turns caution-coloured and no tiles are drawn.</summary>
    public bool VirtualFiles { get; init; }
    /// <summary>The hub shows nothing (pending state: the disc only).</summary>
    public bool HubEmpty { get; init; }
    /// <summary>The pill(s) in the window's margin.</summary>
    public WheelCallouts Callouts { get; init; } = WheelCallouts.None;
    /// <summary>Which margin <see cref="WheelCallouts.Main"/> is drawn in (drag mode: away from the pointer; click mode: above).</summary>
    public CalloutSide MainSide { get; init; } = CalloutSide.Top;
}

/// <summary>
/// Draws the wheel with Win2D into a premultiplied-BGRA <see cref="CanvasRenderTarget"/> and hands back the pixels
/// Geometry constants come from <see cref="WheelGeometry"/>, animation values from <see cref="WheelAnimation"/>, colours from
/// <see cref="WheelPalette"/> (dark/light) or the system colours (high contrast). The drawing is in logical (DIP) units: the render target's
/// DPI is 96·scale, so 1 unit = 1 logical px.
/// </summary>
internal sealed class WheelRenderer : IDisposable
{
    // The device (D3D11 + D2D + the driver's heaps and the effect shaders) is by far the biggest thing the wheel owns, so it is created
    // on demand and handed back by Release() once the wheel has been hidden for a while (to keep resident memory low).
    private CanvasDevice? _device;
    private bool _software;
    private CanvasRenderTarget? _target;
    private CanvasRenderTarget? _glass;
    private int _glassPx;
    private CanvasBitmap? _noise;
    private double _noiseDpi;
    private byte[] _pixels = [];
    private Windows.Storage.Streams.IBuffer? _pixelBuffer;
    private readonly Dictionary<(int N, int I), TileShape> _shapes = [];
    private readonly string _textFont;
    private readonly string _iconFont;
    private readonly Dictionary<string, CanvasTextFormat> _formats = [];
    private readonly Dictionary<(string Text, float Size, int Weight), float> _widths = [];

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
        _noise?.Dispose();
        _noise = null;
        foreach (var shape in _shapes.Values) shape.Geometry.Dispose();
        _shapes.Clear();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
        _widths.Clear();
    }

    // ------------------------------------------------------------------ glass

    /// <summary>
    /// Bakes the acrylic backdrop from the screen snapshot (<paramref name="bgra"/>, <paramref name="sizePx"/> square) once; every frame then just
    /// draws it: Gaussian blur, a saturation boost, then the tint's luminosity cross-faded in (so a bright wallpaper cannot wash the glass out).
    /// </summary>
    public void SetGlass(byte[] bgra, int sizePx, double dpiScale, WheelPalette? palette = null)
    {
        palette ??= WheelPalette.Dark;
        ClearGlass();
        var dpi = (float)(96 * dpiScale);
        using var bitmap = CanvasBitmap.CreateFromBytes(Device, bgra, sizePx, sizePx, DirectXPixelFormat.B8G8R8A8UIntNormalized, dpi, CanvasAlphaMode.Ignore);
        var dips = (float)(sizePx / dpiScale);
        _glass = new CanvasRenderTarget(Device, dips, dips, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using var blur = new GaussianBlurEffect
        {
            Source = bitmap,
            BlurAmount = 12f,
            BorderMode = EffectBorderMode.Hard,
        };
        using var saturated = new SaturationEffect { Source = blur, Saturation = 1.25f };
        using var tintSource = new ColorSourceEffect { Color = WheelTheme.FromRgb(palette.SurfaceTint) };
        using var luminosity = new BlendEffect { Background = saturated, Foreground = tintSource, Mode = BlendEffectMode.Luminosity };
        using var mixed = new CrossFadeEffect { Source1 = saturated, Source2 = luminosity, CrossFade = (float)palette.LuminosityAlpha };
        using var ds = _glass.CreateDrawingSession();
        ds.DrawImage(mixed);
        _glassPx = sizePx;
    }

    public void ClearGlass()
    {
        _glass?.Dispose();
        _glass = null;
        _glassPx = 0;
    }

    public bool HasGlass => _glass is not null;

    /// <summary>A 128 px tile of black/white noise (random alpha), one bitmap pixel per device pixel, drawn at 2 % over the glass.</summary>
    private CanvasBitmap Noise(double dpiScale)
    {
        if (_noise is not null && Math.Abs(_noiseDpi - dpiScale) < 0.001) return _noise;
        _noise?.Dispose();
        const int size = 128;
        var bytes = new byte[size * size * 4];
        var rng = new Random(0x5EED);
        for (var i = 0; i < size * size; i++)
        {
            var a = (byte)rng.Next(256);
            var v = rng.Next(2) == 0 ? a : (byte)0; // premultiplied: white = (a,a,a,a), black = (0,0,0,a)
            bytes[i * 4] = v;
            bytes[i * 4 + 1] = v;
            bytes[i * 4 + 2] = v;
            bytes[i * 4 + 3] = a;
        }
        _noise = CanvasBitmap.CreateFromBytes(Device, bytes, size, size, DirectXPixelFormat.B8G8R8A8UIntNormalized, (float)(96 * dpiScale), CanvasAlphaMode.Premultiplied);
        _noiseDpi = dpiScale;
        return _noise;
    }

    // ------------------------------------------------------------------ frame

    /// <summary>Draws <paramref name="scene"/>; the returned span is valid until the next call. Size is <c>SizePx × SizePx</c>.</summary>
    public ReadOnlySpan<byte> Render(WheelScene scene)
    {
        var size = scene.SizePx;
        var dpi = (float)(96 * scene.DpiScale);
        if (_target is null || _target.SizeInPixels.Width != size || Math.Abs(_target.Dpi - dpi) > 0.01f)
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

        var tiles = s.Tiles;
        var n = tiles.Count;
        var single = n == 1 && !s.VirtualFiles;

        DrawDisc(ds, s, centre, discToWorld);
        if (!single || anim.PageSwitching(now)) DrawHub(ds, s, centre);

        if (!s.VirtualFiles && (n > 0 || s.Outgoing.Count > 0))
        {
            var toWorld = Matrix3x2.CreateTranslation(centre) * discToWorld;
            if (anim.PageSwitching(now))
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
        DrawCallouts(ds, s, logical);
    }

    // ------------------------------------------------------------------ disc and hub

    private void DrawDisc(CanvasDrawingSession ds, WheelScene s, Vector2 centre, Matrix3x2 discToWorld)
    {
        var t = s.Theme;
        var r = (float)WheelGeometry.DiscRadius;

        if (t.HighContrast)
        {
            ds.FillCircle(centre, r, t.HcWindow);
            ds.DrawCircle(centre, r - 1, t.HcWindowText, 2);
            if (s.VirtualFiles) ds.DrawCircle(centre, r - 4, t.HcHighlight, 3);
            return;
        }

        var p = t.Palette;

        // Soft shadow under the disc.
        using (var commands = new CanvasCommandList(ds))
        {
            using (var cds = commands.CreateDrawingSession())
                cds.FillCircle(centre, r, Colors.Black);
            using var shadow = new ShadowEffect { Source = commands, BlurAmount = 10f, ShadowColor = Color.FromArgb(t.Dark ? (byte)110 : (byte)60, 0, 0, 0) };
            ds.DrawImage(shadow, 0, 4);
        }

        using var disc = CanvasGeometry.CreateEllipse(ds, centre, r, r);
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
            ds.FillGeometry(disc, WheelTheme.FromRgb(p.SurfaceTint));
        }

        // The acrylic tint, 2 % noise and the 1 px stroke.
        ds.FillGeometry(disc, WheelTheme.FromRgb(p.SurfaceTint, p.SurfaceTintAlpha * (_glass is null ? 0 : 1)));
        if (_glass is not null)
        {
            using var brush = new CanvasImageBrush(ds, Noise(s.DpiScale))
            {
                ExtendX = CanvasEdgeBehavior.Wrap,
                ExtendY = CanvasEdgeBehavior.Wrap,
                Opacity = (float)p.NoiseAlpha,
                Interpolation = CanvasImageInterpolation.NearestNeighbor,
            };
            ds.FillGeometry(disc, brush);
        }
        ds.DrawCircle(centre, r - 0.5f, WheelTheme.FromRgb(p.DiscStroke, p.DiscStrokeAlpha), 1f);

        if (s.VirtualFiles)
        {
            // A caution-coloured rim: this drag cannot be converted in place.
            ds.DrawCircle(centre, r - 1.5f, WheelTheme.FromRgb(p.CautionGlyph, 0.8), 2f);
        }
    }

    private void DrawHub(CanvasDrawingSession ds, WheelScene s, Vector2 centre)
    {
        var t = s.Theme;
        var hub = (float)WheelGeometry.HubRadius;
        var scaleText = t.TextScale;

        Color text, secondary;
        if (t.HighContrast)
        {
            ds.FillCircle(centre, hub, t.HcWindow);
            ds.DrawCircle(centre, hub - 1, t.HcWindowText, 2);
            text = t.HcWindowText;
            secondary = t.HcWindowText;
        }
        else
        {
            var p = t.Palette;
            ds.FillCircle(centre, hub, WheelTheme.FromRgb(p.HubFill, p.HubFillAlpha));
            ds.DrawCircle(centre, hub - 0.5f, WheelTheme.FromRgb(p.HubStroke, p.HubStrokeAlpha), 1f);
            text = WheelTheme.FromRgb(p.HubText);
            secondary = WheelTheme.FromRgb(p.HubSecondary);
        }

        if (s.VirtualFiles)
        {
            DrawCentred(ds, WheelCallout.InfoGlyph, centre.X, centre.Y, 40, 40,
                t.HighContrast ? t.HcHighlight : WheelTheme.FromRgb(t.Palette.CautionGlyph), IconFormat(28));
            return;
        }
        if (s.HubEmpty) return;

        var hasHint = s.HubHint.Length > 0 && !s.CentreBold && WheelTypography.HubHintFits(scaleText);
        var size = (float)(WheelTypography.Body * scaleText);
        var weight = s.CentreBold ? FontWeights.Bold : FontWeights.SemiBold;

        // The title wraps inside the hub (two lines: "Convert to" / "PNG") and shrinks only if even that does not fit.
        var boxWidth = 84f;
        var boxHeight = hasHint ? 22f : 46f;
        using var layout = FitLayout(ds, s.CentreText, size, weight, boxWidth, boxHeight);
        var titleY = hasHint ? centre.Y - 10 : centre.Y;
        ds.DrawTextLayout(layout, centre.X - boxWidth / 2, titleY - (float)layout.LayoutBounds.Height / 2 - (float)layout.LayoutBounds.Y, text);

        if (hasHint)
            DrawCentred(ds, s.HubHint, centre.X, centre.Y + 17, 96, 16, secondary,
                Format((float)(WheelTypography.Caption * scaleText), FontWeights.Normal));
    }

    /// <summary>A wrapped, centred layout of <paramref name="text"/> in a box; the font size shrinks (never below Caption) until the height fits.</summary>
    private CanvasTextLayout FitLayout(CanvasDrawingSession ds, string text, float size, FontWeight weight, float width, float maxHeight)
    {
        var current = size;
        while (true)
        {
            var format = new CanvasTextFormat
            {
                FontFamily = _textFont,
                FontSize = current,
                FontWeight = weight,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Top,
                WordWrapping = CanvasWordWrapping.Wrap,
            };
            var layout = new CanvasTextLayout(ds, text, format, width, 200);
            format.Dispose();
            if (layout.LayoutBounds.Height <= maxHeight || current <= WheelTypography.MinFitSize)
                return layout;
            layout.Dispose();
            current = Math.Max((float)WheelTypography.MinFitSize, current - 1);
        }
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

            if (n == 1)
            {
                var single = Matrix3x2.CreateScale((float)(0.85 + 0.15 * pop)) * toWorld;
                ds.Transform = single;
                DrawSingleTile(ds, s, tiles[i], hl, tileAlpha);
                continue;
            }

            var radial = hl * WheelGeometry.HighlightPush + slide + (i == pulse ? s.Animation.PickPush(s.Now) : 0);
            var push = WheelGeometry.PushOffset(i, n, radial);
            var popScale = (float)(0.85 + 0.15 * pop);
            var tileToWorld = Matrix3x2.CreateScale(popScale)
                              * Matrix3x2.CreateTranslation((float)push.X, (float)push.Y)
                              * toWorld;
            ds.Transform = tileToWorld;

            var shape = ShapeFor(ds, i, n);
            DrawTileBody(ds, t, shape, hl, tileAlpha);
            DrawTileLabel(ds, t, tiles[i], i, n, hl, tileAlpha);
        }
    }

    private static Color AccentFor(WheelTheme t) => t.HighContrast ? t.HcHighlight : t.AccentFill;

    /// <summary>The label colour on the accent-filled (highlighted) tile: white or black, whichever reaches 4.5:1 against the fill.</summary>
    private static Color LabelOnAccent(WheelTheme t)
    {
        if (t.HighContrast) return t.HcHighlightText;
        var fill = WheelTheme.ToRgb(t.AccentFill);
        return WheelTheme.FromRgb(Contrast.TextOnFill(t.Palette.OnAccentLight, t.Palette.OnAccentDark, [fill]));
    }

    private Color IdleLabel(WheelTheme t) => t.HighContrast ? t.HcButtonText : WheelTheme.FromRgb(t.Palette.TileText);

    private void DrawTileBody(CanvasDrawingSession ds, WheelTheme t, TileShape shape, float hl, double alpha)
    {
        Color top, bottom, rim;
        float rimWidth = 1f;
        if (t.HighContrast)
        {
            top = bottom = Lerp(t.HcButtonFace, t.HcHighlight, hl);
            rim = t.HcButtonText;
            rimWidth = 1f + hl; // the selected tile also gets an inner outline below
        }
        else
        {
            var p = t.Palette;
            var accent = t.AccentFill;
            var normalTop = WheelTheme.FromRgb(p.TileFill, p.TileFillTopAlpha);
            var normalBottom = WheelTheme.FromRgb(p.TileFill, p.TileFillBottomAlpha);
            // Windows 11 accent fills are flat; a hint of light at the top keeps the tile from reading as a sticker.
            var accentTop = WheelTheme.Mix(accent, Colors.White, t.Dark ? 0.04 : 0.0);
            top = Lerp(normalTop, accentTop, hl);
            bottom = Lerp(normalBottom, accent, hl);
            var normalRim = WheelTheme.FromRgb(p.TileStroke, p.TileStrokeAlpha);
            var accentRim = WheelTheme.WithAlpha(WheelTheme.Mix(accent, Colors.Black, 0.35), 0.9);
            rim = Lerp(normalRim, accentRim, hl);
        }

        using var brush = new CanvasLinearGradientBrush(ds, Scale(top, alpha), Scale(bottom, alpha))
        {
            StartPoint = new Vector2((float)shape.Bounds.X, (float)shape.Bounds.Y),
            EndPoint = new Vector2((float)shape.Bounds.X, (float)(shape.Bounds.Y + shape.Bounds.Height)),
        };
        ds.FillGeometry(shape.Geometry, brush);
        ds.DrawGeometry(shape.Geometry, Scale(rim, alpha), rimWidth);

        if (t.HighContrast && hl > 0.01f)
        {
            // High contrast: the selection must not be colour alone. A 3 px ButtonText outline around the Highlight-filled tile
            // (HighlightText would vanish against the disc: it is the colour of the disc in most schemes).
            ds.DrawGeometry(shape.Geometry, Scale(WheelTheme.WithAlpha(t.HcButtonText, hl), alpha), 3f);
        }
    }

    private void DrawTileLabel(CanvasDrawingSession ds, WheelTheme t, ActionDef action, int index, int n, float hl, double alpha)
    {
        var label = WheelGeometry.LabelCentre(index, n);
        float x = (float)label.X, y = (float)label.Y;
        var color = Scale(Lerp(IdleLabel(t), LabelOnAccent(t), hl), alpha);

        var scale = t.TextScale;
        var isFormat = action.Page == WheelPage.Format;
        var glyph = isFormat ? Glyphs.ForFormat(action.Title) : action.Glyph;

        // Room for the text: the tile is as wide as the chord at the label radius (minus the gap and some air), never more than the ring is thick.
        var chord = 2 * WheelGeometry.LabelRadius * Math.Sin(Math.PI / n) - WheelGeometry.TileGap - 8;
        var maxWidth = (float)Math.Clamp(chord, 30, 84);

        var nominal = (float)((isFormat ? WheelTypography.Body : WheelTypography.Caption) * scale);
        var weight = isFormat ? FontWeights.Bold : FontWeights.SemiBold;
        var textWidth = TextWidth(ds, action.Title, nominal, weight);
        var size = (float)WheelTypography.FitSize(nominal, textWidth, maxWidth);

        var glyphSize = (float)(WheelTypography.TileGlyph * Math.Min(scale, 1.25));
        if (glyph is null)
        {
            DrawCentred(ds, action.Title, x, y, maxWidth + 8, size * 1.6f, color, Format(size, weight));
            return;
        }

        // Glyph above the label, the pair centred on the label point.
        var textHeight = size * 1.25f;
        const float gap = 3f;
        var total = glyphSize + gap + textHeight;
        var top = -total / 2;
        DrawCentred(ds, glyph, x, y + top + glyphSize / 2, glyphSize + 8, glyphSize + 8, color, IconFormat(glyphSize));
        DrawCentred(ds, action.Title, x, y + top + glyphSize + gap + textHeight / 2, maxWidth + 8, textHeight + 2, color, Format(size, weight));
    }

    /// <summary>A wheel with one tile: no ring, the hub itself is the button (glyph, label, accent fill on hover).</summary>
    private void DrawSingleTile(CanvasDrawingSession ds, WheelScene s, ActionDef action, float hl, double alpha)
    {
        var t = s.Theme;
        var r = (float)WheelGeometry.SingleHubRadius;
        var centre = Vector2.Zero;
        var scale = t.TextScale;

        Color fillIdle, fillHot, rim;
        if (t.HighContrast)
        {
            fillIdle = t.HcButtonFace;
            fillHot = t.HcHighlight;
            rim = t.HcButtonText;
        }
        else
        {
            var p = t.Palette;
            // Over the hub well, not the glass: idle looks like a raised button, hover is the accent fill.
            fillIdle = WheelTheme.FromRgb(p.HubFill, p.HubFillAlpha);
            fillHot = t.AccentFill;
            rim = WheelTheme.FromRgb(p.HubStroke, 0.18);
        }

        var fill = Lerp(fillIdle, fillHot, hl);
        ds.FillCircle(centre, r, Scale(fill, alpha));
        ds.DrawCircle(centre, r - 0.5f, Scale(Lerp(rim, t.HighContrast ? t.HcHighlightText : WheelTheme.WithAlpha(WheelTheme.Mix(t.AccentFill, Colors.Black, 0.35), 0.9), hl), alpha), t.HighContrast ? 2f : 1f);
        if (t.HighContrast && hl > 0.01f)
        {
            ds.DrawCircle(centre, r, Scale(WheelTheme.WithAlpha(t.HcButtonText, hl), alpha), 3f);
        }

        var color = Scale(Lerp(t.HighContrast ? t.HcButtonText : WheelTheme.FromRgb(t.Palette.HubText), LabelOnAccent(t), hl), alpha);
        var glyph = action.Page == WheelPage.Format ? Glyphs.ForFormat(action.Title) : action.Glyph;
        var labelSize = (float)(WheelTypography.Body * scale);
        var labelWidth = TextWidth(ds, action.Title, labelSize, FontWeights.SemiBold);
        labelSize = (float)WheelTypography.FitSize(labelSize, labelWidth, 2 * r - 24);

        if (glyph is null)
        {
            DrawCentred(ds, action.Title, 0, 0, 2 * r - 16, labelSize * 1.6f, color, Format(labelSize, FontWeights.SemiBold));
            return;
        }
        var glyphSize = (float)(WheelTypography.SingleGlyph * Math.Min(scale, 1.25));
        var textHeight = labelSize * 1.25f;
        var total = glyphSize + 4 + textHeight;
        var top = -total / 2;
        DrawCentred(ds, glyph, 0, top + glyphSize / 2, glyphSize + 8, glyphSize + 8, color, IconFormat(glyphSize));
        DrawCentred(ds, action.Title, 0, top + glyphSize + 4 + textHeight / 2, 2 * r - 16, textHeight + 2, color, Format(labelSize, FontWeights.SemiBold));
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

    // ------------------------------------------------------------------ callout pills (the window's transparent margin)

    private void DrawCallouts(CanvasDrawingSession ds, WheelScene s, float logical)
    {
        if (s.Callouts.Main is { IsEmpty: false } main) DrawCallout(ds, s, main, s.MainSide, logical);
        if (s.Callouts.Secondary is { IsEmpty: false } second)
            DrawCallout(ds, s, second, s.MainSide == CalloutSide.Top ? CalloutSide.Bottom : CalloutSide.Top, logical);
    }

    /// <summary>
    /// One pill in the top or bottom margin: [glyph] [text] | [keycap label]… on an opaque surface, so its contrast never depends on the wallpaper.
    /// Prominent hints get keycap outlines; quiet ones are plain secondary-colour text (still 4.5:1).
    /// </summary>
    private void DrawCallout(CanvasDrawingSession ds, WheelScene s, CalloutContent c, CalloutSide side, float logical)
    {
        var t = s.Theme;
        var p = t.Palette;
        var fontSize = (float)(WheelTypography.Caption * t.TextScale);
        var height = (float)WheelTypography.PillHeight(fontSize);
        const float padX = 10, itemGap = 10, keyGap = 5, maxWidth = 364;
        var keyPad = 6f;

        bool caution = c.Tone == CalloutTone.Caution;
        bool prominent = c.Emphasis == HintEmphasis.Prominent;

        Color fill, stroke, text, secondary, glyphColor;
        float strokeWidth = 1f;
        if (t.HighContrast)
        {
            fill = t.HcButtonFace; stroke = t.HcButtonText; text = t.HcButtonText; secondary = t.HcButtonText; glyphColor = t.HcButtonText;
            strokeWidth = caution ? 2f : 1f;
        }
        else if (caution)
        {
            fill = WheelTheme.FromRgb(p.CautionFill);
            stroke = WheelTheme.FromRgb(p.CautionGlyph, 0.55);
            text = WheelTheme.FromRgb(p.CautionText);
            secondary = text;
            glyphColor = WheelTheme.FromRgb(p.CautionGlyph);
        }
        else
        {
            fill = WheelTheme.FromRgb(p.PillFill);
            stroke = WheelTheme.FromRgb(p.PillStroke, p.PillStrokeAlpha);
            text = WheelTheme.FromRgb(p.PillText);
            secondary = WheelTheme.FromRgb(p.PillSecondary);
            glyphColor = text;
        }

        // Measure.
        var textFormatWeight = FontWeights.SemiBold;
        var glyphWidth = c.Glyph is null ? 0f : fontSize * 1.25f;
        var available = maxWidth - 2 * padX;
        float textWidth = 0;
        if (!string.IsNullOrEmpty(c.Text)) textWidth = TextWidth(ds, c.Text!, fontSize, textFormatWeight);

        float KeyWidth(KeyHint h) => TextWidth(ds, h.Key, fontSize, FontWeights.SemiBold) + (prominent ? 2 * keyPad : 0);
        double HintWidth(KeyHint h) => KeyWidth(h) + keyGap + TextWidth(ds, h.Label, fontSize, FontWeights.Normal);

        var used = glyphWidth + (glyphWidth > 0 && textWidth > 0 ? 6 : 0);
        var textRoom = (float)available - used;
        // Hints give way first; the text is only ellipsised if it alone does not fit.
        var hintsRoom = (float)available - used - textWidth - (textWidth > 0 ? itemGap * 2 : 0);
        var hints = c.KeyHints.Count == 0 ? c.KeyHints : WheelCallout.FitHints(c.KeyHints, hintsRoom, HintWidth, itemGap);
        var hintsWidth = hints.Count == 0 ? 0f : (float)(hints.Sum(HintWidth) + (hints.Count - 1) * itemGap);
        var shownTextWidth = Math.Min(textWidth, textRoom);

        var contentWidth = used + shownTextWidth + (shownTextWidth > 0 && hintsWidth > 0 ? itemGap * 2 : 0) + hintsWidth;
        var width = (float)Math.Min(maxWidth, contentWidth + 2 * padX);
        var cx = logical / 2;
        var band = (float)WheelGeometry.WindowMargin - 5 - height / 2;
        var cy = side == CalloutSide.Top ? band : logical - band;
        var rect = new Rect(cx - width / 2, cy - height / 2, width, height);
        var radius = height / 2;

        // A faint contact shadow, then the pill.
        ds.FillRoundedRectangle(new Rect(rect.X - 1, rect.Y + 1, rect.Width + 2, rect.Height + 2), radius + 1, radius + 1,
            Color.FromArgb(t.Dark ? (byte)70 : (byte)26, 0, 0, 0));
        ds.FillRoundedRectangle(rect, radius, radius, fill);
        ds.DrawRoundedRectangle(new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1), radius - 0.5f, radius - 0.5f, stroke, strokeWidth);

        var x = (float)rect.X + padX + (width - 2 * padX - contentWidth) / 2;
        if (c.Glyph is not null)
        {
            DrawLeft(ds, c.Glyph, x, cy, glyphWidth, height, glyphColor, IconFormat(fontSize * 1.05f));
            x += glyphWidth + (textWidth > 0 ? 6 : 0);
        }
        if (shownTextWidth > 0)
        {
            DrawLeft(ds, c.Text!, x, cy, shownTextWidth + 1, height, text, Format(fontSize, textFormatWeight, trim: true));
            x += shownTextWidth;
            if (hintsWidth > 0)
            {
                x += itemGap;
                ds.DrawLine(x, cy - height * 0.25f, x, cy + height * 0.25f, WheelTheme.WithAlpha(secondary, 0.5), 1f);
                x += itemGap;
            }
        }

        foreach (var (hint, i) in hints.Select((h, i) => (h, i)))
        {
            var keyW = KeyWidth(hint);
            if (prominent)
            {
                var keyRect = new Rect(x + 0.5, cy - (height - 8) / 2 + 0.5, keyW - 1, height - 9);
                ds.FillRoundedRectangle(keyRect, 4, 4, WheelTheme.WithAlpha(secondary, 0.14));
                ds.DrawRoundedRectangle(keyRect, 4, 4, WheelTheme.WithAlpha(secondary, 0.75), 1f);
            }
            DrawLeft(ds, hint.Key, x, cy, keyW, height, prominent ? text : secondary, Format(fontSize, FontWeights.SemiBold)); // centred in the keycap
            x += keyW + keyGap;
            var labelW = TextWidth(ds, hint.Label, fontSize, FontWeights.Normal);
            DrawLeft(ds, hint.Label, x, cy, labelW + 1, height, prominent ? text : secondary, Format(fontSize, FontWeights.Normal));
            x += labelW + itemGap;
        }
    }

    // ------------------------------------------------------------------ text helpers

    private CanvasTextFormat Format(float size, FontWeight weight, bool wrap = false, bool trim = false, bool leftAligned = false)
    {
        var key = $"t{size}/{weight.Weight}/{wrap}/{trim}/{leftAligned}";
        if (_formats.TryGetValue(key, out var existing)) return existing;
        var format = new CanvasTextFormat
        {
            FontFamily = _textFont,
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = leftAligned ? CanvasHorizontalAlignment.Left : CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = wrap ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap,
        };
        if (trim) { format.TrimmingGranularity = CanvasTextTrimmingGranularity.Character; format.TrimmingSign = CanvasTrimmingSign.Ellipsis; }
        return _formats[key] = format;
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

    private float TextWidth(CanvasDrawingSession ds, string text, float size, FontWeight weight)
    {
        var key = (text, size, (int)weight.Weight);
        if (_widths.TryGetValue(key, out var w)) return w;
        using var layout = new CanvasTextLayout(ds, text, Format(size, weight, leftAligned: true), 1000, 100);
        w = (float)Math.Ceiling(layout.LayoutBounds.Width);
        return _widths[key] = w;
    }

    private static void DrawCentred(CanvasDrawingSession ds, string text, float cx, float cy, float width, float height, Color color, CanvasTextFormat format) =>
        ds.DrawText(text, cx - width / 2, cy - height / 2, width, height, color, format);

    private void DrawLeft(CanvasDrawingSession ds, string text, float x, float cy, float width, float height, Color color, CanvasTextFormat format)
    {
        // The formats are centre-aligned (shared with the tiles); a box exactly as wide as the measured text puts it where x says.
        ds.DrawText(text, x, cy - height / 2, width, height, color, format);
    }

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
