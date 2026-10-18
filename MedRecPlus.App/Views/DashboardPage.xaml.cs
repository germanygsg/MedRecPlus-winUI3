using System.Collections.ObjectModel;
using System.Globalization;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace MedRecPlus.App.Views;

public sealed partial class DashboardPage : Page
{
    public ObservableCollection<InvoiceRow> Rows { get; } = new();
    private bool _loaded;
    private DashboardRepository Data => new(AppSession.Db);

    public DashboardPage()
    {
        InitializeComponent();
        RecentInvoices.ItemsSource = Rows;
        Loaded += (_, _) => { _loaded = true; Refresh(); };
        Unloaded += (_, _) => _loaded = false;
        ActualThemeChanged += (_, _) => { if (_loaded) RefreshTrends(); };
    }

    private static int Days(ComboBox range) => range.SelectedIndex switch { 1 => 30, 2 => 90, _ => 7 };

    private void Refresh()
    {
        RefreshTrends();
        RefreshPatients();
        var unpaid = Data.Outstanding();
        UnpaidCount.Text = $"{unpaid.Count:N0} unpaid {(unpaid.Count == 1 ? "invoice" : "invoices")}";
        UnpaidSub.Text = $"{UiHelpers.Money(unpaid.BalanceCents)} outstanding";
        Rows.Clear();
        foreach (var inv in AppSession.InvoiceRecords.List("", null, null, null, 10))
            Rows.Add(new InvoiceRow
            {
                Id = inv.Id, Number = inv.InvoiceNumber, Patient = inv.PatientName,
                Total = UiHelpers.Money(inv.TotalCents), Status = inv.Status,
            });
        InvoicesEmpty.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshTrends(ComboBox? changed = null)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        if (changed == null || changed == PatientRange)
        {
            var trend = Data.Trend(DashboardMetric.Patients, Days(PatientRange));
            PatientsCount.Text = trend.Sum(day => day.Value).ToString("N0");
            PatientsSub.Text = "New registrations in this period";
            PatientChart.SetData(trend, dark ? Color.FromArgb(255, 183, 155, 255) : Color.FromArgb(255, 139, 101, 217));
        }
        if (changed == null || changed == AppointmentRange)
        {
            var trend = Data.Trend(DashboardMetric.Appointments, Days(AppointmentRange));
            VisitsToday.Text = trend.Sum(day => day.Value).ToString("N0");
            VisitsSub.Text = "Visits in this period";
            AppointmentChart.SetData(trend, dark ? Color.FromArgb(255, 108, 213, 153) : Color.FromArgb(255, 51, 167, 104));
        }
        if (changed == null || changed == RevenueRange)
        {
            var trend = Data.Trend(DashboardMetric.Revenue, Days(RevenueRange));
            RevenueMonth.Text = UiHelpers.Money(trend.Sum(day => day.Value));
            RevenueChart.SetData(trend, dark ? Color.FromArgb(255, 255, 188, 98) : Color.FromArgb(255, 228, 148, 43), money: true);
        }
    }

    private void RefreshPatients()
    {
        var patients = Data.RecentPatients(Days(QuickRange)).Select(patient => new QuickPatientRow
        {
            Id = patient.Id, Name = patient.Name,
            Initial = string.IsNullOrWhiteSpace(patient.Name) ? "?" : StringInfo.GetNextTextElement(patient.Name.Trim()).ToUpper(CultureInfo.CurrentCulture),
            Detail = $"{patient.Age} years · {patient.RecordNumber}",
            Visits = $"{patient.Visits} {(patient.Visits == 1 ? "visit" : "visits")}",
        }).ToList();
        QuickPatients.ItemsSource = patients;
        QuickEmpty.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Range_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (ReferenceEquals(sender, QuickRange)) RefreshPatients();
        else RefreshTrends(sender as ComboBox);
    }

    private async void NewVisit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VisitDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Tag as string == "saved") Refresh();
    }

    private async void AddPatient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PatientDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Tag as string == "saved") Refresh();
    }

    private void AllPatients_Click(object sender, RoutedEventArgs e)
    {
        App.Shell?.SelectNav("Patients");
        App.Shell?.NavigateTo(typeof(PatientsPage));
    }

    private void AllInvoices_Click(object sender, RoutedEventArgs e)
    {
        App.Shell?.SelectNav("Invoices");
        App.Shell?.NavigateTo(typeof(InvoicesPage));
    }

    private void Patient_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QuickPatientRow row)
        {
            App.Shell?.SelectNav("Patients");
            App.Shell?.NavigateTo(typeof(PatientDetailPage), row.Id);
        }
    }

    private void Invoice_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InvoiceRow row)
        {
            App.Shell?.SelectNav("Invoices");
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), row.Id);
        }
    }

    public sealed class QuickPatientRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Initial { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Visits { get; set; } = "";
    }

    public sealed class InvoiceRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string Patient { get; set; } = "";
        public string Total { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
