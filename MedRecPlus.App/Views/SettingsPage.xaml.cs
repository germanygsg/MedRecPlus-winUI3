using System.Collections.ObjectModel;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class SettingsPage : Page
{
    public ObservableCollection<OpRow> Ops { get; } = new();
    public ObservableCollection<TrRow> Treatments { get; } = new();
    public ObservableCollection<ExamRow> Exams { get; } = new();
    public ObservableCollection<BackupRow> Backups { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        OperatorList.ItemsSource = Ops;
        TreatmentList.ItemsSource = Treatments;
        ExamList.ItemsSource = Exams;
        BackupList.ItemsSource = Backups;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        // Defensive: a settings-page failure must never crash the app.
        try { RefreshInner(); }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppData.BaseDir, "settings-errors.log"),
                    System.DateTime.Now.ToString("o") + " " + ex.ToString() + "\n");
            }
            catch { }
        }
    }

    private void RefreshInner()
    {
        var clinic = AppSession.Settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());
        ClinicNameBox.Text = clinic.Name;
        HeaderBox.Text = clinic.HeaderText;
        FooterBox.Text = clinic.FooterText;

        var cur = AppSession.Settings.GetOrDefault(SettingKeys.Currency, new CurrencyConfig());
        TaxLabelBox.Text = cur.TaxLabel;
        TaxTypeBox.SelectedIndex = cur.TaxType switch { "percent" => 1, "fixed" => 2, _ => 0 };
        TaxValueBox.Value = (double)cur.TaxValue;

        Ops.Clear();
        foreach (var o in AppSession.Operators.GetAll())
            Ops.Add(new OpRow { Id = o.Id, Display = $"{o.Name} ({o.Role})" });

        Treatments.Clear();
        foreach (var t in AppSession.Treatments.GetAll())
            Treatments.Add(new TrRow { Id = t.Id, Name = t.Name, Price = UiHelpers.Money(t.PriceCents) });

        Exams.Clear();
        foreach (var e in AppSession.CustomExams.GetAll())
            Exams.Add(new ExamRow { Id = e.Id, Display = $"{e.Name} ({e.Unit})" });

        Backups.Clear();
        foreach (var b in AppSession.Backups.ListBackups())
            Backups.Add(new BackupRow { Path = b, Name = Path.GetFileName(b) });

        AboutText.Text = $"MedRecPlus Offline · ICD-11 database: {(AppSession.Icd.IsAvailable ? "bundled" : "not available")} · " +
                         $"Data: {AppData.BaseDir}";
    }

    private void SaveClinic_Click(object sender, RoutedEventArgs e)
    {
        var clinic = AppSession.Settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());
        clinic.Name = ClinicNameBox.Text.Trim();
        clinic.HeaderText = HeaderBox.Text;
        clinic.FooterText = FooterBox.Text;
        AppSession.Settings.Set(SettingKeys.ClinicProfile, clinic);
        Dialogs.Alert(XamlRoot, "Saved", "Clinic profile saved.");
    }

    private void SaveTax_Click(object sender, RoutedEventArgs e)
    {
        var cur = AppSession.Settings.GetOrDefault(SettingKeys.Currency, new CurrencyConfig());
        cur.TaxLabel = TaxLabelBox.Text.Trim();
        cur.TaxType = (TaxTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
        cur.TaxValue = (decimal)TaxValueBox.Value;
        AppSession.Settings.Set(SettingKeys.Currency, cur);
        Dialogs.Alert(XamlRoot, "Saved", "Tax settings saved. New invoices will use these.");
    }

    private async void AddOperator_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "Name", PlaceholderText = "e.g. Dr. Maya" };
        var roleBox = new TextBox { Header = "Role", PlaceholderText = "e.g. Doctor" };
        var dlg = new ContentDialog
        {
            Title = "Add operator",
            Content = new StackPanel { Spacing = 8, Children = { nameBox, roleBox } },
            PrimaryButtonText = "Add", CloseButtonText = "Cancel", XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && nameBox.Text.Trim().Length > 0)
        {
            AppSession.Operators.Create(nameBox.Text.Trim(), roleBox.Text.Trim());
            Refresh();
        }
    }

    private async void DeleteOperator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id &&
            await Dialogs.Confirm(XamlRoot, "Delete operator?", "The operator will be hidden."))
        {
            AppSession.Operators.SoftDelete(id);
            Refresh();
        }
    }

    private async void AddTreatment_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TreatmentDialog { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved") Refresh();
    }

    private async void DeleteTreatment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id &&
            await Dialogs.Confirm(XamlRoot, "Delete treatment?", "The treatment will be hidden from the catalog."))
        {
            AppSession.Treatments.SoftDelete(id);
            Refresh();
        }
    }

    private async void AddExam_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "Name", PlaceholderText = "e.g. Oxygen saturation" };
        var unitBox = new TextBox { Header = "Unit", PlaceholderText = "e.g. %" };
        var dlg = new ContentDialog
        {
            Title = "Add examination",
            Content = new StackPanel { Spacing = 8, Children = { nameBox, unitBox } },
            PrimaryButtonText = "Add", CloseButtonText = "Cancel", XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && nameBox.Text.Trim().Length > 0)
        {
            AppSession.CustomExams.Create(nameBox.Text.Trim(), unitBox.Text.Trim());
            Refresh();
        }
    }

    private async void DeleteExam_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id &&
            await Dialogs.Confirm(XamlRoot, "Delete examination?", "The template will be hidden."))
        {
            AppSession.CustomExams.SoftDelete(id);
            Refresh();
        }
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppSession.Backups.CreateBackup(AppSession.Actor);
            Dialogs.Alert(XamlRoot, "Backup created", path);
            Refresh();
        }
        catch (Exception ex) { Dialogs.Alert(XamlRoot, "Backup failed", ex.Message); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string path) return;
        if (!await Dialogs.Confirm(XamlRoot, "Restore backup?",
            "The current database will be REPLACED by this backup. The app will close — reopen it afterwards.",
            "Restore")) return;
        try
        {
            AppSession.Backups.RestoreBackup(path, AppSession.Actor);
            Dialogs.Alert(XamlRoot, "Restored", "Backup restored. Please close and reopen the app.");
        }
        catch (Exception ex) { Dialogs.Alert(XamlRoot, "Restore failed", ex.Message); }
    }

    private void ChangePw_Click(object sender, RoutedEventArgs e)
    {
        PwError.Visibility = Visibility.Collapsed;
        try
        {
            if (NewPwBox.Password != ConfirmPwBox.Password)
                throw new InvalidOperationException("New passwords do not match.");
            var newDb = AppData.ChangePassword(AppSession.Db, CurrentPwBox.Password, NewPwBox.Password);
            AppSession.Rekey(newDb);
            CurrentPwBox.Password = NewPwBox.Password = ConfirmPwBox.Password = "";
            Dialogs.Alert(XamlRoot, "Done", "Password changed. The database was re-encrypted.");
        }
        catch (Exception ex)
        {
            PwError.Text = ex.Message;
            PwError.Visibility = Visibility.Visible;
        }
    }

    public sealed class OpRow { public long Id { get; set; } public string Display { get; set; } = ""; }
    public sealed class TrRow { public long Id { get; set; } public string Name { get; set; } = ""; public string Price { get; set; } = ""; }
    public sealed class ExamRow { public long Id { get; set; } public string Display { get; set; } = ""; }
    public sealed class BackupRow { public string Path { get; set; } = ""; public string Name { get; set; } = ""; }
}
