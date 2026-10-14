// Encrypted backups (PRD must-have: with no cloud copy, backups are the only
// safety net). The live DB file is SQLCipher-encrypted at rest, so a
// checkpointed file copy IS an encrypted backup. Every backup is verified by
// re-opening it with the same key before it is accepted.

using Dapper;
using MedRecPlus.Core.Data;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Services;

public sealed class BackupService
{
    private readonly Database _db;
    private readonly string _dir;
    private readonly AuditService _audit;
    private const int KeepLast = 30;

    public BackupService(Database db, string backupsDir)
    {
        _db = db;
        _dir = backupsDir;
        _audit = new AuditService(db);
        Directory.CreateDirectory(_dir);
    }

    public string CreateBackup(string actor)
    {
        // Flush WAL into the main file, drop pooled connections, then copy.
        SqliteConnection.ClearAllPools();
        using (var conn = _db.Open())
            conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        SqliteConnection.ClearAllPools();

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(_dir, $"medrec-backup-{stamp}.db");
        File.Copy(_db.DbPath, path, overwrite: false);

        if (!VerifyBackup(path))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("Backup verification failed; backup discarded.");
        }

        Prune();
        _audit.Append(AuditEvents.BackupCreated, "backup", Path.GetFileName(path), actor,
            new Dictionary<string, object> { ["path"] = path });
        return path;
    }

    private bool VerifyBackup(string path)
    {
        try
        {
            var probe = _db.ForPath(path);
            if (!probe.TryUnlock()) return false;
            using var conn = probe.Open();
            var tables = conn.ExecuteScalar<long>(
                "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
            return tables > 5 && new FileInfo(path).Length > 0;
        }
        catch { return false; }
    }

    public IEnumerable<string> ListBackups() =>
        Directory.Exists(_dir)
            ? Directory.GetFiles(_dir, "medrec-backup-*.db").OrderByDescending(f => f)
            : Enumerable.Empty<string>();

    private void Prune()
    {
        foreach (var f in ListBackups().Skip(KeepLast))
            try { File.Delete(f); } catch { /* best effort */ }
    }

    /// <summary>Restores a backup over the live DB. All connections must be
    /// closed first; the caller must restart the app afterwards.</summary>
    public void RestoreBackup(string backupPath, string actor)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Backup not found.", backupPath);
        if (!VerifyBackup(backupPath))
            throw new InvalidOperationException("Backup failed verification; not restored.");
        SqliteConnection.ClearAllPools();
        File.Copy(backupPath, _db.DbPath, overwrite: true);
        _audit.Append("backup_restored", "backup", Path.GetFileName(backupPath), actor, null);
    }
}
