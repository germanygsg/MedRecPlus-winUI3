using System.Text.Json;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedRecPlus.App;

/// <summary>A display snapshot shared by the invoice screen and Windows printing.</summary>
internal sealed record InvoiceReceipt(
    string Number, string Header, string BillTo, string Info,
    IReadOnlyList<(string Name, string Price)> Lines,
    string Subtotal, string? Discount, string? Tax, string Total, string Paid,
    string Operator, string Footer)
{
    public static InvoiceReceipt Create(Invoice invoice, string? recordNumber, ClinicProfile legacyClinic)
    {
        // New invoices carry their issue-time clinic profile. Only legacy invoices
        // without a profile use current settings; a damaged saved profile fails closed.
        var clinic = legacyClinic;
        if (!string.IsNullOrWhiteSpace(invoice.DocumentProfileJson))
        {
            using var profile = JsonDocument.Parse(invoice.DocumentProfileJson);
            clinic = profile.RootElement.GetProperty("clinic").Deserialize<ClinicProfile>()
                ?? throw new InvalidOperationException("The saved invoice clinic profile is missing.");
        }
        var treatments = JsonSerializer.Deserialize<List<TreatmentItem>>(invoice.TreatmentsJson) ?? [];
        return new InvoiceReceipt(
            invoice.InvoiceNumber,
            string.IsNullOrWhiteSpace(clinic.HeaderText) ? clinic.Name : clinic.HeaderText,
            $"{invoice.PatientName}\nRecord: {recordNumber}",
            $"#{invoice.InvoiceNumber}\n{UiHelpers.Date(invoice.Date)}\nStatus: {invoice.Status.ToUpperInvariant()}",
            treatments.Select(t => (t.Name, UiHelpers.Money(t.PriceCents))).ToArray(),
            $"Subtotal: {UiHelpers.Money(invoice.SubtotalCents)}",
            invoice.DiscountCents > 0 ? $"Discount: −{UiHelpers.Money(invoice.DiscountCents)}" : null,
            (invoice.TaxCents ?? 0) > 0 ? $"{invoice.TaxLabel}: {UiHelpers.Money(invoice.TaxCents!.Value)}" : null,
            $"TOTAL: {UiHelpers.Money(invoice.TotalCents)}",
            $"Paid: {UiHelpers.Money(invoice.PaidCents)} · Outstanding: {UiHelpers.Money(invoice.TotalCents - invoice.PaidCents)}",
            $"Operator: {invoice.OperatorName} · Visit: {UiHelpers.Date(invoice.AppointmentDate)}",
            clinic.FooterText);
    }

    // Each block can move intact to the next printed page. No virtualized list:
    // all treatment rows exist, including rows scrolled off the invoice screen.
    public IEnumerable<FrameworkElement> CreateBlocks()
    {
        yield return Text(Header, 18, bold: true);
        var title = Text("TREATMENT RECEIPT", bold: true);
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 12);
        yield return title;

        var info = Columns();
        var billed = new StackPanel();
        billed.Children.Add(Text("Billed to", bold: true));
        billed.Children.Add(Text(BillTo));
        var details = new StackPanel();
        details.Children.Add(Text("Invoice info", bold: true));
        details.Children.Add(Text(Info));
        Grid.SetColumn(details, 1);
        info.Children.Add(billed);
        info.Children.Add(details);
        info.Margin = new Thickness(0, 0, 0, 12);
        yield return info;

        foreach (var line in Lines)
        {
            var row = Columns(autoPrice: true);
            row.Children.Add(Text(line.Name));
            var price = Text(line.Price);
            Grid.SetColumn(price, 1);
            row.Children.Add(price);
            yield return row;
        }

        var totals = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        totals.Children.Add(Right(Subtotal));
        if (Discount != null) totals.Children.Add(Right(Discount));
        if (Tax != null) totals.Children.Add(Right(Tax));
        totals.Children.Add(Right(Total, 16, bold: true));
        totals.Children.Add(Right(Paid));
        var op = Text(Operator);
        op.Margin = new Thickness(0, 8, 0, 4);
        totals.Children.Add(op);
        totals.Children.Add(Text(Footer));
        yield return totals;
    }

    internal static TextBlock Text(string value, double size = 14, bool bold = false) => new()
    {
        Text = value,
        FontFamily = (FontFamily)Application.Current.Resources["AppFontFamily"],
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock Right(string value, double size = 14, bool bold = false)
    {
        var text = Text(value, size, bold);
        text.TextAlignment = TextAlignment.Right;
        return text;
    }

    private static Grid Columns(bool autoPrice = false)
    {
        var grid = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = autoPrice ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
        });
        return grid;
    }
}
