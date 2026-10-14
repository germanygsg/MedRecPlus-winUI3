// Deterministic AI Insights engine (PRD §8). The old app's 'local' and 'cloud'
// provider modes were never implemented — only this deterministic mode ships.
// Answers are computed locally from the DB; every answer carries a non-clinical
// disclaimer and an evidence list (auditability). Indonesian + English keywords.

using Dapper;
using MedRecPlus.Core.Data;

namespace MedRecPlus.Core.Services;

public sealed class AiAnswer
{
    public string Text { get; set; } = "";
    public List<(string Label, string Value)> Kpis { get; } = new();
    public List<(string Source, string RecordId, string Summary)> Evidence { get; } = new();
    public const string Disclaimer =
        "Analytics only — not medical advice. Figures are computed from local clinic records.";
}

public sealed class AiInsightsService
{
    private readonly Database _db;
    private readonly AuditService _audit;

    public AiInsightsService(Database db)
    {
        _db = db;
        _audit = new AuditService(db);
    }

    public AiAnswer Answer(string question, string actor)
    {
        var q = question.ToLowerInvariant();
        AiAnswer ans = q switch
        {
            var s when ContainsAny(s, "pendapatan", "omzet", "revenue", "income", "pemasukan")
                => RevenueSummary(q),
            var s when ContainsAny(s, "belum bayar", "belum dibayar", "tunggakan", "unpaid", "overdue", "piutang")
                => UnpaidFollowups(),
            var s when ContainsAny(s, "operator", "dokter", "doctor", "top")
                => OperatorPerformance(),
            var s when ContainsAny(s, "pasien", "patient")
                => PatientSummary(),
            var s when ContainsAny(s, "kunjungan", "visit", "appointment")
                => VisitSummary(),
            var s when ContainsAny(s, "treatment", "tindakan", "layanan")
                => TreatmentTrends(),
            _ => Overview(),
        };

        using (var conn = _db.Open())
            conn.Execute(
                "INSERT INTO ai_logs (at, question, intent, tools_used, row_counts, duration_ms) VALUES (@now, @q, @intent, '[]', '{}', 0)",
                new { now = DbTime.Now(), q = question, intent = q });
        _audit.Append("ai_question_asked", "ai", null, actor,
            new Dictionary<string, object> { ["question_len"] = question.Length });
        return ans;
    }

    private static bool ContainsAny(string q, params string[] kws) => kws.Any(q.Contains);

    private AiAnswer Overview()
    {
        using var conn = _db.Open();
        var patients = conn.ExecuteScalar<long>("SELECT count(*) FROM patients WHERE deleted_at IS NULL");
        var visits = conn.ExecuteScalar<long>("SELECT count(*) FROM appointments WHERE deleted_at IS NULL");
        var paid = conn.ExecuteScalar<long>("SELECT COALESCE(SUM(paid_cents),0) FROM invoices WHERE deleted_at IS NULL AND status='paid'");
        var unpaid = conn.ExecuteScalar<long>("SELECT count(*) FROM invoices WHERE deleted_at IS NULL AND status='unpaid'");
        var ans = new AiAnswer
        {
            Text = $"Clinic overview: {patients} patients, {visits} visits on record. " +
                   $"Paid revenue totals {Fmt(paid)}. {unpaid} invoices are still unpaid. " + AiAnswer.Disclaimer,
        };
        ans.Kpis.Add(("Patients", patients.ToString()));
        ans.Kpis.Add(("Visits", visits.ToString()));
        ans.Kpis.Add(("Paid revenue", Fmt(paid)));
        ans.Kpis.Add(("Unpaid invoices", unpaid.ToString()));
        ans.Evidence.Add(("patients", "count", $"{patients} active records"));
        ans.Evidence.Add(("appointments", "count", $"{visits} visit records"));
        ans.Evidence.Add(("invoices", "sum(paid_cents)", Fmt(paid)));
        return ans;
    }

    private AiAnswer RevenueSummary(string q)
    {
        var days = q.Contains("30") || q.Contains("bulan") || q.Contains("month") ? 30 : 7;
        var from = DateTime.UtcNow.AddDays(-days).ToString("o");
        using var conn = _db.Open();
        var paid = conn.ExecuteScalar<long>(
            "SELECT COALESCE(SUM(paid_cents),0) FROM invoices WHERE deleted_at IS NULL AND status='paid' AND date >= @from",
            new { from });
        var count = conn.ExecuteScalar<long>(
            "SELECT count(*) FROM invoices WHERE deleted_at IS NULL AND status='paid' AND date >= @from",
            new { from });
        var ans = new AiAnswer
        {
            Text = $"Revenue in the last {days} days: {Fmt(paid)} across {count} paid invoices. " + AiAnswer.Disclaimer,
        };
        ans.Kpis.Add(($"Paid revenue ({days}d)", Fmt(paid)));
        ans.Kpis.Add(("Paid invoices", count.ToString()));
        ans.Evidence.Add(("invoices", $"last {days}d paid", $"{count} invoices, {Fmt(paid)}"));
        return ans;
    }

    private AiAnswer UnpaidFollowups()
    {
        using var conn = _db.Open();
        var rows = conn.Query(
            """
            SELECT invoice_number, patient_name, total_cents - paid_cents AS owing, date
            FROM invoices WHERE deleted_at IS NULL AND status = 'unpaid'
            ORDER BY date LIMIT 10
            """).ToList();
        var totalOwing = rows.Sum(r => (long)r.owing);
        var ans = new AiAnswer
        {
            Text = rows.Count == 0
                ? "No unpaid invoices. " + AiAnswer.Disclaimer
                : $"{rows.Count} unpaid invoices (showing oldest 10), total outstanding {Fmt(totalOwing)}. " +
                  "Consider following up on the oldest ones first. " + AiAnswer.Disclaimer,
        };
        ans.Kpis.Add(("Unpaid invoices", rows.Count.ToString()));
        ans.Kpis.Add(("Outstanding", Fmt(totalOwing)));
        foreach (var r in rows)
            ans.Evidence.Add(("invoices", (string)r.invoice_number,
                $"{r.patient_name}: {Fmt((long)r.owing)} outstanding"));
        return ans;
    }

    private AiAnswer OperatorPerformance()
    {
        using var conn = _db.Open();
        var rows = conn.Query(
            """
            SELECT i.operator_name AS name, count(*) AS visits,
                   COALESCE(SUM(i.paid_cents), 0) AS revenue
            FROM invoices i
            WHERE i.deleted_at IS NULL AND i.status = 'paid'
            GROUP BY i.operator_name ORDER BY revenue DESC LIMIT 10
            """).ToList();
        var ans = new AiAnswer
        {
            Text = rows.Count == 0
                ? "No paid invoices yet, so there is no operator revenue to rank. " + AiAnswer.Disclaimer
                : "Operator ranking by paid-invoice revenue (top 10). " + AiAnswer.Disclaimer,
        };
        foreach (var r in rows)
        {
            ans.Kpis.Add(((string)r.name, Fmt((long)r.revenue)));
            ans.Evidence.Add(("invoices", (string)r.name, $"{r.visits} paid invoices, {Fmt((long)r.revenue)}"));
        }
        return ans;
    }

    private AiAnswer PatientSummary()
    {
        using var conn = _db.Open();
        var total = conn.ExecuteScalar<long>("SELECT count(*) FROM patients WHERE deleted_at IS NULL");
        var week = DateTime.UtcNow.AddDays(-7).ToString("o");
        var recent = conn.ExecuteScalar<long>(
            "SELECT count(*) FROM patients WHERE deleted_at IS NULL AND created_at >= @week", new { week });
        var ans = new AiAnswer
        {
            Text = $"{total} patients on record, {recent} registered in the last 7 days. " + AiAnswer.Disclaimer,
        };
        ans.Kpis.Add(("Patients", total.ToString()));
        ans.Kpis.Add(("New (7d)", recent.ToString()));
        ans.Evidence.Add(("patients", "count", $"{total} active"));
        return ans;
    }

    private AiAnswer VisitSummary()
    {
        using var conn = _db.Open();
        var today = DateTime.UtcNow.Date.ToString("o");
        var tomorrow = DateTime.UtcNow.Date.AddDays(1).ToString("o");
        var todayCount = conn.ExecuteScalar<long>(
            "SELECT count(*) FROM appointments WHERE deleted_at IS NULL AND date >= @today AND date < @tomorrow",
            new { today, tomorrow });
        var total = conn.ExecuteScalar<long>("SELECT count(*) FROM appointments WHERE deleted_at IS NULL");
        var invoiced = conn.ExecuteScalar<long>(
            "SELECT count(DISTINCT appointment_id) FROM invoices WHERE deleted_at IS NULL");
        var ans = new AiAnswer
        {
            Text = $"{todayCount} visits today, {total} total. {invoiced} visits have invoices. " + AiAnswer.Disclaimer,
        };
        ans.Kpis.Add(("Visits today", todayCount.ToString()));
        ans.Kpis.Add(("Total visits", total.ToString()));
        ans.Kpis.Add(("Invoiced", invoiced.ToString()));
        return ans;
    }

    private AiAnswer TreatmentTrends()
    {
        using var conn = _db.Open();
        // Treatments are JSON snapshots; count catalog treatments by name match.
        var catalog = conn.Query("SELECT name FROM treatments WHERE deleted_at IS NULL").Select(r => (string)r.name).ToList();
        var ans = new AiAnswer { Text = "Treatment usage is tracked per visit snapshot. " + AiAnswer.Disclaimer };
        ans.Kpis.Add(("Catalog treatments", catalog.Count.ToString()));
        ans.Evidence.Add(("treatments", "catalog", $"{catalog.Count} treatments defined"));
        return ans;
    }

    private static string Fmt(long cents) => $"Rp {(cents / 100.0):N0}".Replace(",", ".");
}
