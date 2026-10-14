using System.Collections.ObjectModel;
using System.Text.Json;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class InvoiceDetailPage : Page
{
    private long _invoiceId;
    private Invoice _invoice = null!;
    public ObservableCollection<LineRow> Lines { get; } = new();
    public ObservableCollection<PaymentRow> Payments { get; } = new();

    public InvoiceDetailPage()
    {
        InitializeComponent();
        LineItems.ItemsSource = Lines;
        PaymentList.ItemsSource = Payments;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _invoiceId = (long)e.Parameter;
        Refresh();
    }

    private void Refresh()
    {
        _invoice = AppSession.Invoices.GetInvoice(_invoiceId);
        var clinic = AppSession.Settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());

        ClinicHeader.Text = string.IsNullOrWhiteSpace(clinic.HeaderText) ? clinic.Name : clinic.HeaderText;
        BillTo.Text = $"{_invoice.PatientName}\nRecord: {AppSession.Patients.GetById(_invoice.PatientId)?.RecordNumber}";
        InvoiceInfo.Text = $"#{_invoice.InvoiceNumber}\n{UiHelpers.Date(_invoice.Date)}\nStatus: {_invoice.Status.ToUpper()}";

        Lines.Clear();
        var items = JsonSerializer.Deserialize<List<TreatmentItem>>(_invoice.TreatmentsJson) ?? new();
        foreach (var t in items)
            Lines.Add(new LineRow { Name = t.Name, Price = UiHelpers.Money(t.PriceCents) });

        SubtotalText.Text = $"Subtotal: {UiHelpers.Money(_invoice.SubtotalCents)}";
        DiscountText.Text = _invoice.DiscountCents > 0 ? $"Discount: −{UiHelpers.Money(_invoice.DiscountCents)}" : "";
        DiscountText.Visibility = _invoice.DiscountCents > 0 ? Visibility.Visible : Visibility.Collapsed;
        TaxText.Text = (_invoice.TaxCents ?? 0) > 0 ? $"{_invoice.TaxLabel}: {UiHelpers.Money(_invoice.TaxCents!.Value)}" : "";
        TaxText.Visibility = (_invoice.TaxCents ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalText.Text = $"TOTAL: {UiHelpers.Money(_invoice.TotalCents)}";
        PaidText.Text = $"Paid: {UiHelpers.Money(_invoice.PaidCents)} · Outstanding: {UiHelpers.Money(_invoice.TotalCents - _invoice.PaidCents)}";

        OperatorInfo.Text = $"Operator: {_invoice.OperatorName} · Visit: {UiHelpers.Date(_invoice.AppointmentDate)}";
        ClinicFooter.Text = clinic.FooterText;

        Payments.Clear();
        foreach (var p in AppSession.Invoices.GetPayments(_invoiceId))
            Payments.Add(new PaymentRow
            {
                Date = UiHelpers.Date(p.PaidAt), Method = p.Method,
                Amount = UiHelpers.Money(p.AmountCents),
            });
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.Shell?.NavigateTo(typeof(InvoicesPage));

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (_invoice.Status == InvoiceStatuses.Void)
        {
            Dialogs.Alert(XamlRoot, "Void invoice", "Cannot take payment on a void invoice.");
            return;
        }
        var dlg = new PaymentDialog(_invoice) { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved") Refresh();
    }

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (_invoice.Status == InvoiceStatuses.Void) return;
        if (await Dialogs.Confirm(XamlRoot, "Void invoice?",
            $"Void invoice {_invoice.InvoiceNumber}? This is audited and cannot be undone.", "Void"))
        {
            try
            {
                AppSession.Invoices.SetStatus(_invoiceId, InvoiceStatuses.Void, AppSession.Actor);
                Refresh();
            }
            catch (Exception ex) { Dialogs.Alert(XamlRoot, "Void failed", ex.Message); }
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.Confirm(XamlRoot, "Delete invoice?", $"Delete invoice {_invoice.InvoiceNumber}?"))
        {
            AppSession.Invoices.DeleteInvoice(_invoiceId, AppSession.Actor);
            App.Shell?.NavigateTo(typeof(InvoicesPage));
        }
    }

    public sealed class LineRow
    {
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
    }

    public sealed class PaymentRow
    {
        public string Date { get; set; } = "";
        public string Method { get; set; } = "";
        public string Amount { get; set; } = "";
    }
}
