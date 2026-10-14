// Dapper repositories. Soft delete everywhere (deleted_at); reads filter it.
// Columns are snake_case in SQLite; SELECTs alias to PascalCase properties.

using Dapper;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Data;

public static class DbTime
{
    public static string Now() => DateTime.UtcNow.ToString("o");
}

public sealed class PatientRepository
{
    private readonly Database _db;
    public PatientRepository(Database db) => _db = db;

    private const string Columns = """
        id AS Id, record_number AS RecordNumber, name AS Name, age AS Age,
        gender AS Gender, phone_number AS PhoneNumber, address AS Address,
        initial_diagnosis AS InitialDiagnosis, icd_codes AS IcdCodesJson,
        consent_basis AS ConsentBasis, consent_date AS ConsentDate,
        consent_recorded_by AS ConsentRecordedBy,
        created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt
        """;

    public long Create(Patient p)
    {
        using var conn = _db.Open();
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO patients
              (record_number, name, age, gender, phone_number, address,
               initial_diagnosis, icd_codes, consent_basis, consent_date,
               consent_recorded_by, created_at, updated_at)
            VALUES
              (@RecordNumber, @Name, @Age, @Gender, @PhoneNumber, @Address,
               @InitialDiagnosis, @IcdCodesJson, @ConsentBasis, @ConsentDate,
               @ConsentRecordedBy, @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                p.RecordNumber, p.Name, p.Age, p.Gender, p.PhoneNumber, p.Address,
                p.InitialDiagnosis, p.IcdCodesJson, p.ConsentBasis, p.ConsentDate,
                p.ConsentRecordedBy, now,
            });
    }

    public Patient? GetById(long id)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Patient>(
            $"SELECT {Columns} FROM patients WHERE id = @id AND deleted_at IS NULL",
            new { id });
    }

    public Patient? GetByRecordNumber(string recordNumber)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Patient>(
            $"SELECT {Columns} FROM patients WHERE record_number = @recordNumber AND deleted_at IS NULL",
            new { recordNumber });
    }

    public IEnumerable<Patient> Search(string query, int limit = 50, int offset = 0)
    {
        using var conn = _db.Open();
        var like = $"%{query}%";
        return conn.Query<Patient>(
            $"""
             SELECT {Columns} FROM patients
             WHERE deleted_at IS NULL
               AND (name LIKE @like OR record_number LIKE @like OR phone_number LIKE @like)
             ORDER BY name COLLATE NOCASE
             LIMIT @limit OFFSET @offset
             """,
            new { like, limit, offset }).ToList();
    }

    public int Count()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>("SELECT count(*) FROM patients WHERE deleted_at IS NULL");
    }

    public void Update(Patient p)
    {
        using var conn = _db.Open();
        conn.Execute(
            """
            UPDATE patients SET
              name = @Name, age = @Age, gender = @Gender, phone_number = @PhoneNumber,
              address = @Address, initial_diagnosis = @InitialDiagnosis,
              icd_codes = @IcdCodesJson, consent_basis = @ConsentBasis,
              consent_date = @ConsentDate, consent_recorded_by = @ConsentRecordedBy,
              updated_at = @now
            WHERE id = @Id AND deleted_at IS NULL
            """,
            new
            {
                p.Id, p.Name, p.Age, p.Gender, p.PhoneNumber, p.Address,
                p.InitialDiagnosis, p.IcdCodesJson, p.ConsentBasis, p.ConsentDate,
                p.ConsentRecordedBy, now = DbTime.Now(),
            });
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE patients SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }

    /// <summary>GDPR Art. 17 hard delete. Caller must have confirmed (typed name)
    /// and checked the retention guard. Removes patient + visits + invoices +
    /// attachments metadata. Attachment FILES on disk are deleted by the caller.</summary>
    public void HardDelete(long id, SqliteConnection? conn = null, SqliteTransaction? tx = null)
    {
        var ownConn = conn == null;
        conn ??= _db.Open();
        var ownTx = tx == null;
        tx ??= conn.BeginTransaction();
        try
        {
            conn.Execute("DELETE FROM invoices WHERE patient_id = @id", new { id }, tx);
            conn.Execute("DELETE FROM appointments WHERE patient_id = @id", new { id }, tx);
            conn.Execute("DELETE FROM attachments WHERE owner_type = 'patient' AND owner_id = @id", new { id }, tx);
            conn.Execute("DELETE FROM patients WHERE id = @id", new { id }, tx);
            if (ownTx) tx.Commit();
        }
        finally
        {
            if (ownTx) tx.Dispose();
            if (ownConn) conn.Dispose();
        }
    }
}

public sealed class OperatorRepository
{
    private readonly Database _db;
    public OperatorRepository(Database db) => _db = db;

    private const string Columns = """
        id AS Id, name AS Name, role AS Role,
        created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt
        """;

    public long Create(string name, string? role)
    {
        using var conn = _db.Open();
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            "INSERT INTO operators (name, role, created_at, updated_at) VALUES (@name, @role, @now, @now); SELECT last_insert_rowid();",
            new { name, role, now });
    }

    public IEnumerable<Operator> GetAll()
    {
        using var conn = _db.Open();
        return conn.Query<Operator>(
            $"SELECT {Columns} FROM operators WHERE deleted_at IS NULL ORDER BY name COLLATE NOCASE").ToList();
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE operators SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }
}

public sealed class TreatmentRepository
{
    private readonly Database _db;
    public TreatmentRepository(Database db) => _db = db;

    private const string Columns = """
        id AS Id, name AS Name, description AS Description, price_cents AS PriceCents,
        notes AS Notes, created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt
        """;

    public long Create(string name, string? description, long priceCents, string? notes)
    {
        if (priceCents <= 0) throw new ArgumentException("price must be > 0", nameof(priceCents));
        using var conn = _db.Open();
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            "INSERT INTO treatments (name, description, price_cents, notes, created_at, updated_at) VALUES (@name, @description, @priceCents, @notes, @now, @now); SELECT last_insert_rowid();",
            new { name, description, priceCents, notes, now });
    }

    public IEnumerable<Treatment> GetAll()
    {
        using var conn = _db.Open();
        return conn.Query<Treatment>(
            $"SELECT {Columns} FROM treatments WHERE deleted_at IS NULL ORDER BY name COLLATE NOCASE").ToList();
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE treatments SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }
}
