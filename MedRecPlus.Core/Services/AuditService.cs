// Append-only, tamper-evident audit trail. Ports the old app's AUD-01 device log
// (SecurityService DATA_AUDIT_EVENTS) into local SQLite.
// IRON RULE: events carry IDs only — never patient names, contact data, or
// clinical content. meta is a flat JSON object of scalars.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;

namespace MedRecPlus.Core.Services;

public static class AuditEvents
{
    public const string PatientViewed = "patient_viewed";
    public const string PatientCreated = "patient_created";
    public const string PatientUpdated = "patient_updated";
    public const string PatientDeleted = "patient_deleted";
    public const string PatientErased = "patient_erased";
    public const string PatientExported = "patient_exported";
    public const string PatientPrinted = "patient_printed";
    public const string ConsentUpdated = "consent_updated";
    public const string RecordCreated = "record_created";
    public const string RecordUpdated = "record_updated";
    public const string RecordDeleted = "record_deleted";
    public const string RecordSearched = "record_searched";
    public const string AttachmentAdded = "attachment_added";
    public const string AttachmentDeleted = "attachment_deleted";
    public const string InvoiceIssued = "invoice_issued";
    public const string InvoiceStatusChanged = "invoice_status_changed";
    public const string InvoicePrinted = "invoice_printed";
    public const string InvoiceExported = "invoice_exported";
    public const string SecurityIncident = "security_incident";
    public const string AppLocked = "app_locked";
    public const string UnlockFailed = "unlock_failed";
    public const string UnlockSucceeded = "unlock_succeeded";
    public const string BackupCreated = "backup_created";
    public const string BackupRestored = "backup_restored";
    public const string RetentionPurged = "retention_purged";
}

public sealed class AuditService
{
    private readonly Database _db;
    public AuditService(Database db) => _db = db;

    /// <param name="meta">Flat scalar dictionary. Must not contain PHI.</param>
    public void Append(string eventName, string? entityType, string? entityId,
        string actor, Dictionary<string, object>? meta = null)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var prev = conn.QueryFirstOrDefault<string>(
            "SELECT chain_hash FROM audit_events ORDER BY rowid DESC LIMIT 1", transaction: tx)
            ?? "GENESIS";
        var id = Guid.NewGuid().ToString("N");
        var at = DbTime.Now();
        var metaJson = JsonSerializer.Serialize(meta ?? new Dictionary<string, object>());
        var payload = $"{prev}|{id}|{at}|{eventName}|{entityType}|{entityId}|{actor}|{metaJson}";
        var chainHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        conn.Execute(
            """
            INSERT INTO audit_events (id, at, "event", entity_type, entity_id, actor, meta, chain_hash)
            VALUES (@id, @at, @eventName, @entityType, @entityId, @actor, @metaJson, @chainHash)
            """,
            new { id, at, eventName, entityType, entityId, actor, metaJson, chainHash }, tx);
        tx.Commit();
    }

    public IEnumerable<AuditEvent> Query(string? eventName = null, string? actorLike = null,
        string? fromIso = null, string? toIso = null, int limit = 500)
    {
        using var conn = _db.Open();
        return conn.Query<AuditEvent>(
            """
            SELECT id AS Id, at AS At, "event" AS Event, entity_type AS EntityType,
                   entity_id AS EntityId, actor AS Actor, meta AS MetaJson, chain_hash AS ChainHash
            FROM audit_events
            WHERE (@eventName IS NULL OR "event" = @eventName)
              AND (@actorLike IS NULL OR actor LIKE @actorLike)
              AND (@fromIso IS NULL OR at >= @fromIso)
              AND (@toIso IS NULL OR at <= @toIso)
            ORDER BY rowid DESC
            LIMIT @limit
            """,
            new { eventName, actorLike, fromIso, toIso, limit }).ToList();
    }

    /// <summary>Verifies the hash chain. Returns false at the first broken link.</summary>
    public bool VerifyChain()
    {
        using var conn = _db.Open();
        var rows = conn.Query(
            """
            SELECT id, at, "event", entity_type, entity_id, actor, meta, chain_hash
            FROM audit_events ORDER BY rowid
            """).ToList();
        var prev = "GENESIS";
        foreach (var r in rows)
        {
            var payload = $"{prev}|{r.id}|{r.at}|{r.@event}|{r.entity_type}|{r.entity_id}|{r.actor}|{r.meta}";
            var expect = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            if (!string.Equals(expect, (string)r.chain_hash, StringComparison.OrdinalIgnoreCase))
                return false;
            prev = (string)r.chain_hash;
        }
        return true;
    }
}
