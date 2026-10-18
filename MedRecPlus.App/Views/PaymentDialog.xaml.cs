using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class PaymentDialog : ContentDialog
{
    private readonly Invoice _invoice;
    private readonly FormValidation _validation = new();

    public PaymentDialog(Invoice invoice)
    {
        InitializeComponent();
        _invoice = invoice;
        var outstanding = invoice.TotalCents - invoice.PaidCents;
        BalanceText.Text = $"Outstanding: {UiHelpers.Money(outstanding)}";
        AmountBox.Value = outstanding / 100.0;
        _validation.Add(AmountBox, AmountBoxError, () =>
        {
            var amount = FormValidation.Number(AmountBox);
            return !double.IsFinite(amount) || amount <= 0 ? "Enter an amount greater than Rp 0."
                : amount > outstanding / 100.0 ? $"Amount exceeds the outstanding balance of {UiHelpers.Money(outstanding)}." : null;
        });
        _validation.Add(MethodBox, MethodBoxError, () => MethodBox.SelectedItem == null ? "Select a payment method." : null);
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ErrorText.Visibility = Visibility.Collapsed;
        if (!_validation.Validate()) return;
        try
        {
            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "cash";
            AppSession.Invoices.RecordPayment(_invoice.Id, (long)(FormValidation.Number(AmountBox) * 100),
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
