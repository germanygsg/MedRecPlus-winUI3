// ICD-11 / ICF search over the bundled icd11.db (read-only). Ports the exact
// relevance algorithm from the old Tauri app (LIKE-based: exact code → code
// prefix → title prefix → substring). Gracefully unavailable when the DB
// isn't bundled.

using Dapper;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Services;

public sealed class IcdService
{
    private const string Select =
        "SELECT code, title, linearization, chapter, is_leaf, entity_uri, foundation_uri FROM icd_entities";

    private readonly string? _dbPath;

    public IcdService(string? dbPath)
    {
        _dbPath = dbPath;
    }

    public bool IsAvailable => _dbPath != null && File.Exists(_dbPath);

    public IEnumerable<IcdCode> Search(string query, string linearization = "mms", int limit = 20)
    {
        if (!IsAvailable) return Enumerable.Empty<IcdCode>();
        var q = query.Trim();
        if (q.Length < 2) return Enumerable.Empty<IcdCode>();
        limit = Math.Clamp(limit, 1, 100);

        var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var contains = $"%{escaped}%";
        var prefix = $"{escaped}%";

        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        return conn.Query(
            $"""
             {Select} WHERE linearization = @lin
               AND (code LIKE @contains ESCAPE '\' OR title LIKE @contains ESCAPE '\')
             ORDER BY CASE WHEN code = @q THEN 0
                           WHEN code LIKE @prefix ESCAPE '\' THEN 1
                           WHEN title LIKE @prefix ESCAPE '\' THEN 2
                           ELSE 3 END, title
             LIMIT @limit
             """,
            new { lin = linearization, contains, q, prefix, limit })
            .Select(r => new IcdCode
            {
                Code = (string)r.code,
                Title = StripHtml((string)r.title),
                Type = linearization == "icf" ? "icf" : "icd11",
                LinearizationUri = (string?)r.entity_uri,
                FoundationUri = (string?)r.foundation_uri,
            }).ToList();
    }

    public IcdCode? Lookup(string code)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(code)) return null;
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        var r = conn.QueryFirstOrDefault(
            $"""
             {Select} WHERE code = @code
             ORDER BY CASE WHEN linearization = 'mms' THEN 0 ELSE 1 END LIMIT 1
             """,
            new { code = code.Trim() });
        if (r == null) return null;
        return new IcdCode
        {
            Code = (string)r.code,
            Title = StripHtml((string)r.title),
            Type = "icd11",
            LinearizationUri = (string?)r.entity_uri,
            FoundationUri = (string?)r.foundation_uri,
        };
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
}
