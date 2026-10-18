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
    IReadOnlyList<ReceiptLine> Lines,
    string Subtotal, string? Discount, string? Tax, string Total, string Paid,
    string Operator, string Footer)
{
    public ReceiptLayout Layout { get; init; } = new();
    public string ClinicName { get; init; } = "";
    public bool UseLegacyAppearance { get; init; } = true;

    public static InvoiceReceipt Create(Invoice invoice, string? recordNumber, ClinicProfile legacyClinic)
    {
        // New invoices carry their issue-time clinic profile. Only legacy invoices
        // without a profile use current settings; a damaged saved profile fails closed.
        var clinic = legacyClinic;
        ReceiptLayout? layout = null;
        if (!string.IsNullOrWhiteSpace(invoice.DocumentProfileJson))
        {
            using var profile = JsonDocument.Parse(invoice.DocumentProfileJson);
            clinic = profile.RootElement.GetProperty("clinic").Deserialize<ClinicProfile>()
                ?? throw new InvalidOperationException("The saved invoice clinic profile is missing.");
            if (profile.RootElement.TryGetProperty("receipt_layout", out var savedLayout))
                layout = savedLayout.Deserialize<ReceiptLayout>()?.Normalize()
                    ?? throw new InvalidOperationException("The saved invoice receipt layout is missing.");
        }
        var treatments = JsonSerializer.Deserialize<List<TreatmentItem>>(invoice.TreatmentsJson) ?? [];
        return new InvoiceReceipt(
            invoice.InvoiceNumber,
            string.IsNullOrWhiteSpace(clinic.HeaderText) ? clinic.Name : clinic.HeaderText,
            $"{invoice.PatientName}\nRecord: {recordNumber}",
            $"#{invoice.InvoiceNumber}\n{UiHelpers.Date(invoice.Date)}\nStatus: {invoice.Status.ToUpperInvariant()}",
            treatments.Select(t => new ReceiptLine(t.Name, UiHelpers.Money(t.PriceCents), t.Notes)).ToArray(),
            $"Subtotal: {UiHelpers.Money(invoice.SubtotalCents)}",
            invoice.DiscountCents > 0 ? $"Discount: −{UiHelpers.Money(invoice.DiscountCents)}" : null,
            (invoice.TaxCents ?? 0) > 0 ? $"{invoice.TaxLabel}: {UiHelpers.Money(invoice.TaxCents!.Value)}" : null,
            $"TOTAL: {UiHelpers.Money(invoice.TotalCents)}",
            $"Paid: {UiHelpers.Money(invoice.PaidCents)} · Outstanding: {UiHelpers.Money(invoice.TotalCents - invoice.PaidCents)}",
            $"Operator: {invoice.OperatorName} · Visit: {UiHelpers.Date(invoice.AppointmentDate)}",
            clinic.FooterText)
        {
            Layout = layout ?? new ReceiptLayout(),
            ClinicName = clinic.Name,
            UseLegacyAppearance = layout == null,
        };
    }

    // Each block can move intact to the next printed page. No virtualized list:
    // all treatment rows exist, including rows scrolled off the invoice screen.
    public IEnumerable<FrameworkElement> CreateBlocks()
    {
        // Profiles issued before the designer existed retain their original
        // appearance, even after the current receipt settings change.
        if (UseLegacyAppearance) return CreateLegacyBlocks();
        return CreateCustomBlocks();
    }

    private IEnumerable<FrameworkElement> CreateCustomBlocks()
    {
        var layout = Layout.Normalize();
        TextBlock Label(string value, double size, bool bold = false) => StyledText(value, size, bold);
        TextBlock TotalLabel(string value, double size, bool bold = false)
        {
            var text = Label(value, size, bold);
            text.TextAlignment = TextAlignment.Right;
            return text;
        }
        Border Divider(string name) => new()
        {
            Name = name, Height = 1, Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Margin = new Thickness(0, 6, 0, 8),
        };

        if (layout.ShowClinicHeader)
        {
            var header = new StackPanel();
            var name = Label(string.IsNullOrWhiteSpace(ClinicName) ? Header : ClinicName, layout.ClinicHeaderSize, true);
            name.TextAlignment = TextAlignment.Center;
            header.Children.Add(name);
            if (!string.IsNullOrWhiteSpace(Header) && Header != ClinicName)
            {
                var address = Label(Header, layout.InfoSize);
                address.TextAlignment = TextAlignment.Center;
                header.Children.Add(address);
            }
            yield return header;
        }
        if (layout.ShowReceiptTitle)
        {
            var title = Label("TREATMENT RECEIPT", layout.ReceiptTitleSize, true);
            title.TextAlignment = TextAlignment.Center;
            title.Margin = new Thickness(0, 8, 0, 12);
            yield return title;
        }
        if (layout.HeaderDivider) yield return Divider("HeaderDivider");

        if (layout.ShowPatient || layout.ShowInvoiceInfo)
        {
            var info = Columns();
            if (layout.ShowPatient && layout.ShowInvoiceInfo)
            {
                info.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                info.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                info.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                info.ColumnDefinitions[1].Width = new GridLength(0);
                info.ColumnSpacing = 0;
            }
            if (layout.ShowPatient)
            {
                var billed = new StackPanel();
                billed.Children.Add(Label("Billed to", layout.InfoSize, true));
                billed.Children.Add(Label(BillTo, layout.InfoSize));
                info.Children.Add(billed);
            }
            if (layout.ShowInvoiceInfo)
            {
                var details = new StackPanel();
                details.Children.Add(Label("Invoice info", layout.InfoSize, true));
                details.Children.Add(Label(Info, layout.InfoSize));
                Grid.SetColumn(details, layout.ShowPatient ? 1 : 0);
                info.Children.Add(details);
            }
            info.Margin = new Thickness(0, 0, 0, 12);
            yield return info;
        }
        if (layout.DetailsDivider) yield return Divider("DetailsDivider");

        if (layout.ShowTreatments)
        {
            foreach (var line in Lines)
            {
                var treatment = new StackPanel();
                var row = Columns(autoPrice: true);
                row.Children.Add(Label(line.Name, layout.TreatmentSize));
                var price = Label(line.Price, layout.TreatmentSize);
                Grid.SetColumn(price, 1);
                row.Children.Add(price);
                treatment.Children.Add(row);
                if (layout.ShowTreatmentNotes && !string.IsNullOrWhiteSpace(line.Notes))
                {
                    var notes = Label("Notes: " + line.Notes, layout.TreatmentSize);
                    notes.Margin = new Thickness(10, 0, 0, 4);
                    treatment.Children.Add(notes);
                }
                yield return treatment;
            }
        }
        if (layout.TreatmentsDivider) yield return Divider("TreatmentsDivider");

        if (layout.ShowTotals || (layout.ShowTax && Tax != null))
        {
            var totals = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            if (layout.ShowTotals)
            {
                totals.Children.Add(TotalLabel(Subtotal, layout.InfoSize));
                if (Discount != null) totals.Children.Add(TotalLabel(Discount, layout.InfoSize));
            }
            if (layout.ShowTax && Tax != null) totals.Children.Add(TotalLabel(Tax, layout.InfoSize));
            if (layout.ShowTotals)
            {
                totals.Children.Add(TotalLabel(Total, layout.TotalSize, true));
                totals.Children.Add(TotalLabel(Paid, layout.InfoSize));
            }
            yield return totals;
        }
        if (layout.TotalDivider) yield return Divider("TotalDivider");
        if (layout.ShowOperator)
        {
            var op = Label(Operator, layout.InfoSize);
            op.Margin = new Thickness(0, 8, 0, 4);
            yield return op;
        }
        if (layout.FooterDivider) yield return Divider("FooterDivider");
        if (layout.ShowFooter && !string.IsNullOrWhiteSpace(Footer))
        {
            var footer = Label(Footer, layout.FooterSize);
            footer.TextAlignment = TextAlignment.Center;
            yield return footer;
        }
    }

    internal TextBlock StyledText(string value, double size, bool bold = false)
    {
        var layout = Layout.Normalize();
        var text = Text(value, size, bold);
        text.FontFamily = layout.Font switch
        {
            "vt323" => new FontFamily("ms-appx:///Assets/Fonts/VT323-Regular.ttf#VT323"),
            "courier" => new FontFamily("Courier New"),
            _ => (FontFamily)Application.Current.Resources["AppFontFamily"],
        };
        text.LineHeight = size * layout.LineSpacing;
        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        return text;
    }

    private IEnumerable<FrameworkElement> CreateLegacyBlocks()
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

internal sealed record ReceiptLine(string Name, string Price, string? Notes = null);
