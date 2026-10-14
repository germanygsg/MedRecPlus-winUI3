using System.Collections.ObjectModel;
using System.Text.Json;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class VisitDetailPage : Page
{
    private long _visitId;
    private Appointment _visit = null!;
    private List<TreatmentItem> _treatments = new();
    public ObservableCollection<TreatmentNoteRow> TreatmentRows { get; } = new();

    public VisitDetailPage()
    {
        InitializeComponent();
        TreatmentList.ItemsSource = TreatmentRows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _visitId = (long)e.Parameter;
        Refresh();
    }

    private void Refresh()
    {
        _visit = AppSession.Visits.GetVisit(_visitId);
        _treatments = JsonSerializer.Deserialize<List<TreatmentItem>>(_visit.TreatmentsJson) ?? new();

        var invoiced = AppSession.Visits.IsInvoiced(_visitId);
        FrozenNote.Visibility = invoiced ? Visibility.Visible : Visibility.Collapsed;
        var inv = AppSession.InvoiceRecords.GetByAppointmentId(_visitId);
        InvoiceButton.Content = inv == null ? "Generate invoice" : $"View invoice ({inv.InvoiceNumber})";

        TitleText.Text = $"{_visit.PatientName}";
        MetaText.Text = $"{UiHelpers.Date(_visit.Date)} · {_visit.OperatorName}";
        NotesText.Text = string.IsNullOrWhiteSpace(_visit.Notes) ? "" : $"Notes: {_visit.Notes}";

        try
        {
            var vitals = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(_visit.VitalSignsJson)
                         ?? new();
            var parts = new List<string>();
            if (vitals.TryGetValue("bloodPressure", out var bp) && bp.GetString() is string s && s.Length > 0)
                parts.Add($"BP {s}");
            foreach (var k in new[] { "respirationRate", "heartRate", "borgScale" })
                if (vitals.TryGetValue(k, out var el) && el.TryGetInt32(out var n) && n > 0)
                    parts.Add($"{k}: {n}");
            foreach (var kv in vitals.Where(kv => kv.Key.StartsWith("custom_")))
                parts.Add($"{kv.Key}: {kv.Value.GetString()}");
            VitalsText.Text = parts.Count > 0 ? string.Join(" · ", parts) : "Not recorded";
        }
        catch { VitalsText.Text = "Not recorded"; }

        TreatmentRows.Clear();
        foreach (var t in _treatments)
            TreatmentRows.Add(new TreatmentNoteRow { Id = t.Id, Name = t.Name, Price = UiHelpers.Money(t.PriceCents), Notes = t.Notes ?? "" });

        SubtotalText.Text = $"Subtotal: {UiHelpers.Money(_visit.TotalPriceCents)}";
        DiscountText.Text = _visit.DiscountType == DiscountTypes.None ? "" :
            $"Discount ({_visit.DiscountType} {(_visit.DiscountType == DiscountTypes.Percent ? _visit.DiscountValueCents + "%" : UiHelpers.Money(_visit.DiscountValueCents))})";
        var discVal = _visit.DiscountType == DiscountTypes.Percent ? (decimal)_visit.DiscountValueCents : _visit.DiscountValueCents / 100m;
        var totals = MoneyMath.Compute(_visit.TotalPriceCents, _visit.DiscountType, discVal, TaxTypes.None, 0);
        TotalText.Text = $"Payable: {UiHelpers.Money(totals.TotalCents)}";
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.Shell?.NavigateTo(typeof(AppointmentsPage));

    private void Invoice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var inv = AppSession.Invoices.GenerateFromAppointment(_visitId, AppSession.Actor);
            App.Shell?.NavigateTo(typeof(InvoiceDetailPage), inv.Id);
        }
        catch (Exception ex) { Dialogs.Alert(XamlRoot, "Invoice", ex.Message); }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (AppSession.Visits.IsInvoiced(_visitId))
        {
            Dialogs.Alert(XamlRoot, "Frozen", "This visit is invoiced and frozen. Only treatment notes can be edited.");
            return;
        }
        Dialogs.Alert(XamlRoot, "Edit visit", "Full visit editing opens here in the next pass — treatment notes can be edited below right now.");
        await Task.CompletedTask;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var hasInvoice = AppSession.Visits.IsInvoiced(_visitId);
        var msg = hasInvoice ? "This visit has an invoice — deleting the visit also deletes its invoice." : "Delete this visit record?";
        if (await Dialogs.Confirm(XamlRoot, "Delete visit?", msg))
        {
            AppSession.Visits.DeleteVisit(_visitId, AppSession.Actor);
            App.Shell?.NavigateTo(typeof(AppointmentsPage));
        }
    }


    private void SaveNotes_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in TreatmentRows)
        {
            var t = _treatments.FirstOrDefault(t => t.Id == row.Id);
            if (t != null) t.Notes = row.Notes;
        }
        AppSession.Visits.UpdateTreatmentNotes(_visitId, _treatments, AppSession.Actor);
        Dialogs.Alert(XamlRoot, "Saved", "Treatment notes updated.");
    }

    public sealed class TreatmentNoteRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
