// Key-value settings store (JSON values). Single local settings row per key —
// the old app's owner-publish/version/conflict machinery is gone (PRD §5.2).

using System.Text.Json;
using Dapper;
using MedRecPlus.Core.Data;

namespace MedRecPlus.Core.Services;

public sealed class SettingsService
{
    private readonly Database _db;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public SettingsService(Database db) => _db = db;

    public T? Get<T>(string key)
    {
        using var conn = _db.Open();
        var raw = conn.QueryFirstOrDefault<string>(
            "SELECT value FROM settings WHERE key = @key", new { key });
        return raw == null ? default : JsonSerializer.Deserialize<T>(raw, JsonOpts);
    }

    public T GetOrDefault<T>(string key, T defaultValue)
    {
        var v = Get<T>(key);
        return v == null ? defaultValue : v;
    }

    public void Set<T>(string key, T value)
    {
        using var conn = _db.Open();
        conn.Execute(
            """
            INSERT INTO settings (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now
            """,
            new { key, value = JsonSerializer.Serialize(value, JsonOpts), now = DbTime.Now() });
    }

    public void Remove(string key)
    {
        using var conn = _db.Open();
        conn.Execute("DELETE FROM settings WHERE key = @key", new { key });
    }

    public void SaveReceiptSetup(ClinicProfile clinic, ReceiptLayout layout)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var (key, value) in new[] {
            (SettingKeys.ClinicProfile, JsonSerializer.Serialize(clinic, JsonOpts)),
            (SettingKeys.ReceiptDocument, JsonSerializer.Serialize(layout.Normalize(), JsonOpts)),
        })
            conn.Execute("""
                INSERT INTO settings (key, value, updated_at) VALUES (@key, @value, @now)
                ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now
                """, new { key, value, now = DbTime.Now() }, tx);
        tx.Commit();
    }
}

/// <summary>Well-known settings keys.</summary>
public static class SettingKeys
{
    public const string Currency = "currency";                 // CurrencyConfig JSON
    public const string ReceiptDocument = "receipt_document"; // receipt layout JSON
    public const string ClinicProfile = "clinic_profile";     // name, address, phone
    public const string Retention = "retention";               // per-category days
    public const string BackupSchedule = "backup_schedule";
    public const string AiSettings = "ai_settings";
    public const string Onboarding = "onboarding";             // completed tours
    public const string UiPrefs = "ui_prefs";                 // theme, language, view mode
    public const string DbKeySalt = "db_key_salt";            // base64 salt for SQLCipher key
}

public sealed class CurrencyConfig
{
    public string Code { get; set; } = "IDR";
    public string Symbol { get; set; } = "Rp";
    public int DecimalPlaces { get; set; } = 0;
    public string ThousandSeparator { get; set; } = ".";
    public string DecimalSeparator { get; set; } = ",";
    public string TaxLabel { get; set; } = "PPN";
    public string TaxType { get; set; } = Models.TaxTypes.Percent;
    public decimal TaxValue { get; set; } = 11m;
}
