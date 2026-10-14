// Local record/invoice numbering. Replaces the old app's Postgres sequence
// (patients) and client-side MAX+1 scan (invoices) with an atomic
// sequences-table allocation inside the caller's transaction.
//
// Patient: PT{YYYY}{seq:D6}   e.g. PT2026000142
// Invoice: INV-{YYYYMM}-{seq:D4}  e.g. INV-202610-0042
// Numbers are never reused; soft-deleted rows keep theirs (UNIQUE enforced).

using Dapper;
using MedRecPlus.Core.Data;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Services;

public sealed class NumberingService
{
    private readonly Database _db;
    public NumberingService(Database db) => _db = db;

    /// <summary>Allocates the next value for (name, period) atomically.</summary>
    public static long Allocate(SqliteConnection conn, SqliteTransaction tx, string name, string period)
    {
        var allocated = conn.QueryFirstOrDefault<long?>(
            "UPDATE sequences SET next_value = next_value + 1 WHERE name = @name AND period = @period RETURNING next_value - 1",
            new { name, period }, tx);
        if (allocated.HasValue) return allocated.Value;

        // First allocation for this period: seed next_value = 1 so the UPDATE
        // below allocates 1 (a concurrent loser of the INSERT race allocates 2).
        conn.Execute(
            "INSERT INTO sequences (name, period, next_value) VALUES (@name, @period, 1) ON CONFLICT(name, period) DO NOTHING",
            new { name, period }, tx);
        allocated = conn.QueryFirstOrDefault<long?>(
            "UPDATE sequences SET next_value = next_value + 1 WHERE name = @name AND period = @period RETURNING next_value - 1",
            new { name, period }, tx);
        return allocated ?? 1;
    }

    public string NextPatientRecordNumber(SqliteConnection conn, SqliteTransaction tx, int year)
    {
        var seq = Allocate(conn, tx, "patient_record_number", year.ToString());
        return $"PT{year}{seq:D6}";
    }

    public string NextInvoiceNumber(SqliteConnection conn, SqliteTransaction tx, int year, int month)
    {
        var period = $"{year}{month:D2}";
        var seq = Allocate(conn, tx, "invoice_number", period);
        return $"INV-{period}-{seq:D4}";
    }

    /// <summary>
    /// Seeds counters above imported maxima (used by the old-app backup importer,
    /// PRD §13). After this, the next allocation continues past existing numbers.
    /// </summary>
    public static void SeedAbove(SqliteConnection conn, SqliteTransaction tx,
        string name, string period, long existingMax)
    {
        conn.Execute(
            """
            INSERT INTO sequences (name, period, next_value) VALUES (@name, @period, @next)
            ON CONFLICT(name, period) DO UPDATE SET next_value = max(next_value, @next)
            """,
            new { name, period, next = existingMax + 1 }, tx);
    }
}
