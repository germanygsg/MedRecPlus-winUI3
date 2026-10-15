using Dapper;
using MedRecPlus.Core.Data;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Services;

public enum SearchCategory { Patients, Appointments, Invoices }

public sealed class SearchEntry
{
    public SearchCategory Category { get; set; }
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Date { get; set; }
}

public sealed record SearchGroup(SearchCategory Category, int Total, IReadOnlyList<SearchEntry> Entries);

/// <summary>Searches live records and their patient identity across all dates.</summary>
public sealed class GlobalSearchService(Database database)
{
    public IReadOnlyList<SearchGroup> Search(string query, int limitPerCategory = 5,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(limitPerCategory, 0);
        if (string.IsNullOrWhiteSpace(query)) return [];
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        return Enum.GetValues<SearchCategory>().Select(category =>
            ReadGroup(connection, transaction, query, category, limitPerCategory, 0, cancellationToken)).ToArray();
    }

    public SearchGroup SearchCategoryPage(string query, SearchCategory category, int limit = 25,
        int offset = 0, CancellationToken cancellationToken = default)
    {
        ValidatePage(limit, offset);
        if (string.IsNullOrWhiteSpace(query)) return new(category, 0, []);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        return ReadGroup(connection, transaction, query, category, limit, offset, cancellationToken);
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
    }

    private static SearchGroup ReadGroup(SqliteConnection connection, SqliteTransaction transaction,
        string query, SearchCategory category, int limit, int offset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only these fixed SQL fragments are interpolated; all user input is parameterized.
        var (from, columns, fields, order) = category switch
        {
            SearchCategory.Patients => (
                "patients p WHERE p.deleted_at IS NULL",
                "p.id AS Id, p.name AS Title, p.record_number || COALESCE(' · ' || NULLIF(p.phone_number, ''), '') AS Description",
                new[] { "p.name", "p.record_number", "p.phone_number" },
                "p.name COLLATE NOCASE, p.id"),
            SearchCategory.Appointments => (
                "appointments a JOIN patients p ON p.id = a.patient_id WHERE a.deleted_at IS NULL AND p.deleted_at IS NULL",
                "a.id AS Id, p.name AS Title, p.record_number || ' · ' || a.operator_name AS Description, a.date AS Date",
                new[] { "p.name", "p.record_number", "p.phone_number", "a.patient_name", "a.operator_name", "a.date", "a.description" },
                "a.date DESC, a.id DESC"),
            SearchCategory.Invoices => (
                "invoices i JOIN patients p ON p.id = i.patient_id WHERE i.deleted_at IS NULL AND p.deleted_at IS NULL",
                "i.id AS Id, i.invoice_number AS Title, p.name || ' · ' || p.record_number || ' · ' || i.status AS Description, i.date AS Date",
                new[] { "p.name", "p.record_number", "p.phone_number", "i.patient_name", "i.invoice_number", "i.operator_name", "i.status", "i.date" },
                "i.date DESC, i.id DESC"),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        var parameters = new DynamicParameters(new { limit, offset });
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filters = words.Select((word, index) =>
        {
            var name = $"word{index}";
            // LIKE metacharacters should find literal text, never expand the search.
            parameters.Add(name, "%" + word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            return " AND (" + string.Join(" OR ", fields.Select(field => $"{field} LIKE @{name} ESCAPE '\\'")) + ")";
        });
        var where = from + string.Concat(filters);
        using var reader = connection.QueryMultiple(new CommandDefinition($"""
            SELECT COUNT(*) FROM {where};
            SELECT {columns} FROM {where} ORDER BY {order} LIMIT @limit OFFSET @offset;
            """, parameters, transaction, cancellationToken: cancellationToken));
        var total = reader.ReadSingle<int>();
        var entries = reader.Read<SearchEntry>().ToArray();
        foreach (var entry in entries) entry.Category = category;
        return new(category, total, entries);
    }
}
