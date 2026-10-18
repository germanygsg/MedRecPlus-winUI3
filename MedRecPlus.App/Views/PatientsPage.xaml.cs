using System.Collections.ObjectModel;
using System.ComponentModel;
using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class PatientsPage : Page
{
    public ObservableCollection<PatientRow> Rows { get; } = new();

    private bool _initialized;
    private int _page = 1;
    private int _pageSize = 10;
    private int _total;
    private readonly TableColumnPicker _columns;

    public PatientsPage()
    {
        InitializeComponent();
        _columns = new TableColumnPicker(ColumnsButton, TableHeader, TableContent, TableScroll, "patients",
            new(1, "record_number", "Record Number", MinimumWidth: 96),
            new(2, "patient_name", "Patient Name", CanHide: false, MinimumWidth: 120),
            new(3, "age", "Age", CanHide: false, MinimumWidth: 48),
            new(4, "address", "Address"),
            new(5, "phone", "Phone Number", MinimumWidth: 100),
            new(6, "initial_diagnosis", "Initial Diagnosis", MinimumWidth: 100),
            new(7, "date_added", "Date Added", MinimumWidth: 88));
        _initialized = true;
        PatientList.ItemsSource = Rows;
        Loaded += (_, _) => Refresh();
    }

    private void TableRow_Loaded(object sender, RoutedEventArgs e) => _columns.ApplyTo((Grid)sender);

    private void Refresh()
    {
        // Rows always start unselected; reset the header checkbox and toolbar before rebuilding.
        SelectAllBox.IsChecked = false;
        BulkDeleteButton.Visibility = Visibility.Collapsed;
        _total = AppSession.Patients.Count();
        var maxPage = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
        if (_page > maxPage) _page = maxPage;
        var offset = (_page - 1) * _pageSize;

        Rows.Clear();
        foreach (var p in AppSession.Patients.Search("", _pageSize, offset))
        {
            var row = new PatientRow
            {
                Id = p.Id,
                RecordNumber = p.RecordNumber,
                Name = p.Name,
                Age = p.Age.ToString(),
                Phone = p.PhoneNumber ?? "",
                Address = p.Address ?? "",
                InitialDiagnosis = p.InitialDiagnosis ?? "",
                DateAdded = UiHelpers.DateShort(p.CreatedAt),
            };
            row.PropertyChanged += Row_PropertyChanged;
            Rows.Add(row);
        }

        var first = _total == 0 ? 0 : offset + 1;
        var last = offset + Rows.Count;
        ShowingText.Text = _total == 0
            ? "No patients"
            : $"Showing {first} - {last} of {_total} patients";
        PrevPageButton.IsEnabled = _page > 1;
        NextPageButton.IsEnabled = last < _total;
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var row in Rows)
            row.IsSelected = selected;
    }

    private void SelectAll_Checked(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void SelectAll_Unchecked(object sender, RoutedEventArgs e) => SetAllSelected(false);

    // The binding pushes the new value before PropertyChanged fires, so the count is fresh here.
    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PatientRow.IsSelected))
            UpdateBulkDelete();
    }

    // Click fires on every user toggle regardless of binding timing; sync the row from the
    // checkbox itself so the count never lags behind what is on screen.
    private void RowChecked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.DataContext is PatientRow row)
            row.IsSelected = box.IsChecked == true;
        UpdateBulkDelete();
    }

    private void UpdateBulkDelete()
    {
        // Show bulk delete only when two or more rows are checked.
        BulkDeleteButton.Visibility = Rows.Count(r => r.IsSelected) >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Fires once during XAML init before the page is usable; ignore that one.
        if (!_initialized) return;
        if (PageSizeBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Content as string, out var size))
        {
            _pageSize = size;
            _page = 1;
            Refresh();
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1)
        {
            _page--;
            Refresh();
        }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_page - 1) * _pageSize + Rows.Count < _total)
        {
            _page++;
            Refresh();
        }
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
            PrimaryButtonStyle = (Style)Application.Current.Resources["DeleteConfirmationButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            AppSession.Patients.SoftDelete(id);
            Refresh();
        }
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var ids = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToArray();
        if (ids.Length == 0) return;
        var confirm = new ContentDialog
        {
            Title = $"Delete {ids.Length} patients?",
            Content = "This soft-deletes the selected patients (hidden, recoverable via restore). For permanent erasure use the patient details page.",
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DeleteConfirmationButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            foreach (var id in ids)
                AppSession.Patients.SoftDelete(id);
            Refresh();
        }
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id)
            App.Shell?.NavigateTo(typeof(PatientDetailPage), id);
    }

    private void Patient_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PatientRow row)
            App.Shell?.NavigateTo(typeof(PatientDetailPage), row.Id);
    }

    public sealed class PatientRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string RecordNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Age { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string InitialDiagnosis { get; set; } = "";
        public string DateAdded { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
