using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MikeShift.Core.Editing;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace MikeShift.App.Editors;

/// <summary>
/// A two-thumb range slider over a track that can show a strip of video frames (WinUI has none). The start thumb sits
/// left of the selection and the end thumb right of it, like the trim handles of a video editor. The rules (clamping,
/// minimum length) are <see cref="TimeRangeModel"/>'s.
/// <para>Keyboard (thumb focused): Left/Right 0.1 s, Shift 1 s, Ctrl 10 s, Home/End to the ends of what is allowed.</para>
/// </summary>
internal sealed class RangeSlider : UserControl
{
    private const double ThumbWidth = 16;
    private const double TrackHeight = 64;

    private enum Which { None, Start, End }

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0));

    private readonly Grid _strip = new() { IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle _dimLeft = new() { Fill = DimBrush };
    private readonly Rectangle _dimRight = new() { Fill = DimBrush };
    private readonly Rectangle _frame = Xaml<Rectangle>(
        "<Rectangle xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' StrokeThickness='3' Stroke='{ThemeResource AccentFillColorDefaultBrush}' />");
    private readonly ThumbControl _startThumb;
    private readonly ThumbControl _endThumb;

    private TimeRangeModel? _model;
    private Which _drag;
    private double _grabOffset;

    public RangeSlider()
    {
        Height = TrackHeight;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _startThumb = new ThumbControl(isStart: true, "Trim start");
        _endThumb = new ThumbControl(isStart: false, "Trim end");
        _startThumb.KeyDown += (_, e) => OnThumbKey(Which.Start, e);
        _endThumb.KeyDown += (_, e) => OnThumbKey(Which.End, e);

        var track = Xaml<Border>(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' CornerRadius='6' Margin='16,0,16,0' Background='{ThemeResource ControlAltFillColorTertiaryBrush}' />");
        track.Child = _strip;

        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        root.Children.Add(track);
        root.Children.Add(_overlay);
        foreach (UIElement e in new UIElement[] { _dimLeft, _dimRight, _frame, _startThumb, _endThumb }) _overlay.Children.Add(e);
        Content = root;

        root.PointerPressed += OnPointerPressed;
        root.PointerMoved += OnPointerMoved;
        root.PointerReleased += (_, e) => { root.ReleasePointerCapture(e.Pointer); _drag = Which.None; };
        root.PointerCaptureLost += (_, _) => _drag = Which.None;
        SizeChanged += (_, _) => Layout();
    }

    /// <summary>Raised after the user moved a thumb.</summary>
    public event Action? UserEdited;

    /// <summary>Binds the slider to a model (redraws on every change).</summary>
    public void Attach(TimeRangeModel model)
    {
        if (_model is not null) _model.Changed -= Layout;
        _model = model;
        model.Changed += Layout;
        Layout();
    }

    /// <summary>Shows the frames across the track; an empty list leaves a plain track.</summary>
    public void SetThumbnails(IReadOnlyList<ImageSource> images)
    {
        _strip.Children.Clear();
        _strip.ColumnDefinitions.Clear();
        for (var i = 0; i < images.Count; i++)
        {
            _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var image = new Image { Source = images[i], Stretch = Stretch.UniformToFill };
            Grid.SetColumn(image, i);
            _strip.Children.Add(image);
        }
    }

    /// <summary>Focuses the start thumb.</summary>
    public void FocusStart() => _startThumb.Focus(FocusState.Programmatic);

    // ---- Geometry ----

    private double Usable => Math.Max(1, ActualWidth - 2 * ThumbWidth);

    private double XOf(TimeSpan t) =>
        _model is null || _model.Duration <= TimeSpan.Zero ? ThumbWidth : ThumbWidth + t.TotalSeconds / _model.Duration.TotalSeconds * Usable;

    private TimeSpan TimeAt(double selectionEdgeX)
    {
        if (_model is null) return TimeSpan.Zero;
        var fraction = Math.Clamp((selectionEdgeX - ThumbWidth) / Usable, 0, 1);
        return TimeSpan.FromSeconds(fraction * _model.Duration.TotalSeconds);
    }

    private void Layout()
    {
        if (_model is null || ActualWidth <= 0) return;
        var xs = XOf(_model.Start);
        var xe = XOf(_model.End);
        var w = ActualWidth;

        Place(_dimLeft, ThumbWidth, 0, Math.Max(0, xs - ThumbWidth), TrackHeight);
        Place(_dimRight, xe, 0, Math.Max(0, w - ThumbWidth - xe), TrackHeight);
        Place(_frame, xs, 0, Math.Max(0, xe - xs), TrackHeight);
        Place(_startThumb, xs - ThumbWidth, 0, ThumbWidth, TrackHeight);
        Place(_endThumb, xe, 0, ThumbWidth, TrackHeight);

        _startThumb.Describe($"Trim start, {TimeRangeModel.Format(_model.Start)}");
        _endThumb.Describe($"Trim end, {TimeRangeModel.Format(_model.End)}");
    }

    /// <summary>Builds an element from XAML so its <c>{ThemeResource}</c> brushes follow light/dark changes.</summary>
    private static T Xaml<T>(string xaml) where T : class => (T)XamlReader.Load(xaml);

    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        e.Width = w;
        e.Height = h;
    }

    // ---- Pointer ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_model is null || !e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed) return;
        var x = e.GetCurrentPoint(_overlay).Position.X;
        var xs = XOf(_model.Start);
        var xe = XOf(_model.End);

        if (x >= xs - ThumbWidth && x <= xs)
        {
            _drag = Which.Start;
            _grabOffset = x - xs;
        }
        else if (x >= xe && x <= xe + ThumbWidth)
        {
            _drag = Which.End;
            _grabOffset = x - xe;
        }
        else
        {
            // A click on the track grabs the nearer thumb and jumps it there.
            _drag = Math.Abs(x - xs) <= Math.Abs(x - xe) ? Which.Start : Which.End;
            _grabOffset = 0;
            Apply(x);
        }

        (_drag == Which.Start ? _startThumb : _endThumb).Focus(FocusState.Pointer);
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Which.None) return;
        Apply(e.GetCurrentPoint(_overlay).Position.X);
    }

    private void Apply(double pointerX)
    {
        if (_model is null) return;
        var t = TimeAt(pointerX - _grabOffset);
        if (_drag == Which.Start) _model.SetStart(t);
        else _model.SetEnd(t);
        UserEdited?.Invoke();
    }

    // ---- Keyboard ----

    private void OnThumbKey(Which which, KeyRoutedEventArgs e)
    {
        if (_model is null) return;
        var ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var step = TimeSpan.FromSeconds(ctrl ? 10 : shift ? 1 : 0.1);
        var current = which == Which.Start ? _model.Start : _model.End;

        TimeSpan target;
        switch (e.Key)
        {
            case VirtualKey.Left or VirtualKey.Down: target = current - step; break;
            case VirtualKey.Right or VirtualKey.Up: target = current + step; break;
            case VirtualKey.Home: target = TimeSpan.Zero; break;
            case VirtualKey.End: target = _model.Duration; break;
            default: return;
        }

        if (which == Which.Start) _model.SetStart(target);
        else _model.SetEnd(target);
        UserEdited?.Invoke();
        e.Handled = true;
    }

    /// <summary>A focusable handle: grip on a rounded accent bar.</summary>
    private sealed class ThumbControl : ContentControl
    {
        public ThumbControl(bool isStart, string name)
        {
            IsTabStop = true;
            UseSystemFocusVisuals = true;
            IsHitTestVisible = false; // the slider's root handles the pointer
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            AutomationProperties.SetName(this, name);
            AutomationProperties.SetHelpText(this, "Left and right arrow keys move it by a tenth of a second, Shift by a second, Ctrl by ten.");

            var corners = isStart ? "6,0,0,6" : "0,6,6,0";
            Content = Xaml<Border>(
                $"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' CornerRadius='{corners}' Background='{{ThemeResource AccentFillColorDefaultBrush}}'>" +
                "<Rectangle Width='3' Height='22' RadiusX='1.5' RadiusY='1.5' HorizontalAlignment='Center' VerticalAlignment='Center' Fill='{ThemeResource TextOnAccentFillColorPrimaryBrush}' />" +
                "</Border>");
        }

        public void Describe(string name) => AutomationProperties.SetName(this, name);
    }
}
