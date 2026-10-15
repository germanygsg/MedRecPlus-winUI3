using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.Printing;

namespace MedRecPlus.App;

/// <summary>One physical page layout for the invoice preview and printer.</summary>
internal static class InvoicePaperLayout
{
    internal const double PaperWidthMm = 120;
    internal const double PaperHeightMm = 138;
    internal static Size PaperSize => new(PaperWidthMm * 96 / 25.4, PaperHeightMm * 96 / 25.4);
    internal static PrintPageDescription DefaultPageDescription => new()
    {
        PageSize = PaperSize,
        ImageableRect = new Rect(0, 0, PaperSize.Width, PaperSize.Height),
    };

    // The receipt always uses 120 x 138 mm. Keep the selected driver's physical
    // margins. Screen and print previews share this pagination and geometry.
    internal static IReadOnlyList<FrameworkElement> BuildPages(InvoiceReceipt receipt, Panel host, PrintPageDescription description)
    {
        var pages = new List<FrameworkElement>();
        host.Children.Clear();
        var paper = PaperSize;
        var area = description.ImageableRect;
        var margin = Math.Min(24, paper.Width * 0.03);
        // Round inward: XAML's pixel rounding must never expand content beyond
        // a driver's fractional printable bounds.
        var left = Math.Ceiling(Math.Max(margin, area.Left));
        var top = Math.Ceiling(Math.Max(margin, area.Top));
        var right = Math.Ceiling(Math.Max(margin, description.PageSize.Width - area.Right));
        var bottom = Math.Ceiling(Math.Max(margin, description.PageSize.Height - area.Bottom));
        var width = Math.Floor(paper.Width - left - right);
        var height = Math.Floor(paper.Height - top - bottom - 28); // page identification
        if (width <= 32 || height <= 32)
            throw new InvalidOperationException("The selected paper is too small for this invoice.");

        StackPanel? body = null;
        double used = 0;
        foreach (var original in receipt.CreateBlocks())
        {
            FrameworkElement block = original;
            // Measure while rooted so theme and text metrics match the preview.
            host.Children.Add(block);
            block.Width = width;
            block.Measure(new Size(width, double.PositiveInfinity));
            var blockHeight = block.DesiredSize.Height;
            host.Children.Remove(block);
            if (body == null || used + blockHeight > height + 0.01)
            {
                body = NewPage(host, pages, paper, left, top, width, height);
                used = 0;
            }
            if (blockHeight > height)
            {
                // An unusually tall individual block (e.g. a long clinic footer)
                // must remain complete even on small paper.
                block = new Viewbox
                {
                    Child = original, Width = width, Height = height,
                    Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                blockHeight = height;
            }
            body.Children.Add(block);
            used += blockHeight;
        }

        for (var index = 0; index < pages.Count; index++)
        {
            var page = (Grid)pages[index];
            var footer = InvoiceReceipt.Text($"#{receipt.Number} · Page {index + 1} of {pages.Count}", 11);
            footer.Width = width;
            footer.Margin = new Thickness(left, top + height + 8, 0, 0);
            footer.HorizontalAlignment = HorizontalAlignment.Left;
            footer.VerticalAlignment = VerticalAlignment.Top;
            page.Children.Add(footer);
            page.Measure(paper);
            page.Arrange(new Rect(0, 0, paper.Width, paper.Height));
        }
        host.UpdateLayout();
        return pages;
    }

    private static StackPanel NewPage(Panel host, List<FrameworkElement> pages, Size paper, double left, double top, double width, double height)
    {
        var page = new Grid
        {
            Width = paper.Width, Height = paper.Height,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            RequestedTheme = ElementTheme.Light,
        };
        var body = new StackPanel
        {
            Width = width, Height = height,
            Margin = new Thickness(left, top, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        page.Children.Add(body);
        pages.Add(page);
        host.Children.Add(page);
        return body;
    }
}
