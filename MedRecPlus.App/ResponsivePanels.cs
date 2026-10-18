using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MedRecPlus.App;

/// <summary>Equal-width cells that move into additional rows as the content area narrows.</summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(AdaptiveGrid), new PropertyMetadata(2, Changed));
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth), typeof(double), typeof(AdaptiveGrid), new PropertyMetadata(150d, Changed));
    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(AdaptiveGrid), new PropertyMetadata(12d, Changed));
    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(AdaptiveGrid), new PropertyMetadata(8d, Changed));
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(AdaptiveGrid), new PropertyMetadata(new Thickness(0), Changed));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double MinimumColumnWidth { get => (double)GetValue(MinimumColumnWidthProperty); set => SetValue(MinimumColumnWidthProperty, value); }
    public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => (double)GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((AdaptiveGrid)sender).InvalidateMeasure();

    private Size Layout(double availableWidth, bool arrange)
    {
        var children = Children.Where(child => child.Visibility != Visibility.Collapsed).ToArray();
        if (children.Length == 0) return new Size(0, 0);
        var padding = Padding;
        var gap = Math.Max(0, ColumnSpacing);
        var width = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth - padding.Left - padding.Right)
            : Math.Max(1, Columns) * Math.Max(1, MinimumColumnWidth) + (Math.Max(1, Columns) - 1) * gap;
        var columns = Math.Max(1, Math.Min(Math.Min(children.Length, Columns),
            (int)Math.Floor((width + gap) / (Math.Max(1, MinimumColumnWidth) + gap))));
        var cellWidth = Math.Max(0, (width - (columns - 1) * gap) / columns);
        var y = padding.Top;
        for (var start = 0; start < children.Length; start += columns)
        {
            var count = Math.Min(columns, children.Length - start);
            var height = 0d;
            for (var column = 0; column < count; column++)
            {
                var child = children[start + column];
                child.Measure(new Size(cellWidth, double.PositiveInfinity));
                height = Math.Max(height, child.DesiredSize.Height);
            }
            if (arrange)
                for (var column = 0; column < count; column++)
                    children[start + column].Arrange(new Rect(padding.Left + column * (cellWidth + gap), y, cellWidth, height));
            y += height + Math.Max(0, RowSpacing);
        }
        return new Size(width + padding.Left + padding.Right, y - Math.Max(0, RowSpacing) + padding.Bottom);
    }
    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, false);
    protected override Size ArrangeOverride(Size finalSize) { Layout(finalSize.Width, true); return finalSize; }
}

/// <summary>Toolbars and chips wrap as whole items, keeping every action reachable.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(8d,
            (sender, _) => ((WrapPanel)sender).InvalidateMeasure()));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private Size Layout(double availableWidth, bool arrange)
    {
        var width = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : double.PositiveInfinity;
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        foreach (var child in Children.Where(child => child.Visibility != Visibility.Collapsed))
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var childWidth = Math.Min(width, child.DesiredSize.Width);
            if (x > 0 && x + childWidth > width + 0.1)
            {
                x = 0; y += rowHeight + Math.Max(0, Spacing); rowHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, childWidth, child.DesiredSize.Height));
            usedWidth = Math.Max(usedWidth, x + childWidth);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            x += childWidth + Math.Max(0, Spacing);
        }
        return new Size(usedWidth, y + rowHeight);
    }
    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, false);
    protected override Size ArrangeOverride(Size finalSize) { Layout(finalSize.Width, true); return finalSize; }
}
