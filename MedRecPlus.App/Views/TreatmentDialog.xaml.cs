using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MedRecPlus.Core.Services;

namespace MedRecPlus.App.Views;

public sealed partial class TreatmentDialog : ContentDialog
{
    public TreatmentDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        try
        {
            var name = NameBox.Text.Trim();
            if (name.Length == 0) throw new InvalidOperationException("Name is required.");
            if (!RupiahAmount.TryParseCents(PriceBox.Text, out var priceCents))
                throw new InvalidOperationException("Enter a whole-rupiah price of at least Rp 1 (e.g. 300000).");
            AppSession.Treatments.Create(name, DescBox.Text.Trim(),
                priceCents, null);
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
