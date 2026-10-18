using System.Globalization;
using MedRecPlus.Core.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace MedRecPlus.App;

/// <summary>A native, resize-aware daily area chart. Curves stay between adjacent data values.</summary>
public sealed class DashboardChart : Grid
{
    private readonly Canvas _plot = new() { Height = 148, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Canvas _labels = new() { Height = 26, IsHitTestVisible = false };
    private IReadOnlyList<DashboardDay> _days = [];
    private Color _color;
    private bool _money;
    private const double Left = 52, Right = 20, Top = 10, Bottom = 132;

    public DashboardChart()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_plot);
        SetRow(_labels, 1);
        Children.Add(_labels);
        SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) => Draw();
        _plot.PointerMoved += ShowValue;
        _plot.PointerExited += (_, _) => { if (ToolTipService.GetToolTip(_plot) is ToolTip tip) tip.IsOpen = false; };
    }

    public void SetData(IReadOnlyList<DashboardDay> days, Color color, bool money = false)
    {
        _days = days;
        _color = color;
        _money = money;
        AutomationProperties.SetName(this, "Daily trend. " + string.Join("; ", days.Select(d => $"{d.Date:dd MMM}: {Format(d.Value)}")));
        Draw();
    }

    private string Format(long value) => _money ? UiHelpers.Money(value) : value.ToString("N0");

    private void ShowValue(object sender, PointerRoutedEventArgs e)
    {
        if (_days.Count == 0) return;
        var width = Math.Max(1, ActualWidth - Left - Right);
        var index = (int)Math.Clamp(Math.Round((e.GetCurrentPoint(_plot).Position.X - Left) / width * (_days.Count - 1)), 0, _days.Count - 1);
        var day = _days[index];
        if (ToolTipService.GetToolTip(_plot) is not ToolTip tip)
        {
            tip = new ToolTip();
            ToolTipService.SetToolTip(_plot, tip);
        }
        tip.Content = $"{day.Date:ddd, dd MMM yyyy} · {Format(day.Value)}";
        tip.IsOpen = true;
    }

    private void Draw()
    {
        _plot.Children.Clear();
        _labels.Children.Clear();
        if (_days.Count == 0 || ActualWidth < 100) return;
        var width = Math.Max(1, ActualWidth - Left - Right);
        // Application resources resolve against the app theme, which can differ
        // from this page's requested theme. Use the chart's effective theme here.
        var dark = ActualTheme == ElementTheme.Dark;
        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var foreground = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : dark ? Color.FromArgb(255, 195, 192, 200) : Color.FromArgb(255, 104, 101, 112);
        var textBrush = new SolidColorBrush(foreground);
        var gridBrush = new SolidColorBrush(highContrast ? foreground
            : dark ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        var stroke = highContrast ? foreground : _color;
        var maximum = _days.Max(d => d.Value);
        // Whole-number ticks for counts, compact rupiah ticks for money.
        var rawStep = Math.Max(_money ? 100d : 1d, maximum / 3d);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = new[] { 1d, 2d, 2.5d, 5d, 10d }.Select(n => n * magnitude).First(n => n >= rawStep);
        if (!_money) step = Math.Ceiling(step);
        var ceiling = step * 3;
        for (var i = 0; i <= 3; i++)
        {
            var y = Bottom - (Bottom - Top) * i / 3;
            _plot.Children.Add(new Line
            {
                X1 = Left, X2 = Left + width, Y1 = y, Y2 = y, Stroke = gridBrush,
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 4 },
            });
            var value = step * i / (_money ? 100 : 1);
            var label = new TextBlock
            {
                Text = Compact(value), FontSize = 10, Foreground = textBrush, Width = Left - 10,
                TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetTop(label, y - 7); _plot.Children.Add(label);
        }

        var points = _days.Select((day, i) => new Point(Left + width * i / Math.Max(1, _days.Count - 1),
            Bottom - day.Value / ceiling * (Bottom - Top))).ToArray();
        var lineFigure = Figure(points);
        var areaFigure = Figure(points);
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, Bottom) });
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[0].X, Bottom) });
        areaFigure.IsClosed = true;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(48, stroke.R, stroke.G, stroke.B), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(2, stroke.R, stroke.G, stroke.B), Offset = 1 });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = new PathFigureCollection { areaFigure } }, Fill = fill,
        });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = new PathFigureCollection { lineFigure } },
            Stroke = new SolidColorBrush(stroke), StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        if (_days.Count <= 7)
            foreach (var point in points)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(stroke) };
                Canvas.SetLeft(dot, point.X - 2.5); Canvas.SetTop(dot, point.Y - 2.5); _plot.Children.Add(dot);
            }

        var labelCount = Math.Min(_days.Count, Math.Max(2, (int)(width / 68) + 1));
        for (var j = 0; j < labelCount; j++)
        {
            var index = (int)Math.Round(j * (_days.Count - 1d) / Math.Max(1, labelCount - 1));
            var label = new TextBlock
            {
                Text = _days[index].Date.ToString(_days.Count <= 7 ? "dd ddd" : "dd MMM", CultureInfo.CurrentCulture),
                FontSize = 10, Foreground = textBrush, Width = 60, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetLeft(label, Math.Clamp(points[index].X - 30, 0, ActualWidth - 60));
            Canvas.SetTop(label, 7); _labels.Children.Add(label);
        }
    }

    private static PathFigure Figure(Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false };
        for (var i = 1; i < points.Length; i++)
        {
            var a = points[i - 1]; var b = points[i]; var control = (b.X - a.X) * .35;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(a.X + control, a.Y), Point2 = new Point(b.X - control, b.Y), Point3 = b,
            });
        }
        return figure;
    }

    private static string Compact(double value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.#") + "B",
        >= 1_000_000 => (value / 1_000_000).ToString("0.#") + "M",
        >= 1_000 => (value / 1_000).ToString("0.#") + "K",
        _ => value.ToString("0.#"),
    };
}
