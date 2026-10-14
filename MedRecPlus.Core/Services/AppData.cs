// App-local data locations + first-run setup / unlock / password change.
// Layout under %LocalAppData%\MedRecPlusOffline:
//   medrec.db        SQLCipher-encrypted database
//   medrec.salt      16-byte Argon2 salt (needed BEFORE the DB can be opened)
//   attachments\     original image files (metadata + thumbnails in DB)
//   backups\         timestamped encrypted backups
// icd11.db is bundled next to the exe (or in the data dir as fallback).

using System.Security.Cryptography;
using MedRecPlus.Core.Data;

namespace MedRecPlus.Core.Services;

public static class AppData
{
    public static string BaseDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedRecPlusOffline");

    public static string DbPath => Path.Combine(BaseDir, "medrec.db");
    public static string SaltPath => Path.Combine(BaseDir, "medrec.salt");
    public static string AttachmentsDir => Path.Combine(BaseDir, "attachments");
    public static string BackupsDir => Path.Combine(BaseDir, "backups");

    /// <summary>ICD database: next to the exe (bundled) or in the data dir.</summary>
    public static string? FindIcdDb()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "icd11.db"),
            Path.Combine(BaseDir, "icd11.db"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsFirstRun => !File.Exists(DbPath);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(BackupsDir);
    }

    /// <summary>Emergency unlock: permanently deletes the encrypted database,
    /// salt, attachments, and backups. After this the app returns to first-run
    /// setup. There is no recovery.</summary>
    public static void WipeAllData()
    {
        // Pooled SQLite connections (e.g. from a failed unlock attempt) keep
        // the file handle open, which makes File.Delete silently fail on
        // Windows. Drain the pool first.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Delete the database files first (most sensitive), then the rest.
        foreach (var f in new[] { DbPath, DbPath + "-wal", DbPath + "-shm", DbPath + "-journal", SaltPath })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        foreach (var d in new[] { AttachmentsDir, BackupsDir })
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }

        if (File.Exists(DbPath))
            throw new IOException("Could not delete the database file — it is still in use. Restart the app and try again.");
    }

    /// <summary>First-run setup: validates the password, creates salt + encrypted DB,
    /// stores the clinic profile and the first operator. Returns an open Database.</summary>
    public static Database Setup(string password, string clinicName, string operatorName)
    {
        PasswordService.ValidatePolicy(password);
        if (string.IsNullOrWhiteSpace(clinicName)) throw new ArgumentException("Clinic name required.", nameof(clinicName));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Operator name required.", nameof(operatorName));

        EnsureCreated();
        if (File.Exists(DbPath)) throw new InvalidOperationException("Database already exists.");

        var salt = RandomNumberGenerator.GetBytes(16);
        File.WriteAllBytes(SaltPath, salt);
        var db = new Database(DbPath, PasswordService.DbKeyHex(password, salt));
        db.Initialize();

        var settings = new SettingsService(db);
        settings.Set(SettingKeys.ClinicProfile, new ClinicProfile { Name = clinicName.Trim() });
        settings.Set("password_hash", PasswordService.HashPassword(password));
        settings.Set("setup_at", DbTime.Now());

        var ops = new OperatorRepository(db);
        var opId = ops.Create(operatorName.Trim(), "Admin");
        new AuditService(db).Append(AuditEvents.RecordCreated, "operator", opId.ToString(), "setup", null);
        return db;
    }

    /// <summary>Unlocks the DB. Returns null when the password is wrong.</summary>
    public static Database? Unlock(string password)
    {
        if (IsFirstRun) throw new InvalidOperationException("No database yet; run setup first.");
        var salt = File.ReadAllBytes(SaltPath);
        var db = new Database(DbPath, PasswordService.DbKeyHex(password, salt));
        if (!db.TryUnlock()) return null;
        db.Initialize(); // idempotent: creates tables + runs pending migrations
        return db;
    }

    /// <summary>Changes the DB password: verifies the current one, rekeys SQLCipher,
    /// rotates the KDF salt. Returns a Database bound to the new key.</summary>
    public static Database ChangePassword(Database db, string currentPassword, string newPassword)
    {
        PasswordService.ValidatePolicy(newPassword);
        var salt = File.ReadAllBytes(SaltPath);
        var probe = new Database(DbPath, PasswordService.DbKeyHex(currentPassword, salt));
        if (!probe.TryUnlock())
            throw new InvalidOperationException("Current password is incorrect.");

        var newSalt = RandomNumberGenerator.GetBytes(16);
        var newKeyHex = PasswordService.DbKeyHex(newPassword, newSalt);
        db.Rekey(newKeyHex); // db still holds the old key; rekey runs on an old-key connection
        File.WriteAllBytes(SaltPath, newSalt);

        var newDb = new Database(DbPath, newKeyHex);
        new SettingsService(newDb).Set("password_hash", PasswordService.HashPassword(newPassword));
        new AuditService(newDb).Append(AuditEvents.SecurityIncident, "security", null, "settings",
            new Dictionary<string, object> { ["action"] = "password_changed" });
        return newDb;
    }
}
