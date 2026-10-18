using System.Globalization;
using Dapper;

namespace MedRecPlus.Core.Data;

public enum DashboardMetric { Patients, Appointments, Revenue }

public sealed record DashboardDay(DateTime Date, long Value);

public sealed class DashboardPatient
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string RecordNumber { get; set; } = "";
    public int Age { get; set; }
    public int Visits { get; set; }
}

/// <summary>Uncapped dashboard aggregates, bucketed by the clinic PC's local calendar days.</summary>
public sealed class DashboardRepository(Database db)
{
    public IReadOnlyList<DashboardDay> Trend(DashboardMetric metric, int days, DateTime? today = null)
    {
        if (days is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(days));
        var end = (today ?? DateTime.Today).Date.AddDays(1);
        var start = end.AddDays(-days);
        // These identifiers are fixed application constants, never user input.
        var (table, column, value, filter) = metric switch
        {
            DashboardMetric.Patients => ("patients", "created_at", "COUNT(*)", ""),
            DashboardMetric.Appointments => ("appointments", "date", "COUNT(*)", ""),
            // Match the Reports revenue definition: fully paid invoices by issue date.
            DashboardMetric.Revenue => ("invoices", "date", "SUM(paid_cents)", "AND status = 'paid'"),
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
        using var conn = db.Open();
        var values = conn.Query<DailyAggregate>($"""
            SELECT date({column}, 'localtime') AS Day, {value} AS Value
            FROM {table}
            WHERE deleted_at IS NULL {filter}
              AND julianday({column}) >= julianday(@from)
              AND julianday({column}) < julianday(@to)
            GROUP BY date({column}, 'localtime')
            """, new { from = LocalUtc(start), to = LocalUtc(end) })
            .ToDictionary(row => row.Day, row => row.Value);
        return Enumerable.Range(0, days).Select(offset =>
        {
            var date = start.AddDays(offset);
            return new DashboardDay(date, values.GetValueOrDefault(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }).ToArray();
    }

    public IReadOnlyList<DashboardPatient> RecentPatients(int days, DateTime? today = null)
    {
        if (days is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(days));
        var end = (today ?? DateTime.Today).Date.AddDays(1);
        using var conn = db.Open();
        return conn.Query<DashboardPatient>("""
            SELECT p.id AS Id, p.name AS Name, p.record_number AS RecordNumber, p.age AS Age,
                   COUNT(a.id) AS Visits
            FROM patients p
            LEFT JOIN appointments a ON a.patient_id = p.id AND a.deleted_at IS NULL
            WHERE p.deleted_at IS NULL
            GROUP BY p.id
            HAVING (julianday(p.created_at) >= julianday(@from) AND julianday(p.created_at) < julianday(@to))
                OR MAX(CASE WHEN julianday(a.date) >= julianday(@from) AND julianday(a.date) < julianday(@to) THEN 1 ELSE 0 END) = 1
            ORDER BY MAX(julianday(p.created_at), COALESCE(MAX(julianday(a.date)), 0)) DESC, p.id DESC
            LIMIT 4
            """, new { from = LocalUtc(end.AddDays(-days)), to = LocalUtc(end) }).ToList();
    }

    public (int Count, long BalanceCents) Outstanding()
    {
        using var conn = db.Open();
        var result = conn.QuerySingle<OutstandingAggregate>("""
            SELECT COUNT(*) AS Count, COALESCE(SUM(total_cents - paid_cents), 0) AS BalanceCents
            FROM invoices WHERE deleted_at IS NULL AND status = 'unpaid'
            """);
        return (result.Count, result.BalanceCents);
    }

    private static string LocalUtc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime().ToString("o");
    private sealed class DailyAggregate
    {
        public string Day { get; set; } = "";
        public long Value { get; set; }
    }
    private sealed class OutstandingAggregate
    {
        public int Count { get; set; }
        public long BalanceCents { get; set; }
    }
}
