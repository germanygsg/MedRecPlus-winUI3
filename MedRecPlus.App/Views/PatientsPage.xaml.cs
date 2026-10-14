using System.Collections.ObjectModel;
using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class PatientsPage : Page
{
    public ObservableCollection<PatientRow> Rows { get; } = new();

    private bool _loaded;

    public PatientsPage()
    {
        InitializeComponent();
        PatientList.ItemsSource = Rows;
        Loaded += (_, _) => { _loaded = true; Refresh(); };
    }

    private void Refresh()
    {
        Rows.Clear();
        foreach (var p in AppSession.Patients.Search(SearchBox.Text.Trim(), 100))
            Rows.Add(new PatientRow
            {
                Id = p.Id, RecordNumber = p.RecordNumber,
                Name = p.Name, Age = p.Age.ToString(), Phone = p.PhoneNumber ?? "",
            });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        Refresh();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PatientDialog();
        dlg.XamlRoot = XamlRoot;
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved")
            Refresh();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id)
        {
            var dlg = new PatientDialog(id);
            dlg.XamlRoot = XamlRoot;
            await dlg.ShowAsync();
            if (dlg.Tag as string == "saved")
                Refresh();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var confirm = new ContentDialog
        {
            Title = "Delete patient?",
            Content = "This soft-deletes the patient (hidden, recoverable via restore). For permanent erasure use the patient details page.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            AppSession.Patients.SoftDelete(id);
            Refresh();
        }
    }

    private void Patient_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PatientRow row)
            App.Shell?.NavigateTo(typeof(PatientDetailPage), row.Id);
    }

    public sealed class PatientRow
    {
        public long Id { get; set; }
        public string RecordNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Age { get; set; } = "";
        public string Phone { get; set; } = "";
    }
}
