using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedRecPlus.App.Views;

public sealed partial class InvoicesPage : Page
{
    private bool _loaded;
    private int _page = 1;
    private int _pageSize = 10;
    private int _total;
    private readonly TableColumnPicker _columns;
    public ObservableCollection<InvoiceRow> Rows { get; } = new();

    public InvoicesPage()
    {
        InitializeComponent();
        _columns = new TableColumnPicker(ColumnsButton, TableHeader, TableContent, TableScroll, "invoices",
            new(1, "number", "Number", MinimumWidth: 104),
            new(2, "patient_name", "Patient", CanHide: false, MinimumWidth: 120),
            new(3, "date", "Date", MinimumWidth: 88),
            new(4, "total", "Total"),
            new(5, "status", "Status"));
        InvoiceList.ItemsSource = Rows;
        Loaded += (_, _) => { _loaded = true; Refresh(); };
    }

    private void TableRow_Loaded(object sender, RoutedEventArgs e) => _columns.ApplyTo((Grid)sender);

    private void Refresh()
    {
        // Rows always start unselected; reset the header checkbox and toolbar before rebuilding.
        SelectAllBox.IsChecked = false;
        BulkDeleteButton.Visibility = Visibility.Collapsed;
        var status = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(status)) status = null;
        _total = AppSession.InvoiceRecords.Count("", status);
        var maxPage = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
        if (_page > maxPage) _page = maxPage;
        var offset = (_page - 1) * _pageSize;

        Rows.Clear();
        foreach (var inv in AppSession.InvoiceRecords.List("", status, null, null, _pageSize, offset))
            Rows.Add(new InvoiceRow
            {
                Id = inv.Id,
                Number = inv.InvoiceNumber,
                Patient = inv.PatientName,
                Date = UiHelpers.DateShort(inv.Date),
                Total = UiHelpers.Money(inv.TotalCents),
                Status = inv.Status,
                StatusChipBrush = ChipBrush(inv.Status),
                StatusChipForeground = ChipForeground(inv.Status),
            });

        var first = _total == 0 ? 0 : offset + 1;
        var last = offset + Rows.Count;
        ShowingText.Text = _total == 0
            ? "No invoices"
            : $"Showing {first} - {last} of {_total} invoices";
        PrevPageButton.IsEnabled = _page > 1;
        NextPageButton.IsEnabled = last < _total;
    }

    // Status chips reuse the shared tint/text brushes from App.xaml.
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
        if (e.PropertyName == nameof(InvoiceRow.IsSelected))
            UpdateBulkDelete();
    }

    // Click fires on every user toggle regardless of binding timing; sync the row from the
    // checkbox itself so the count never lags behind what is on screen.
    private void RowChecked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.DataContext is InvoiceRow row)
            row.IsSelected = box.IsChecked == true;
        UpdateBulkDelete();
    }

    private void UpdateBulkDelete()
    {
        // Show bulk delete only when two or more rows are checked.
        BulkDeleteButton.Visibility = Rows.Count(r => r.IsSelected) >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Changed(object sender, object e)
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

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id)
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), id);
    }

    private void Invoice_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InvoiceRow row)
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), row.Id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        if (await Dialogs.Confirm(XamlRoot, "Delete invoice?", "This soft-deletes the invoice (hidden, recoverable via restore)."))
        {
            AppSession.Invoices.DeleteInvoice(id, AppSession.Actor);
            Refresh();
        }
    }

    private async void BulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var ids = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToArray();
        if (ids.Length == 0) return;
        if (await Dialogs.Confirm(XamlRoot, $"Delete {ids.Length} invoices?",
            "This soft-deletes the selected invoices (hidden, recoverable via restore)."))
        {
            foreach (var id in ids)
                AppSession.Invoices.DeleteInvoice(id, AppSession.Actor);
            Refresh();
        }
    }

    public sealed class InvoiceRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string Patient { get; set; } = "";
        public string Date { get; set; } = "";
        public string Total { get; set; } = "";
        public string Status { get; set; } = "";
        public Brush StatusChipBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        public Brush StatusChipForeground { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

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
