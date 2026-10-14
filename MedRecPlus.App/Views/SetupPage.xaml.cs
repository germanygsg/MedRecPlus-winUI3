using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupPage() => InitializeComponent();

    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        try
        {
            var clinic = ClinicNameBox.Text.Trim();
            var op = OperatorNameBox.Text.Trim();
            var pw = PasswordBox.Password;
            if (pw != ConfirmBox.Password)
                throw new InvalidOperationException("Passwords do not match.");
            var db = AppData.Setup(pw, clinic, op);
            AppSession.Start(db, op);
            App.Shell?.EnterShell();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
