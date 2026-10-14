using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class PaymentDialog : ContentDialog
{
    private readonly Invoice _invoice;

    public PaymentDialog(Invoice invoice)
    {
        InitializeComponent();
        _invoice = invoice;
        var outstanding = invoice.TotalCents - invoice.PaidCents;
        BalanceText.Text = $"Outstanding: {UiHelpers.Money(outstanding)}";
        AmountBox.Value = outstanding / 100.0;
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        try
        {
            if (double.IsNaN(AmountBox.Value) || AmountBox.Value <= 0)
                throw new InvalidOperationException("Amount must be > 0.");
            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "cash";
            AppSession.Invoices.RecordPayment(_invoice.Id, (long)(AmountBox.Value * 100),
                method, NoteBox.Text.Trim(), AppSession.Actor);
            Tag = "saved";
            Hide();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
