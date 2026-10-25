using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MikeShift.Core.Editing;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace MikeShift.App.Editors;

/// <summary>
/// The crop preview: the picture, a dimmed outside, the draggable crop box with rule-of-thirds grid and corner brackets.
/// All rules live in <see cref="CropBoxModel"/>; this control only draws it and turns pointer/keys into model calls.
/// Keyboard (when focused): arrows nudge 1 px, Shift+arrows 10 px.
/// </summary>
internal sealed class CropSurface : UserControl
{
    private const double GrabToleranceDip = 12;

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly Brush WhiteBrush = new SolidColorBrush(Colors.White);
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
    private static readonly Brush ShadowBrush = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));

    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly RectangleGeometry _dimOuter = new();
    private readonly RectangleGeometry _dimHole = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _dim = new() { Fill = DimBrush, IsHitTestVisible = false };
    private readonly Rectangle _shadow = new() { Stroke = ShadowBrush, StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Rectangle _border = new() { Stroke = WhiteBrush, StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Line[] _grid = [Thirds(), Thirds(), Thirds(), Thirds()];
    private readonly Polyline[] _brackets = [Bracket(), Bracket(), Bracket(), Bracket()];

    private CropBoxModel? _model;
    private Point _dragOrigin;
    private bool _dragging;
    private CropHandle _hover = CropHandle.None;

    public CropSurface()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AutomationProperties.SetName(this, "Crop area");
        AutomationProperties.SetHelpText(this, "Drag the box or its corners and edges. Arrow keys move it by one pixel, Shift and arrow keys by ten.");

        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        root.Children.Add(_image);
        root.Children.Add(_overlay);
        _dim.Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { _dimOuter, _dimHole } };
        _overlay.Children.Add(_dim);
        foreach (var r in new[] { _shadow, _border }) _overlay.Children.Add(r);
        foreach (var l in _grid) _overlay.Children.Add(l);
        foreach (var b in _brackets) _overlay.Children.Add(b);
        Content = root;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndDrag();
        PointerExited += (_, _) => SetCursor(CropHandle.None);
        KeyDown += OnKeyDown;
    }

    /// <summary>Raised after the user dragged or nudged (the model's own Changed event covers redraws).</summary>
    public event Action? UserEdited;

    private static Line Thirds() => new() { Stroke = GridBrush, StrokeThickness = 1, IsHitTestVisible = false };

    private static Polyline Bracket() => new()
    {
        Stroke = WhiteBrush,
        StrokeThickness = 3,
        StrokeStartLineCap = PenLineCap.Square,
        StrokeEndLineCap = PenLineCap.Square,
        StrokeLineJoin = PenLineJoin.Miter,
        IsHitTestVisible = false,
    };

    /// <summary>Shows the picture at the given on-screen size and starts tracking the model.</summary>
    public void Attach(Microsoft.UI.Xaml.Media.ImageSource source, CropBoxModel model, double displayWidth, double displayHeight)
    {
        if (_model is not null) _model.Changed -= Redraw;
        _model = model;
        _image.Source = source;
        Width = displayWidth;
        Height = displayHeight;
        _overlay.Width = displayWidth;
        _overlay.Height = displayHeight;
        model.PreviewScale = displayWidth / model.ImageWidth;
        model.Changed += Redraw;
        Redraw();
    }

    private void Redraw()
    {
        if (_model is null) return;
        var (x, y, w, h) = _model.PreviewRect;
        double fullW = Width, fullH = Height;

        // The dimmed outside is one shape (the picture minus the box), so no seams show between pieces.
        _dimOuter.Rect = new Rect(0, 0, fullW, fullH);
        _dimHole.Rect = new Rect(x, y, w, h);

        Place(_border, x, y, w, h);
        Place(_shadow, x - 1, y - 1, w + 2, h + 2);

        // Rule of thirds.
        for (var i = 0; i < 2; i++)
        {
            var gx = x + w * (i + 1) / 3;
            var gy = y + h * (i + 1) / 3;
            SetLine(_grid[i], gx, y, gx, y + h);
            SetLine(_grid[i + 2], x, gy, x + w, gy);
        }

        // Corner brackets, drawn just inside the border so they stay visible at the picture's edge.
        var arm = Math.Max(4, Math.Min(18, Math.Min(w, h) / 3));
        const double inset = 1.5;
        double l = x + inset, t = y + inset, r = x + w - inset, b = y + h - inset;
        SetBracket(_brackets[0], new Point(l, t + arm), new Point(l, t), new Point(l + arm, t));
        SetBracket(_brackets[1], new Point(r - arm, t), new Point(r, t), new Point(r, t + arm));
        SetBracket(_brackets[2], new Point(r, b - arm), new Point(r, b), new Point(r - arm, b));
        SetBracket(_brackets[3], new Point(l + arm, b), new Point(l, b), new Point(l, b - arm));
    }

    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        e.Width = Math.Max(0, w);
        e.Height = Math.Max(0, h);
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }

    private static void SetBracket(Polyline p, Point a, Point b, Point c) => p.Points = [a, b, c];

    // ---- Pointer ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_model is null) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus(FocusState.Pointer);

        // Fingers are blunter than mice.
        var tolerance = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch ? GrabToleranceDip * 1.6 : GrabToleranceDip;
        var handle = _model.HitTest(point.Position.X, point.Position.Y, tolerance);
        if (handle == CropHandle.None) return;

        _model.BeginDrag(handle);
        _dragOrigin = point.Position;
        _dragging = true;
        CapturePointer(e.Pointer);
        SetCursor(handle);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_model is null) return;
        var p = e.GetCurrentPoint(this).Position;
        if (_dragging)
        {
            _model.DragBy(p.X - _dragOrigin.X, p.Y - _dragOrigin.Y);
            UserEdited?.Invoke();
        }
        else
        {
            SetCursor(_model.HitTest(p.X, p.Y, GrabToleranceDip));
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _model?.EndDrag();
    }

    private void SetCursor(CropHandle handle)
    {
        if (handle == _hover) return;
        _hover = handle;
        var shape = handle switch
        {
            CropHandle.TopLeft or CropHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
            CropHandle.TopRight or CropHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
            CropHandle.Top or CropHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
            CropHandle.Left or CropHandle.Right => InputSystemCursorShape.SizeWestEast,
            CropHandle.Move => InputSystemCursorShape.SizeAll,
            _ => InputSystemCursorShape.Arrow,
        };
        ProtectedCursor = InputSystemCursor.Create(shape);
    }

    // ---- Keyboard ----

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_model is null) return;
        var step = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 ? 10 : 1;
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case VirtualKey.Left: dx = -step; break;
            case VirtualKey.Right: dx = step; break;
            case VirtualKey.Up: dy = -step; break;
            case VirtualKey.Down: dy = step; break;
            default: return;
        }

        _model.Nudge(dx, dy);
        UserEdited?.Invoke();
        e.Handled = true;
    }
}
