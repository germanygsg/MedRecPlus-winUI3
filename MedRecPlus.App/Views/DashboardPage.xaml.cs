using System.Collections.ObjectModel;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class DashboardPage : Page
{
    public ObservableCollection<InvoiceRow> Rows { get; } = new();

    public DashboardPage()
    {
        InitializeComponent();
        RecentInvoices.ItemsSource = Rows;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var clinic = AppSession.Settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());
        WelcomeText.Text = string.IsNullOrWhiteSpace(clinic.Name) ? "Dashboard" : clinic.Name;

        PatientsCount.Text = AppSession.Patients.Count().ToString();
        PatientsSub.Text = "total registered";

        var todayVisits = AppSession.Appointments.List(UiHelpers.TodayIsoStart(), UiHelpers.TodayIsoEnd(), "", 1000);
        VisitsToday.Text = todayVisits.Count().ToString();
        VisitsSub.Text = "today";

        var revenue = AppSession.InvoiceRecords.RevenueCents(UiHelpers.MonthIsoStart(), null);
        RevenueMonth.Text = UiHelpers.Money(revenue);
        RevenueSub.Text = "paid invoices, this month";

        var unpaidList = AppSession.InvoiceRecords.List("", "unpaid", null, null, 1000).ToList();
        UnpaidCount.Text = unpaidList.Count.ToString();
        UnpaidSub.Text = $"{UiHelpers.Money(unpaidList.Sum(i => i.TotalCents - i.PaidCents))} outstanding";

        Rows.Clear();
        foreach (var inv in AppSession.InvoiceRecords.List("", null, null, null, 10))
            Rows.Add(new InvoiceRow
            {
                Id = inv.Id,
                Number = inv.InvoiceNumber,
                Patient = inv.PatientName,
                Total = UiHelpers.Money(inv.TotalCents),
                Status = inv.Status,
            });
    }

    private void NewVisit_Click(object sender, RoutedEventArgs e)
    {
        App.Shell?.SelectNav("Appointments");
        App.Shell?.NavigateTo(typeof(AppointmentsPage));
    }

    private void AddPatient_Click(object sender, RoutedEventArgs e)
    {
        App.Shell?.SelectNav("Patients");
        App.Shell?.NavigateTo(typeof(PatientsPage));
    }

    private void Invoice_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InvoiceRow row)
        {
            App.Shell?.SelectNav("Invoices");
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), row.Id);
        }
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
