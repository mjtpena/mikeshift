using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace MikeShift.App.FirstRun;

/// <summary>
/// A picture of the wheel for the welcome window: six format tiles around a hub, one highlighted, and a file being
/// dragged onto it. Drawn from XAML shapes with theme brushes, so it is sharp at any scale and follows light, dark and high contrast.
/// It illustrates the idea; the real wheel is drawn by Win2D.
/// </summary>
public sealed class WheelIllustration : UserControl
{
    private const double Size = 260;
    private const double Outer = 118;
    private const double Inner = 50;
    private const double GapDegrees = 3.2;
    private static readonly string[] Labels = ["PNG", "JPG", "HEIC", "WebP", "PDF", "TIFF"];

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };

    public WheelIllustration()
    {
        Width = Size;
        Height = Size;
        Content = _canvas;
        Loaded += (_, _) => Rebuild();
        ActualThemeChanged += (_, _) => Rebuild();
        AutomationProperties_Name();
    }

    private void AutomationProperties_Name()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Picture of the MikeShift wheel: a ring of format tiles with PNG highlighted");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void Rebuild()
    {
        _canvas.Children.Clear();
        var c = Size / 2;

        // A soft disc behind the tiles (the glass of the real wheel).
        _canvas.Children.Add(new Ellipse
        {
            Width = Outer * 2 + 14, Height = Outer * 2 + 14,
            Fill = Res("CardBackgroundFillColorSecondaryBrush"),
            Stroke = Res("CardStrokeColorDefaultBrush"), StrokeThickness = 1,
        });
        Canvas.SetLeft(_canvas.Children[^1], c - Outer - 7);
        Canvas.SetTop(_canvas.Children[^1], c - Outer - 7);

        for (var i = 0; i < Labels.Length; i++)
        {
            var highlighted = i == 0;
            var start = -90 + i * 60 - 30 + GapDegrees;
            var end = start + 60 - 2 * GapDegrees;

            var wedge = new Path
            {
                Data = Wedge(c, c, Inner, Outer, start, end),
                Fill = highlighted ? Res("AccentFillColorDefaultBrush") : Res("ControlFillColorDefaultBrush"),
                Stroke = highlighted ? Res("AccentFillColorDefaultBrush") : Res("ControlStrokeColorDefaultBrush"),
                StrokeThickness = 1,
            };
            _canvas.Children.Add(wedge);

            var mid = (start + end) / 2 * Math.PI / 180;
            var r = (Inner + Outer) / 2;
            var label = new TextBlock
            {
                Text = Labels[i],
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = highlighted ? Res("TextOnAccentFillColorPrimaryBrush") : Res("TextFillColorPrimaryBrush"),
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, c + r * Math.Cos(mid) - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, c + r * Math.Sin(mid) - label.DesiredSize.Height / 2);
            _canvas.Children.Add(label);
        }

        // The hub says what dropping does.
        var hub = new Ellipse { Width = Inner * 2 - 14, Height = Inner * 2 - 14, Fill = Res("ControlFillColorSecondaryBrush") };
        Canvas.SetLeft(hub, c - Inner + 7);
        Canvas.SetTop(hub, c - Inner + 7);
        _canvas.Children.Add(hub);
        var hubText = new TextBlock
        {
            Text = "Convert to\nPNG",
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextFillColorPrimaryBrush"),
        };
        hubText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(hubText, c - hubText.DesiredSize.Width / 2);
        Canvas.SetTop(hubText, c - hubText.DesiredSize.Height / 2);
        _canvas.Children.Add(hubText);

        // The dragged file: a small card with its name, tilted toward the highlighted tile.
        var file = new Border
        {
            Background = Res("SolidBackgroundFillColorBaseBrush"),
            BorderBrush = Res("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 10, 4),
            RenderTransform = new RotateTransform { Angle = -6 },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, Foreground = Res("AccentTextFillColorPrimaryBrush") },
                    new TextBlock { Text = "photo.heic", FontSize = 12, Foreground = Res("TextFillColorPrimaryBrush") },
                },
            },
        };
        Canvas.SetLeft(file, c + 18);
        Canvas.SetTop(file, 6);
        _canvas.Children.Add(file);
    }

    /// <summary>A ring segment between two radii and two angles (degrees, clockwise from 3 o'clock).</summary>
    private static Geometry Wedge(double cx, double cy, double inner, double outer, double startDeg, double endDeg)
    {
        Point P(double r, double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = P(outer, startDeg), IsClosed = true };
        figure.Segments.Add(new ArcSegment { Point = P(outer, endDeg), Size = new Size(outer, outer), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = P(inner, endDeg) });
        figure.Segments.Add(new ArcSegment { Point = P(inner, startDeg), Size = new Size(inner, inner), SweepDirection = SweepDirection.Counterclockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
