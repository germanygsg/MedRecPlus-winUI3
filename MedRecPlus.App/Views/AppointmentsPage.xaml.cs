using System.Collections.ObjectModel;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class AppointmentsPage : Page
{
    private long? _preselectPatientId;
    private bool _loaded;
    public ObservableCollection<VisitRow> Rows { get; } = new();

    public AppointmentsPage()
    {
        InitializeComponent();
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

    private void Refresh()
    {
        var (from, to) = Range();
        Rows.Clear();
        foreach (var v in AppSession.Appointments.List(from, to, SearchBox.Text.Trim(), 200))
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
            });
        }
    }

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        Refresh();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        Refresh();
    }

    private async void NewVisit_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VisitDialog(_preselectPatientId) { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved") Refresh();
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

    public sealed class VisitRow
    {
        public long Id { get; set; }
        public string Date { get; set; } = "";
        public string Patient { get; set; } = "";
        public string Operator { get; set; } = "";
        public string Total { get; set; } = "";
        public string Invoice { get; set; } = "";
    }
}
