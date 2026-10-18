using System.Collections.ObjectModel;
using System.ComponentModel;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class AppointmentsPage : Page
{
    private long? _preselectPatientId;
    private bool _loaded;
    private int _page = 1;
    private int _pageSize = 10;
    private int _total;
    private readonly TableColumnPicker _columns;
    public ObservableCollection<VisitRow> Rows { get; } = new();

    public AppointmentsPage()
    {
        InitializeComponent();
        _columns = new TableColumnPicker(ColumnsButton, TableHeader, TableContent, TableScroll, "appointments",
            new(1, "date", "Date", MinimumWidth: 88),
            new(2, "patient_name", "Patient", CanHide: false, MinimumWidth: 120),
            new(3, "operator", "Operator"),
            new(4, "total", "Total"),
            new(5, "invoice", "Invoice"));
        VisitList.ItemsSource = Rows;
        Loaded += (_, _) => { _loaded = true; Refresh(); };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppData.BaseDir, "dlg.log"),
                $"OnNavigatedTo param={(e.Parameter == null ? "null" : e.Parameter.GetType().Name + ":" + e.Parameter)}\n");
        }
        catch { }
        if (e.Parameter is long pid)
        {
            _preselectPatientId = pid;
            // Auto-open the new-visit dialog with this patient preselected.
            // (Loaded already fired by the time OnNavigatedTo runs, so open directly.)
            _ = AutoOpenVisitDialogAsync(pid);
        }
    }

    private async Task AutoOpenVisitDialogAsync(long pid)
    {
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppData.BaseDir, "dlg.log"),
                $"AutoOpen start pid={pid} xamlroot={(XamlRoot == null ? "null" : "ok")}\n");
            // Let the page finish rendering before showing the modal dialog.
            await Task.Delay(100);
            var dlg = new VisitDialog(pid) { XamlRoot = XamlRoot };
            await dlg.ShowAsync();
            if (dlg.Tag as string == "saved") Refresh();
            _preselectPatientId = null;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppData.BaseDir, "dlg.log"), "AutoOpen done\n");
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppData.BaseDir, "dlg.log"),
                    "AutoOpen ERROR: " + ex.ToString() + "\n");
            }
            catch { }
        }
    }

    private (string? from, string? to) Range()
    {
        return RangeBox.SelectedIndex switch
        {
            0 => (UiHelpers.TodayIsoStart(), UiHelpers.TodayIsoEnd()),
            1 => (DateTime.UtcNow.AddDays(-7).ToString("o"), null),
            _ => (null, null),
        };
    }

    private void TableRow_Loaded(object sender, RoutedEventArgs e) => _columns.ApplyTo((Grid)sender);

    private void Refresh()
    {
        // Rows always start unselected; reset the header checkbox and toolbar before rebuilding.
        SelectAllBox.IsChecked = false;
        BulkDeleteButton.Visibility = Visibility.Collapsed;
        var (from, to) = Range();
        _total = AppSession.Appointments.Count(from, to);
        var maxPage = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
        if (_page > maxPage) _page = maxPage;
        var offset = (_page - 1) * _pageSize;

        Rows.Clear();
        foreach (var v in AppSession.Appointments.List(from, to, "", _pageSize, offset))
        {
            var inv = AppSession.InvoiceRecords.GetByAppointmentId(v.Id);
            Rows.Add(new VisitRow
            {
                Id = v.Id,
                Date = UiHelpers.Date(v.Date),
                Patient = $"{v.PatientName}",
                Operator = v.OperatorName,
                Total = UiHelpers.Money(v.TotalPriceCents),
                Invoice = inv == null ? "—" : inv.Status,
                ChipVisibility = inv == null ? Visibility.Collapsed : Visibility.Visible,
                InvoiceChipBrush = ChipBrush(inv?.Status),
                InvoiceChipForeground = ChipForeground(inv?.Status),
            });
        }

        var first = _total == 0 ? 0 : offset + 1;
        var last = offset + Rows.Count;
        ShowingText.Text = _total == 0
            ? "No visits"
            : $"Showing {first} - {last} of {_total} visits";
        PrevPageButton.IsEnabled = _page > 1;
        NextPageButton.IsEnabled = last < _total;
    }

    // Invoice status chips reuse the shared tint/text brushes from App.xaml.
    private static Brush ChipBrush(string? status) => status switch
    {
        "paid" => (Brush)Application.Current.Resources["StatusChipPaidBrush"],
        "unpaid" => (Brush)Application.Current.Resources["StatusChipUnpaidBrush"],
        _ => (Brush)Application.Current.Resources["StatusChipVoidBrush"],
    };

    private static Brush ChipForeground(string? status) => status switch
    {
        "paid" => (Brush)Application.Current.Resources["StatusPaidTextBrush"],
        "unpaid" => (Brush)Application.Current.Resources["StatusUnpaidTextBrush"],
        _ => (Brush)Application.Current.Resources["StatusVoidTextBrush"],
    };

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
        if (e.PropertyName == nameof(VisitRow.IsSelected))
            UpdateBulkDelete();
    }

    // Click fires on every user toggle regardless of binding timing; sync the row from the
    // checkbox itself so the count never lags behind what is on screen.
    private void RowChecked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.DataContext is VisitRow row)
            row.IsSelected = box.IsChecked == true;
        UpdateBulkDelete();
    }

    private void UpdateBulkDelete()
    {
        // Show bulk delete only when two or more rows are checked.
        BulkDeleteButton.Visibility = Rows.Count(r => r.IsSelected) >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _page = 1;
        Refresh();
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Fires once during XAML init before the page is usable; ignore that one.
        if (!_loaded) return;
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

    private async void NewVisit_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VisitDialog(_preselectPatientId) { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved") Refresh();
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id)
            App.Shell?.NavigateTo(typeof(VisitDetailPage), id);
    }

    private void Visit_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VisitRow row)
            App.Shell?.NavigateTo(typeof(VisitDetailPage), row.Id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var hasInvoice = AppSession.Visits.IsInvoiced(id);
        var msg = hasInvoice
            ? "This visit has an invoice — deleting the visit also deletes its invoice."
            : "Delete this visit record?";
        if (await Dialogs.Confirm(XamlRoot, "Delete visit?", msg))
        {
            AppSession.Visits.DeleteVisit(id, AppSession.Actor);
            Refresh();
        }
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var ids = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToArray();
        if (ids.Length == 0) return;
        if (await Dialogs.Confirm(XamlRoot, $"Delete {ids.Length} visits?",
            "This deletes the selected visits and any invoices attached to them."))
        {
            foreach (var id in ids)
                AppSession.Visits.DeleteVisit(id, AppSession.Actor);
            Refresh();
        }
    }

    public sealed class VisitRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Date { get; set; } = "";
        public string Patient { get; set; } = "";
        public string Operator { get; set; } = "";
        public string Total { get; set; } = "";
        public string Invoice { get; set; } = "";
        public Visibility ChipVisibility { get; set; } = Visibility.Collapsed;
        public Brush InvoiceChipBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        public Brush InvoiceChipForeground { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

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
