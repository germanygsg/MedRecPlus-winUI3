using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class InvoicesPage : Page
{
    private bool _loaded;
    public ObservableCollection<InvoiceRow> Rows { get; } = new();

    public InvoicesPage()
    {
        InitializeComponent();
        InvoiceList.ItemsSource = Rows;
        Loaded += (_, _) => { _loaded = true; Refresh(); };
    }

    private void Refresh()
    {
        var status = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(status)) status = null;
        Rows.Clear();
        foreach (var inv in AppSession.InvoiceRecords.List("", status, null, null, 200))
            Rows.Add(new InvoiceRow
            {
                Id = inv.Id,
                Number = inv.InvoiceNumber,
                Patient = inv.PatientName,
                Date = UiHelpers.DateShort(inv.Date),
                Total = UiHelpers.Money(inv.TotalCents),
                Status = inv.Status,
            });
    }

    private void Filter_Changed(object sender, object e)
    {
        if (!_loaded) return;
        Refresh();
    }

    private void Invoice_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InvoiceRow row)
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), row.Id);
    }

    public sealed class InvoiceRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string Patient { get; set; } = "";
        public string Date { get; set; } = "";
        public string Total { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
