using System.Collections.ObjectModel;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Printing;

namespace MedRecPlus.App.Views;

public sealed partial class InvoiceDetailPage : Page
{
    private long _invoiceId;
    private Invoice _invoice = null!;
    private InvoiceReceipt? _receipt;
    private InvoicePrinter? _printer;
    private bool _printing;
    private PrintPageDescription _paperDescription = InvoicePaperLayout.DefaultPageDescription;
    public ObservableCollection<PaymentRow> Payments { get; } = new();

    public InvoiceDetailPage()
    {
        InitializeComponent();
        PaymentList.ItemsSource = Payments;
        Loaded += (_, _) => RenderReceiptPreview(_paperDescription);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _invoiceId = (long)e.Parameter;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _printer?.Dispose();
        _printer = null;
        _printing = false;
        base.OnNavigatedFrom(e);
    }

    private void Refresh()
    {
        _invoice = AppSession.Invoices.GetInvoice(_invoiceId);
        var clinic = AppSession.Settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());

        ReceiptPanel.Children.Clear();
        _receipt = null;
        PrintButton.IsEnabled = false;
        try
        {
            _receipt = InvoiceReceipt.Create(_invoice,
                AppSession.Patients.GetById(_invoice.PatientId)?.RecordNumber, clinic);
            RenderReceiptPreview(_paperDescription);
            PrintButton.IsEnabled = !_printing;
        }
        catch (Exception)
        {
            ReceiptPanel.Children.Add(InvoiceReceipt.Text(
                "The saved invoice receipt could not be loaded. Printing is unavailable."));
        }

        Payments.Clear();
        foreach (var p in AppSession.Invoices.GetPayments(_invoiceId))
            Payments.Add(new PaymentRow
            {
                Date = UiHelpers.Date(p.PaidAt), Method = p.Method,
                Amount = UiHelpers.Money(p.AmountCents),
            });
    }

    internal void RenderReceiptPreview(PrintPageDescription description)
    {
        if (_receipt == null) return;
        _paperDescription = description;
        InvoicePaperLayout.ShowPreview(_receipt, ReceiptPanel, description);
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

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_printing || _receipt == null) return;
        _printing = true;
        PrintButton.IsEnabled = false;
        try
        {
            if (!PrintManager.IsSupported())
            {
                Dialogs.Alert(XamlRoot, "Printing unavailable", "Printing is not supported on this device.");
                PrintCompleted(PrintTaskCompletion.Canceled);
                return;
            }
            _printer ??= new InvoicePrinter(App.Shell!, PrintCanvas, PrintCompleted, RenderReceiptPreview);
            if (!await _printer.ShowAsync(_receipt)) PrintCompleted(PrintTaskCompletion.Canceled);
        }
        catch (Exception)
        {
            PrintCompleted(PrintTaskCompletion.Failed);
        }
    }

    private void PrintCompleted(PrintTaskCompletion completion)
    {
        _printing = false;
        PrintButton.IsEnabled = _receipt != null;
        if (completion == PrintTaskCompletion.Failed)
            Dialogs.Alert(XamlRoot, "Printing failed", "The invoice could not be printed. Check your printer and try again.");
        else if (completion == PrintTaskCompletion.Submitted)
        {
            try
            {
                AppSession.Audit.Append(AuditEvents.InvoicePrinted, "invoice", _invoiceId.ToString(), AppSession.Actor);
            }
            catch (Exception)
            {
                Dialogs.Alert(XamlRoot, "Print audit unavailable",
                    "The print job was submitted, but its audit entry could not be saved.");
            }
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

    public sealed class PaymentRow
    {
        public string Date { get; set; } = "";
        public string Method { get; set; } = "";
        public string Amount { get; set; } = "";
    }
}
