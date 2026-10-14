// Encrypted SQLite access. Whole-DB encryption via SQLCipher; the key is a
// 32-byte value (Argon2id-derived, see PasswordService) passed as hex to
// PRAGMA key. A canary read on every Open() turns a wrong key into the
// familiar "file is not a database" error.

using Dapper;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Data;

public sealed class Database
{
    static Database()
    {
        // Microsoft.Data.Sqlite.Core does not auto-initialize the native provider;
        // without this, Open() fails with "no SQLite provider". Safe to call twice.
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly string _keyHex;

    public Database(string dbPath, string keyHex)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentException("dbPath required", nameof(dbPath));
        if (keyHex.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(keyHex, "^[0-9a-fA-F]{64}$"))
            throw new ArgumentException("keyHex must be 64 hex chars (32 bytes)", nameof(keyHex));
        _dbPath = dbPath;
        _keyHex = keyHex.ToLowerInvariant();
    }

    public string DbPath => _dbPath;

    /// <summary>Opens another database file with this instance's key
    /// (used to verify backups).</summary>
    public Database ForPath(string dbPath) => new(dbPath, _keyHex);

    /// <summary>Changes the encryption key (SQLCipher rekey). The instance keeps
    /// working only if the caller also swaps to the new key afterwards.</summary>
    public void Rekey(string newKeyHex)
    {
        using var conn = Open();
        conn.Execute($"PRAGMA rekey = \"x'{newKeyHex.ToLowerInvariant()}'\";");
    }

    /// <summary>Opens a keyed connection. Throws if the key is wrong.</summary>
    public SqliteConnection Open()
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        try
        {
            conn.Execute($"PRAGMA key = \"x'{_keyHex}'\";");
            // Canary: with a wrong key this throws "file is not a database".
            conn.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master;");
            // Busy timeout keeps single-process concurrency (UI + background timer) calm.
            conn.Execute("PRAGMA busy_timeout = 5000;");
            conn.Execute("PRAGMA journal_mode = WAL;");
            conn.Execute("PRAGMA foreign_keys = ON;");
        }
        catch
        {
            conn.Dispose();
            throw;
        }
        return conn;
    }

    /// <summary>Creates all tables. Safe to call on every startup (IF NOT EXISTS).</summary>
    public void Initialize()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var stmt in Schema.Statements)
            conn.Execute(stmt, transaction: tx);
        tx.Commit();
        // Idempotent migrations: ignore "duplicate column" errors.
        foreach (var mig in Schema.Migrations)
        {
            try { conn.Execute(mig); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
            { }
        }
    }

    /// <summary>Verifies the key without side effects. Returns false on wrong key.</summary>
    public bool TryUnlock()
    {
        try
        {
            using var conn = Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
