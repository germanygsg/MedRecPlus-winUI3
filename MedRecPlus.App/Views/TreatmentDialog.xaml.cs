using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MedRecPlus.Core.Services;

namespace MedRecPlus.App.Views;

public sealed partial class TreatmentDialog : ContentDialog
{
    private readonly FormValidation _validation = new();
    public TreatmentDialog()
    {
        InitializeComponent();
        _validation.Add(NameBox, NameBoxError, () => FormValidation.Required(NameBox.Text, "Treatment name"));
        _validation.Add(PriceBox, PriceBoxError, () => RupiahAmount.TryParseCents(PriceBox.Text, out _) ? null
            : "Enter a whole-rupiah price of at least Rp 1 (e.g. 300000).");
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ErrorText.Visibility = Visibility.Collapsed;
        if (!_validation.Validate()) return;
        try
        {
            var name = NameBox.Text.Trim();
            RupiahAmount.TryParseCents(PriceBox.Text, out var priceCents);
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
