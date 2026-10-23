using MikeShift.Core.Actions;

namespace MikeShift.Core.Editing;

/// <summary>The aspect-ratio presets of the crop editor.</summary>
public enum CropAspect
{
    Free,
    Original,
    Square,       // 1:1
    FourFive,     // 4:5
    ThreeTwo,     // 3:2
    SixteenNine,  // 16:9
    NineSixteen,  // 9:16
}

/// <summary>What a pointer press on the crop box grabs.</summary>
public enum CropHandle
{
    None,
    Move,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// The state and rules of the crop editor, free of any UI (PLAN §3.5): image size, preview scale, the crop rectangle in
/// image pixels (top-left origin, in the upright orientation), aspect presets, drag handles with clamping, and the
/// output size with the scale percentage.
/// <para>
/// Coordinates: <see cref="Rect"/> and all rect arithmetic are in image pixels. Pointer positions given to
/// <see cref="HitTest"/> and <see cref="DragBy"/> are in preview pixels (image pixels times <see cref="PreviewScale"/>),
/// relative to the preview's top-left corner.
/// </para>
/// </summary>
public sealed class CropBoxModel
{
    /// <summary>Smallest crop box edge, in image pixels (smaller only if the image itself is smaller).</summary>
    public const int MinBoxPx = 16;

    /// <summary>Lowest value of the scale slider.</summary>
    public const double MinScalePercent = 5;

    /// <summary>Highest value of the scale slider (no upscaling).</summary>
    public const double MaxScalePercent = 100;

    private RectPx _rect;
    private double _scale = 1; // output / rect, 0.0x .. 1; the W/H boxes may take it below MinScalePercent / 100
    private int _outW;
    private int _outH;

    private CropHandle _dragHandle;
    private RectPx _dragStart;

    public CropBoxModel(int imageWidth, int imageHeight, double previewScale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(imageWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(imageHeight, 1);
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        PreviewScale = previewScale > 0 ? previewScale : 1;
        Reset();
    }

    /// <summary>Raised after any state change.</summary>
    public event Action? Changed;

    public int ImageWidth { get; }

    public int ImageHeight { get; }

    /// <summary>Preview pixels per image pixel.</summary>
    public double PreviewScale { get; set; }

    /// <summary>The crop rectangle in image pixels.</summary>
    public RectPx Rect => _rect;

    public CropAspect Aspect { get; private set; }

    /// <summary>Width / height that resizing is locked to, or null when <see cref="Aspect"/> is Free.</summary>
    public double? AspectRatio => RatioOf(Aspect);

    /// <summary>Output width in pixels.</summary>
    public int OutputWidth => _outW;

    /// <summary>Output height in pixels.</summary>
    public int OutputHeight => _outH;

    /// <summary>The scale in percent. 5..100 from the slider; typing a smaller W/H gives a value below 5.</summary>
    public double ScalePercent => _scale * 100;

    /// <summary>The slider position: <see cref="ScalePercent"/> clamped to 5..100.</summary>
    public double SliderPercent => Math.Clamp(ScalePercent, MinScalePercent, MaxScalePercent);

    /// <summary>True while a drag started with <see cref="BeginDrag"/> is in progress.</summary>
    public bool IsDragging => _dragHandle != CropHandle.None;

    private int MinW => Math.Min(MinBoxPx, ImageWidth);

    private int MinH => Math.Min(MinBoxPx, ImageHeight);

    /// <summary>The crop rectangle in preview pixels.</summary>
    public (double X, double Y, double W, double H) PreviewRect =>
        (_rect.X * PreviewScale, _rect.Y * PreviewScale, _rect.W * PreviewScale, _rect.H * PreviewScale);

    /// <summary>True when the crop rectangle lies in the image, is at least the minimum size, and the output size is sane.</summary>
    public bool IsValid =>
        _rect.X >= 0 && _rect.Y >= 0 && _rect.W >= MinW && _rect.H >= MinH
        && _rect.X + _rect.W <= ImageWidth && _rect.Y + _rect.H <= ImageHeight
        && _outW >= 1 && _outH >= 1 && _outW <= _rect.W && _outH <= _rect.H;

    /// <summary>The decision to return from the editor, or null when the state is not valid.</summary>
    public CropChoice? ToChoice() => IsValid ? new CropChoice(_rect, _outW, _outH) : null;

    /// <summary>The ratio (width / height) of a preset for this image; null for Free.</summary>
    public double? RatioOf(CropAspect aspect) => aspect switch
    {
        CropAspect.Free => null,
        CropAspect.Original => (double)ImageWidth / ImageHeight,
        CropAspect.Square => 1.0,
        CropAspect.FourFive => 4.0 / 5.0,
        CropAspect.ThreeTwo => 3.0 / 2.0,
        CropAspect.SixteenNine => 16.0 / 9.0,
        CropAspect.NineSixteen => 9.0 / 16.0,
        _ => null,
    };

    /// <summary>Back to the whole image, Free aspect, 100 % scale.</summary>
    public void Reset()
    {
        _dragHandle = CropHandle.None;
        Aspect = CropAspect.Free;
        _rect = new RectPx(0, 0, ImageWidth, ImageHeight);
        _scale = 1;
        RecomputeOutput();
        Changed?.Invoke();
    }

    /// <summary>
    /// Selects a preset. A locked preset fits the largest box of that ratio inside the current crop box, centred on it.
    /// Free just unlocks.
    /// </summary>
    public void SetAspect(CropAspect aspect)
    {
        Aspect = aspect;
        if (RatioOf(aspect) is { } r)
        {
            var (w, h) = LargestFit(_rect.W, _rect.H, r);
            if (w < MinW || h < MinH)
            {
                // The current box is too thin for this ratio: use the whole image instead. If even that cannot hold a
                // minimum-size box of the ratio (extreme image shapes), the preset cannot apply: stay free.
                (w, h) = LargestFit(ImageWidth, ImageHeight, r);
                if (w < MinW || h < MinH)
                {
                    Aspect = CropAspect.Free;
                    Changed?.Invoke();
                    return;
                }
            }

            w = Math.Min(w, ImageWidth);
            h = Math.Min(h, ImageHeight);
            var cx = _rect.X + _rect.W / 2.0;
            var cy = _rect.Y + _rect.H / 2.0;
            var x = Math.Clamp((int)Math.Round(cx - w / 2.0), 0, ImageWidth - w);
            var y = Math.Clamp((int)Math.Round(cy - h / 2.0), 0, ImageHeight - h);
            _rect = new RectPx(x, y, w, h);
            RecomputeOutput();
        }

        Changed?.Invoke();
    }

    private static (int W, int H) LargestFit(int boxW, int boxH, double ratio)
    {
        double w, h;
        if ((double)boxW / boxH > ratio)
        {
            h = boxH;
            w = h * ratio;
        }
        else
        {
            w = boxW;
            h = w / ratio;
        }

        return (Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
    }

    // ---- Output size / scale ----

    /// <summary>Sets the scale from the slider (clamped to 5..100 %); the output size follows.</summary>
    public void SetScalePercent(double percent)
    {
        if (double.IsNaN(percent)) return;
        _scale = Math.Clamp(percent, MinScalePercent, MaxScalePercent) / 100.0;
        RecomputeOutput();
        Changed?.Invoke();
    }

    /// <summary>
    /// Types a width: H follows by the crop box's aspect, the scale percentage follows too (and may drop below 5 %).
    /// Returns false (and changes nothing) for a value outside 1..crop width.
    /// </summary>
    public bool TrySetOutputWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width)) return false;
        var w = (int)Math.Round(width);
        if (w < 1 || w > _rect.W) return false;
        _scale = (double)w / _rect.W;
        _outW = w;
        _outH = Math.Clamp((int)Math.Round((double)_rect.H * w / _rect.W), 1, _rect.H);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Types a height; the mirror image of <see cref="TrySetOutputWidth"/>.</summary>
    public bool TrySetOutputHeight(double height)
    {
        if (double.IsNaN(height) || double.IsInfinity(height)) return false;
        var h = (int)Math.Round(height);
        if (h < 1 || h > _rect.H) return false;
        _scale = (double)h / _rect.H;
        _outH = h;
        _outW = Math.Clamp((int)Math.Round((double)_rect.W * h / _rect.H), 1, _rect.W);
        Changed?.Invoke();
        return true;
    }

    private void RecomputeOutput()
    {
        if (_scale >= 1)
        {
            _outW = _rect.W;
            _outH = _rect.H;
        }
        else
        {
            _outW = Math.Clamp((int)Math.Round(_rect.W * _scale), 1, _rect.W);
            _outH = Math.Clamp((int)Math.Round(_rect.H * _scale), 1, _rect.H);
        }
    }

    // ---- Moving and resizing ----

    /// <summary>Moves the box by whole image pixels, clamped to the image.</summary>
    public void Nudge(int dx, int dy)
    {
        var x = Math.Clamp(_rect.X + dx, 0, ImageWidth - _rect.W);
        var y = Math.Clamp(_rect.Y + dy, 0, ImageHeight - _rect.H);
        if (x == _rect.X && y == _rect.Y) return;
        _rect = _rect with { X = x, Y = y };
        Changed?.Invoke();
    }

    /// <summary>Which handle (if any) a pointer at a preview position grabs. Corners beat edges beat the inside.</summary>
    /// <param name="px">Pointer x in preview pixels.</param>
    /// <param name="py">Pointer y in preview pixels.</param>
    /// <param name="tolerance">Grab radius in preview pixels (shrinks for very small boxes).</param>
    public CropHandle HitTest(double px, double py, double tolerance)
    {
        var (x, y, w, h) = PreviewRect;
        var tol = Math.Min(tolerance, Math.Min(w, h) / 3.0);
        var right = x + w;
        var bottom = y + h;

        bool nearL = Math.Abs(px - x) <= tol, nearR = Math.Abs(px - right) <= tol;
        bool nearT = Math.Abs(py - y) <= tol, nearB = Math.Abs(py - bottom) <= tol;
        bool inX = px >= x - tol && px <= right + tol, inY = py >= y - tol && py <= bottom + tol;

        if (nearT && nearL) return CropHandle.TopLeft;
        if (nearT && nearR) return CropHandle.TopRight;
        if (nearB && nearL) return CropHandle.BottomLeft;
        if (nearB && nearR) return CropHandle.BottomRight;
        if (nearT && inX) return CropHandle.Top;
        if (nearB && inX) return CropHandle.Bottom;
        if (nearL && inY) return CropHandle.Left;
        if (nearR && inY) return CropHandle.Right;
        if (px >= x && px <= right && py >= y && py <= bottom) return CropHandle.Move;
        return CropHandle.None;
    }

    /// <summary>Starts a drag on a handle. Later <see cref="DragBy"/> calls give the total offset since this point.</summary>
    public void BeginDrag(CropHandle handle)
    {
        _dragHandle = handle;
        _dragStart = _rect;
    }

    /// <summary>Ends the drag.</summary>
    public void EndDrag() => _dragHandle = CropHandle.None;

    /// <summary>Applies the pointer's total offset since <see cref="BeginDrag"/>, in preview pixels.</summary>
    public void DragBy(double dxPreview, double dyPreview) =>
        DragByImagePx(dxPreview / PreviewScale, dyPreview / PreviewScale);

    /// <summary>Applies the total offset since <see cref="BeginDrag"/>, in image pixels.</summary>
    public void DragByImagePx(double dx, double dy)
    {
        if (_dragHandle == CropHandle.None) return;
        var s = _dragStart;
        RectPx next;
        if (_dragHandle == CropHandle.Move)
        {
            next = s with
            {
                X = Math.Clamp((int)Math.Round(s.X + dx), 0, ImageWidth - s.W),
                Y = Math.Clamp((int)Math.Round(s.Y + dy), 0, ImageHeight - s.H),
            };
        }
        else if (AspectRatio is { } ratio)
        {
            next = ResizeLocked(s, _dragHandle, dx, dy, ratio);
        }
        else
        {
            next = ResizeFree(s, _dragHandle, dx, dy);
        }

        if (next == _rect) return;
        _rect = next;
        RecomputeOutput();
        Changed?.Invoke();
    }

    private static (int Sx, int Sy) Direction(CropHandle h) => h switch
    {
        CropHandle.TopLeft => (-1, -1),
        CropHandle.Top => (0, -1),
        CropHandle.TopRight => (1, -1),
        CropHandle.Right => (1, 0),
        CropHandle.BottomRight => (1, 1),
        CropHandle.Bottom => (0, 1),
        CropHandle.BottomLeft => (-1, 1),
        CropHandle.Left => (-1, 0),
        _ => (0, 0),
    };

    private RectPx ResizeFree(RectPx s, CropHandle handle, double dx, double dy)
    {
        var (sx, sy) = Direction(handle);
        int left = s.X, top = s.Y, right = s.X + s.W, bottom = s.Y + s.H;
        if (sx < 0) left = Math.Clamp((int)Math.Round(s.X + dx), 0, right - MinW);
        if (sx > 0) right = Math.Clamp((int)Math.Round(s.X + s.W + dx), left + MinW, ImageWidth);
        if (sy < 0) top = Math.Clamp((int)Math.Round(s.Y + dy), 0, bottom - MinH);
        if (sy > 0) bottom = Math.Clamp((int)Math.Round(s.Y + s.H + dy), top + MinH, ImageHeight);
        return new RectPx(left, top, right - left, bottom - top);
    }

    private RectPx ResizeLocked(RectPx s, CropHandle handle, double dx, double dy, double ratio)
    {
        var (sx, sy) = Direction(handle);
        double minW = Math.Max(MinW, MinH * ratio); // both edges at least their minimum
        double w;
        double h;
        double x;
        double y;

        if (sx != 0 && sy != 0)
        {
            // Corner: the opposite corner is the anchor.
            double ax = sx > 0 ? s.X : s.X + s.W;
            double ay = sy > 0 ? s.Y : s.Y + s.H;
            double availW = sx > 0 ? ImageWidth - ax : ax;
            double availH = sy > 0 ? ImageHeight - ay : ay;
            double w1 = s.W + sx * dx;
            double h1 = s.H + sy * dy;
            w = Math.Max(w1, h1 * ratio);
            w = Math.Min(Math.Max(w, minW), Math.Min(availW, availH * ratio));
            h = w / ratio;
            (w, h) = (Math.Round(w), Math.Round(h));
            if (h > availH) h = Math.Floor(availH);
            if (w > availW) w = Math.Floor(availW);
            x = sx > 0 ? ax : ax - w;
            y = sy > 0 ? ay : ay - h;
        }
        else if (sx != 0)
        {
            // Left/right edge: the opposite edge is fixed, the box stays vertically centred on its middle.
            double ax = sx > 0 ? s.X : s.X + s.W;
            double availW = sx > 0 ? ImageWidth - ax : ax;
            double cy = s.Y + s.H / 2.0;
            double maxH = 2 * Math.Min(cy, ImageHeight - cy);
            w = s.W + sx * dx;
            w = Math.Min(Math.Max(w, minW), Math.Min(availW, maxH * ratio));
            h = w / ratio;
            (w, h) = (Math.Round(w), Math.Round(h));
            x = sx > 0 ? ax : ax - w;
            y = Math.Round(cy - h / 2.0);
        }
        else
        {
            // Top/bottom edge: horizontally centred.
            double ay = sy > 0 ? s.Y : s.Y + s.H;
            double availH = sy > 0 ? ImageHeight - ay : ay;
            double cx = s.X + s.W / 2.0;
            double maxW = 2 * Math.Min(cx, ImageWidth - cx);
            h = s.H + sy * dy;
            double minH = Math.Max(MinH, MinW / ratio);
            h = Math.Min(Math.Max(h, minH), Math.Min(availH, maxW / ratio));
            w = h * ratio;
            (w, h) = (Math.Round(w), Math.Round(h));
            x = Math.Round(cx - w / 2.0);
            y = sy > 0 ? ay : ay - h;
        }

        var wi = Math.Clamp((int)w, 1, ImageWidth);
        var hi = Math.Clamp((int)h, 1, ImageHeight);
        return new RectPx(Math.Clamp((int)x, 0, ImageWidth - wi), Math.Clamp((int)y, 0, ImageHeight - hi), wi, hi);
    }
}
