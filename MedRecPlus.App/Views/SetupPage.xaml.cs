using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class SetupPage : Page
{
    private readonly FormValidation _validation = new();
    public SetupPage()
    {
        InitializeComponent();
        _validation.Add(ClinicNameBox, ClinicNameBoxError, () => FormValidation.Required(ClinicNameBox.Text, "Clinic name"));
        _validation.Add(OperatorNameBox, OperatorNameBoxError, () => FormValidation.Required(OperatorNameBox.Text, "Operator name"));
        _validation.Add(PasswordBox, PasswordBoxError, () => FormValidation.Password(PasswordBox.Password));
        _validation.Add(ConfirmBox, ConfirmBoxError, () => string.IsNullOrEmpty(ConfirmBox.Password) ? "Confirm your password."
            : ConfirmBox.Password != PasswordBox.Password ? "Passwords do not match." : null);
        PasswordBox.PasswordChanged += (_, _) => _validation.Recheck(ConfirmBox);
    }

    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (!_validation.Validate()) return;
        try
        {
            var clinic = ClinicNameBox.Text.Trim();
            var op = OperatorNameBox.Text.Trim();
            var pw = PasswordBox.Password;
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
