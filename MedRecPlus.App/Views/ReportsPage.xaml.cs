using System.Collections.ObjectModel;
using System.Text.Json;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class ReportsPage : Page
{
    public ObservableCollection<OpRow> OpRows { get; } = new();
    public ObservableCollection<TrendRow> TrendRows { get; } = new();

    public ReportsPage()
    {
        InitializeComponent();
        OperatorList.ItemsSource = OpRows;
        TrendList.ItemsSource = TrendRows;
        var now = DateTime.Now;
        for (var m = 1; m <= 12; m++)
            MonthBox.Items.Add(new ComboBoxItem { Content = new DateTime(2020, m, 1).ToString("MMMM"), Tag = m });
        MonthBox.SelectedIndex = now.Month - 1;
        for (var y = now.Year; y >= now.Year - 4; y--)
            YearBox.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
        YearBox.SelectedIndex = 0;
        for (var y = Math.Min(2100, now.Year); y >= Math.Max(2000, now.Year - 10); y--)
            ExportYearBox.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
        ExportYearBox.SelectedIndex = 0;
        ExportOperatorBox.Items.Add(new ComboBoxItem { Content = "All operators" });
        foreach (var op in AppSession.Operators.GetAll())
            ExportOperatorBox.Items.Add(new ComboBoxItem { Content = op.Name, Tag = op.Id });
        ExportOperatorBox.SelectedIndex = 0;
        Loaded += (_, _) => RefreshOperators();
    }

    private void Filter_Changed(object sender, object e) => RefreshOperators();

    private async void ExportData_Click(object sender, RoutedEventArgs e)
    {
        await SaveExport($"MedRecPlus_Data_{DateTime.Today:yyyy-MM-dd}.xlsx", DataExportProgress, service =>
        {
            var result = service.ExportDataPointsWithCounts();
            return (result.Bytes, $"Exported {result.Patients} patients, {result.Visits} visits, {result.Invoices} invoices.");
        });
    }

    private async void ExportOperators_Click(object sender, RoutedEventArgs e)
    {
        if (ExportYearBox.SelectedItem is not ComboBoxItem yearItem || ExportOperatorBox.SelectedItem is not ComboBoxItem operatorItem) return;
        var year = (int)yearItem.Tag;
        var id = operatorItem.Tag as long?;
        var name = operatorItem.Content?.ToString() ?? "All operators";
        var safeName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        safeName = safeName[..Math.Min(80, safeName.Length)].TrimEnd('.', ' ');
        await SaveExport($"MedRecPlus_OperatorPerformance_{year}" + (id.HasValue ? $"_{safeName}" : "") + ".xlsx",
            OperatorExportProgress, service => (service.ExportOperatorPerformance(year, id), $"Exported operator performance for {year}: {name}."));
    }

    private async Task SaveExport(string filename, ProgressRing progress, Func<ExcelExportService, (byte[] Bytes, string Message)> build)
    {
        if (!AppSession.IsReady) return;
        var sessionDb = AppSession.Db;
        bool SameSession() => AppSession.IsReady && ReferenceEquals(sessionDb, AppSession.Db);
        DataExportButton.IsEnabled = OperatorExportButton.IsEnabled = false;
        try
        {
            var path = await Dialogs.SaveFile(XamlRoot, filename, ".xlsx");
            if (path == null || !SameSession()) return;
            progress.IsActive = true;
            progress.Visibility = Visibility.Visible;
            var export = await Task.Run(() => build(new ExcelExportService(sessionDb)));
            if (!SameSession()) return; // An export completed after auto-lock: discard its bytes.
            // Local save is synchronous after the session check: lock cannot interleave it.
            File.WriteAllBytes(path, export.Bytes);
            Dialogs.Alert(XamlRoot, "Export complete", export.Message);
        }
        catch (Exception ex) { if (SameSession()) Dialogs.Alert(XamlRoot, "Export failed", ex.Message); }
        finally
        {
            progress.IsActive = false;
            progress.Visibility = Visibility.Collapsed;
            DataExportButton.IsEnabled = OperatorExportButton.IsEnabled = true;
        }
    }

    private void RefreshOperators()
    {
        if (MonthBox.SelectedItem is not ComboBoxItem mi || YearBox.SelectedItem is not ComboBoxItem yi) return;
        var from = new DateTime((int)yi.Tag, (int)mi.Tag, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
        var to = new DateTime((int)yi.Tag, (int)mi.Tag, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).ToString("o");
        using var conn = AppSession.Db.Open();
        var rows = conn.Query(
            """
            SELECT operator_name AS name, count(*) AS visits, COALESCE(SUM(paid_cents),0) AS revenue
            FROM invoices
            WHERE deleted_at IS NULL AND status = 'paid' AND date >= @from AND date < @to
            GROUP BY operator_name ORDER BY revenue DESC
            """, new { from, to });
        OpRows.Clear();
        foreach (var r in rows)
            OpRows.Add(new OpRow
            {
                Name = (string)r.name,
                Visits = $"{r.visits} paid invoices",
                Revenue = UiHelpers.Money((long)r.revenue),
            });
    }

    private void PatientSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = PatientSearchBox.Text.Trim();
        PatientResults.ItemsSource = q.Length < 2 ? null
            : AppSession.Patients.Search(q, 8).ToList();
        PatientResults.Visibility = PatientResults.ItemsSource != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PatientResult_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MedRecPlus.Core.Models.Patient p) return;
        PatientResults.Visibility = Visibility.Collapsed;
        PatientSearchBox.Text = p.Name;
        TrendTitle.Text = $"{p.Name} ({p.RecordNumber}) — visit vitals";
        TrendRows.Clear();
        foreach (var v in AppSession.Appointments.List(null, null, "", 100000).Where(v => v.PatientId == p.Id))
        {
            string vitals;
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(v.VitalSignsJson) ?? new();
                var parts = new List<string>();
                if (d.TryGetValue("bloodPressure", out var bp) && bp.GetString() is string s && s.Length > 0)
                    parts.Add($"BP {s}");
                foreach (var k in new[] { "respirationRate", "heartRate", "borgScale" })
                    if (d.TryGetValue(k, out var el) && el.TryGetInt32(out var n) && n > 0)
                        parts.Add($"{k}={n}");
                vitals = parts.Count > 0 ? string.Join(", ", parts) : "no vitals recorded";
            }
            catch { vitals = "no vitals recorded"; }
            TrendRows.Add(new TrendRow { Date = UiHelpers.Date(v.Date), Vitals = vitals });
        }
    }

    public sealed class OpRow
    {
        public string Name { get; set; } = "";
        public string Visits { get; set; } = "";
        public string Revenue { get; set; } = "";
    }

    public sealed class TrendRow
    {
        public string Date { get; set; } = "";
        public string Vitals { get; set; } = "";
    }
}
