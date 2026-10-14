using System.Collections.ObjectModel;
using System.Text.Json;
using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class PatientDetailPage : Page
{
    private long _patientId;
    public ObservableCollection<VisitRow> Visits { get; } = new();
    public ObservableCollection<AttachmentRow> Attachments { get; } = new();

    public PatientDetailPage()
    {
        InitializeComponent();
        VisitList.ItemsSource = Visits;
        AttachmentList.ItemsSource = Attachments;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _patientId = (long)e.Parameter;
        Refresh();
    }

    private void Refresh()
    {
        var p = AppSession.Patients.GetById(_patientId);
        if (p == null) { Back_Click(null!, null!); return; }

        NameText.Text = p.Name;
        RecordText.Text = p.RecordNumber;
        InfoText.Text = $"{p.Age} yrs · {p.Gender} · {p.PhoneNumber} · {p.Address}";
        DiagnosisText.Text = string.IsNullOrWhiteSpace(p.InitialDiagnosis)
            ? "" : $"Diagnosis: {p.InitialDiagnosis}";
        IcdChips.Children.Clear();
        if (!string.IsNullOrEmpty(p.IcdCodesJson))
        {
            try
            {
                foreach (var c in JsonSerializer.Deserialize<List<IcdCode>>(p.IcdCodesJson) ?? new())
                {
                    IcdChips.Children.Add(new Border
                    {
                        Padding = new Thickness(8, 4, 8, 4),
                        CornerRadius = new CornerRadius(12),
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.LightSteelBlue),
                        Child = new TextBlock { Text = $"{c.Code}", FontSize = 12 },
                    });
                }
            }
            catch { }
        }
        ConsentText.Text = string.IsNullOrEmpty(p.ConsentBasis)
            ? "⚠ No lawful basis recorded."
            : $"{p.ConsentBasis} · recorded {UiHelpers.DateShort(p.ConsentDate ?? "")} by {p.ConsentRecordedBy}";

        Visits.Clear();
        foreach (var v in AppSession.Appointments.List(null, null, "", 1000)
                     .Where(v => v.PatientId == _patientId))
        {
            var inv = AppSession.InvoiceRecords.GetByAppointmentId(v.Id);
            Visits.Add(new VisitRow
            {
                Id = v.Id,
                Date = UiHelpers.Date(v.Date),
                Operator = v.OperatorName,
                Total = UiHelpers.Money(v.TotalPriceCents),
                Invoice = inv == null ? "no invoice" : inv.Status,
            });
        }

        Attachments.Clear();
        foreach (var a in AppSession.Attachments.ListByOwner("patient", _patientId))
            Attachments.Add(new AttachmentRow
            {
                Id = a.Id,
                FileName = a.FileName,
                Size = $"{a.SizeBytes / 1024} KB",
            });
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.Shell?.NavigateTo(typeof(PatientsPage));

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PatientDialog(_patientId) { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "saved") Refresh();
    }

    private void NewVisit_Click(object sender, RoutedEventArgs e) =>
        App.Shell?.NavigateTo(typeof(AppointmentsPage), _patientId);

    private void Visit_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VisitRow row)
            App.Shell?.NavigateTo(typeof(VisitDetailPage), row.Id);
    }

    private async void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var path = await Dialogs.PickImageFile(XamlRoot);
        if (path == null) return;
        try
        {
            AppSession.Attachments.Add("patient", _patientId, path, null, null, AppSession.Actor);
            Refresh();
        }
        catch (Exception ex) { Dialogs.Alert(XamlRoot, "Attachment failed", ex.Message); }
    }

    private async void AttachmentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id &&
            await Dialogs.Confirm(XamlRoot, "Delete attachment?", "The file will be removed."))
        {
            AppSession.Attachments.Delete(id, AppSession.Actor);
            Refresh();
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var p = AppSession.Patients.GetById(_patientId);
        if (p == null) return;
        var json = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
        var path = await Dialogs.SaveFile(XamlRoot, $"{p.RecordNumber}.json", ".json");
        if (path != null) File.WriteAllText(path, json);
    }

    private async void Erase_Click(object sender, RoutedEventArgs e)
    {
        var p = AppSession.Patients.GetById(_patientId);
        if (p == null) return;
        var dlg = new ErasePatientDialog(p) { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (dlg.Tag as string == "erased")
            App.Shell?.NavigateTo(typeof(PatientsPage));
    }

    public sealed class VisitRow
    {
        public long Id { get; set; }
        public string Date { get; set; } = "";
        public string Operator { get; set; } = "";
        public string Total { get; set; } = "";
        public string Invoice { get; set; } = "";
    }

    public sealed class AttachmentRow
    {
        public long Id { get; set; }
        public string FileName { get; set; } = "";
        public string Size { get; set; } = "";
    }
}
