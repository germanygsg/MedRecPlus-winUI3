using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MedRecPlus.App.Views;

public sealed partial class UnlockPage : Page
{
    public UnlockPage() => InitializeComponent();

    private void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            TryUnlock();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    /// <summary>Emergency unlock: after explicit typed confirmation, wipes the
    /// encrypted database, attachments, and backups, then returns to setup.
    /// There is no recovery — this is the only way back in when the password
    /// is lost, because the database cannot be decrypted without it.</summary>
    private async void EmergencyUnlock_Click(object sender, RoutedEventArgs e)
    {
        var confirmBox = new TextBox
        {
            PlaceholderText = "Type WIPE to confirm",
            Margin = new Thickness(0, 12, 0, 0),
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "This will PERMANENTLY delete everything: all patients, visits, " +
                   "invoices, attachments, and backups. The app will restart as if " +
                   "newly installed. This cannot be undone.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(confirmBox);

        var dialog = new ContentDialog
        {
            Title = "Emergency unlock — wipe all data?",
            Content = content,
            PrimaryButtonText = "Wipe everything",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        dialog.IsPrimaryButtonEnabled = false;
        confirmBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled =
                confirmBox.Text.Trim().Equals("WIPE", StringComparison.OrdinalIgnoreCase);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            AppData.WipeAllData();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Frame?.Navigate(typeof(SetupPage));
    }

    private void TryUnlock()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ErrorText.Text = "Enter your password.";
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Focus(FocusState.Programmatic);
            return;
        }
        try
        {
            var db = AppData.Unlock(PasswordBox.Password);
            if (db == null)
            {
                ErrorText.Text = "Wrong password.";
                ErrorText.Visibility = Visibility.Visible;
                PasswordBox.Password = "";
                PasswordBox.Focus(FocusState.Programmatic);
                return;
            }
            // Actor: first operator's name (single-user v1).
            var actor = new MedRecPlus.Core.Data.OperatorRepository(db)
                .GetAll().FirstOrDefault()?.Name ?? "operator";
            AppSession.Start(db, actor);
            App.Shell?.EnterShell();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
