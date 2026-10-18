// Repositories for visits (appointments), invoices, payments, attachments,
// custom examinations. Soft delete everywhere; reads filter deleted_at.

using Dapper;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Data;

public sealed class AppointmentRepository
{
    private readonly Database _db;
    public AppointmentRepository(Database db) => _db = db;

    private const string Columns = """
        id AS Id, patient_id AS PatientId, patient_name AS PatientName,
        operator_id AS OperatorId, operator_name AS OperatorName,
        date AS Date, vital_signs AS VitalSignsJson, treatments AS TreatmentsJson,
        total_price_cents AS TotalPriceCents, discount_type AS DiscountType,
        discount_value_cents AS DiscountValueCents, notes AS Notes,
        description AS Description, diagnosis_codes AS DiagnosisCodesJson,
        created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt
        """;

    public long Create(Appointment a)
    {
        using var conn = _db.Open();
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO appointments
              (patient_id, patient_name, operator_id, operator_name, date,
               vital_signs, treatments, total_price_cents, discount_type,
               discount_value_cents, notes, description, diagnosis_codes,
               created_at, updated_at)
            VALUES
              (@PatientId, @PatientName, @OperatorId, @OperatorName, @Date,
               @VitalSignsJson, @TreatmentsJson, @TotalPriceCents, @DiscountType,
               @DiscountValueCents, @Notes, @Description, @DiagnosisCodesJson,
               @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                a.PatientId, a.PatientName, a.OperatorId, a.OperatorName, a.Date,
                a.VitalSignsJson, a.TreatmentsJson, a.TotalPriceCents, a.DiscountType,
                a.DiscountValueCents, a.Notes, a.Description, a.DiagnosisCodesJson, now,
            });
    }

    public Appointment? GetById(long id)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Appointment>(
            $"SELECT {Columns} FROM appointments WHERE id = @id AND deleted_at IS NULL",
            new { id });
    }

    public IEnumerable<Appointment> List(string? fromIso, string? toIso, string query = "",
        int limit = 50, int offset = 0)
    {
        using var conn = _db.Open();
        var like = $"%{query}%";
        return conn.Query<Appointment>(
            $"""
             SELECT {Columns} FROM appointments
             WHERE deleted_at IS NULL
               AND (@fromIso IS NULL OR date >= @fromIso)
               AND (@toIso IS NULL OR date < @toIso)
               AND (patient_name LIKE @like OR @query = '')
             ORDER BY date DESC
             LIMIT @limit OFFSET @offset
             """,
            new { fromIso, toIso, like, query, limit, offset }).ToList();
    }

    public int Count(string? fromIso, string? toIso, string query = "")
    {
        using var conn = _db.Open();
        var like = $"%{query}%";
        return conn.ExecuteScalar<int>(
            """
            SELECT count(*) FROM appointments
            WHERE deleted_at IS NULL
              AND (@fromIso IS NULL OR date >= @fromIso)
              AND (@toIso IS NULL OR date < @toIso)
              AND (patient_name LIKE @like OR @query = '')
            """,
            new { fromIso, toIso, like, query });
    }

    public void Update(Appointment a)
    {
        using var conn = _db.Open();
        conn.Execute(
            """
            UPDATE appointments SET
              patient_id = @PatientId, patient_name = @PatientName,
              operator_id = @OperatorId, operator_name = @OperatorName,
              vital_signs = @VitalSignsJson, treatments = @TreatmentsJson,
              total_price_cents = @TotalPriceCents, discount_type = @DiscountType,
              discount_value_cents = @DiscountValueCents, notes = @Notes,
              description = @Description, diagnosis_codes = @DiagnosisCodesJson,
              updated_at = @now
            WHERE id = @Id AND deleted_at IS NULL
            """,
            new
            {
                a.Id, a.PatientId, a.PatientName, a.OperatorId, a.OperatorName,
                a.VitalSignsJson, a.TreatmentsJson, a.TotalPriceCents, a.DiscountType,
                a.DiscountValueCents, a.Notes, a.Description, a.DiagnosisCodesJson,
                now = DbTime.Now(),
            });
    }

    public void UpdateTreatmentNotes(long id, string treatmentsJson)
    {
        using var conn = _db.Open();
        conn.Execute(
            "UPDATE appointments SET treatments = @treatmentsJson, updated_at = @now WHERE id = @id AND deleted_at IS NULL",
            new { id, treatmentsJson, now = DbTime.Now() });
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE appointments SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }

    public bool HasInvoice(long appointmentId)
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>(
            "SELECT count(*) FROM invoices WHERE appointment_id = @appointmentId AND deleted_at IS NULL",
            new { appointmentId }) > 0;
    }
}

public sealed class InvoiceRepository
{
    private readonly Database _db;
    public InvoiceRepository(Database db) => _db = db;

    private const string Columns = """
        id AS Id, invoice_number AS InvoiceNumber, appointment_id AS AppointmentId,
        patient_id AS PatientId, patient_name AS PatientName,
        operator_id AS OperatorId, operator_name AS OperatorName,
        date AS Date, appointment_date AS AppointmentDate,
        vital_signs AS VitalSignsJson, treatments AS TreatmentsJson,
        subtotal_cents AS SubtotalCents, discount_type AS DiscountType,
        discount_value_cents AS DiscountValueCents, discount_cents AS DiscountCents,
        tax_label AS TaxLabel, tax_type AS TaxType, tax_value_cents AS TaxValueCents,
        tax_cents AS TaxCents, total_cents AS TotalCents, paid_cents AS PaidCents,
        status AS Status, payment_method AS PaymentMethod, payment_note AS PaymentNote,
        document_profile AS DocumentProfileJson,
        created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt
        """;

    public long Create(Invoice inv, SqliteConnection? conn = null, SqliteTransaction? tx = null)
    {
        var own = conn == null;
        conn ??= _db.Open();
        try
        {
            var now = DbTime.Now();
            return conn.ExecuteScalar<long>(
                """
                INSERT INTO invoices
                  (invoice_number, appointment_id, patient_id, patient_name,
                   operator_id, operator_name, date, appointment_date,
                   vital_signs, treatments, subtotal_cents, discount_type,
                   discount_value_cents, discount_cents, tax_label, tax_type,
                   tax_value_cents, tax_cents, total_cents, paid_cents, status,
                   payment_method, payment_note, document_profile,
                   created_at, updated_at)
                VALUES
                  (@InvoiceNumber, @AppointmentId, @PatientId, @PatientName,
                   @OperatorId, @OperatorName, @Date, @AppointmentDate,
                   @VitalSignsJson, @TreatmentsJson, @SubtotalCents, @DiscountType,
                   @DiscountValueCents, @DiscountCents, @TaxLabel, @TaxType,
                   @TaxValueCents, @TaxCents, @TotalCents, @PaidCents, @Status,
                   @PaymentMethod, @PaymentNote, @DocumentProfileJson,
                   @now, @now);
                SELECT last_insert_rowid();
                """,
                new
                {
                    inv.InvoiceNumber, inv.AppointmentId, inv.PatientId, inv.PatientName,
                    inv.OperatorId, inv.OperatorName, inv.Date, inv.AppointmentDate,
                    inv.VitalSignsJson, inv.TreatmentsJson, inv.SubtotalCents, inv.DiscountType,
                    inv.DiscountValueCents, inv.DiscountCents, inv.TaxLabel, inv.TaxType,
                    inv.TaxValueCents, inv.TaxCents, inv.TotalCents, inv.PaidCents, inv.Status,
                    inv.PaymentMethod, inv.PaymentNote, inv.DocumentProfileJson, now,
                }, tx);
        }
        finally { if (own) conn.Dispose(); }
    }

    public Invoice? GetById(long id)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Invoice>(
            $"SELECT {Columns} FROM invoices WHERE id = @id AND deleted_at IS NULL",
            new { id });
    }

    public Invoice? GetByAppointmentId(long appointmentId)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Invoice>(
            $"SELECT {Columns} FROM invoices WHERE appointment_id = @appointmentId AND deleted_at IS NULL",
            new { appointmentId });
    }

    public IEnumerable<Invoice> List(string query = "", string? status = null,
        string? fromIso = null, string? toIso = null, int limit = 50, int offset = 0)
    {
        using var conn = _db.Open();
        var like = $"%{query}%";
        return conn.Query<Invoice>(
            $"""
             SELECT {Columns} FROM invoices
             WHERE deleted_at IS NULL
               AND (@status IS NULL OR status = @status)
               AND (@fromIso IS NULL OR date >= @fromIso)
               AND (@toIso IS NULL OR date < @toIso)
               AND (invoice_number LIKE @like OR patient_name LIKE @like OR @query = '')
             ORDER BY date DESC
             LIMIT @limit OFFSET @offset
             """,
            new { query, like, status, fromIso, toIso, limit, offset }).ToList();
    }

    public int Count(string query = "", string? status = null,
        string? fromIso = null, string? toIso = null)
    {
        using var conn = _db.Open();
        var like = $"%{query}%";
        return conn.ExecuteScalar<int>(
            """
            SELECT count(*) FROM invoices
            WHERE deleted_at IS NULL
              AND (@status IS NULL OR status = @status)
              AND (@fromIso IS NULL OR date >= @fromIso)
              AND (@toIso IS NULL OR date < @toIso)
              AND (invoice_number LIKE @like OR patient_name LIKE @like OR @query = '')
            """,
            new { query, like, status, fromIso, toIso });
    }

    public void ApplyPayment(long invoiceId, long amountCents, string method, string? note,
        string newStatus, SqliteConnection conn, SqliteTransaction tx)
    {
        conn.Execute(
            """
            UPDATE invoices SET
              paid_cents = paid_cents + @amountCents,
              payment_method = @method, payment_note = @note,
              status = @newStatus, updated_at = @now
            WHERE id = @invoiceId AND deleted_at IS NULL
            """,
            new { invoiceId, amountCents, method, note, newStatus, now = DbTime.Now() }, tx);
    }

    public void SetStatus(long invoiceId, string status, SqliteConnection? conn = null,
        SqliteTransaction? tx = null)
    {
        var own = conn == null;
        conn ??= _db.Open();
        try
        {
            conn.Execute(
                "UPDATE invoices SET status = @status, updated_at = @now WHERE id = @invoiceId AND deleted_at IS NULL",
                new { invoiceId, status, now = DbTime.Now() }, tx);
        }
        finally { if (own) conn.Dispose(); }
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE invoices SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }

    public long RevenueCents(string? fromIso, string? toIso)
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<long>(
            """
            SELECT COALESCE(SUM(paid_cents), 0) FROM invoices
            WHERE deleted_at IS NULL AND status = 'paid'
              AND (@fromIso IS NULL OR date >= @fromIso)
              AND (@toIso IS NULL OR date < @toIso)
            """,
            new { fromIso, toIso });
    }
}

public sealed class PaymentRepository
{
    private readonly Database _db;
    public PaymentRepository(Database db) => _db = db;

    public long Add(long invoiceId, long amountCents, string method, string? note,
        SqliteConnection conn, SqliteTransaction tx)
    {
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO invoice_payments
              (invoice_id, amount_cents, method, note, paid_at, created_at)
            VALUES (@invoiceId, @amountCents, @method, @note, @now, @now);
            SELECT last_insert_rowid();
            """,
            new { invoiceId, amountCents, method, note, now }, tx);
    }

    public IEnumerable<InvoicePayment> ListByInvoice(long invoiceId)
    {
        using var conn = _db.Open();
        return conn.Query<InvoicePayment>(
            """
            SELECT id AS Id, invoice_id AS InvoiceId, amount_cents AS AmountCents,
                   method AS Method, note AS Note, paid_at AS PaidAt, created_at AS CreatedAt
            FROM invoice_payments WHERE invoice_id = @invoiceId ORDER BY id
            """,
            new { invoiceId }).ToList();
    }
}

public sealed class AttachmentRepository
{
    private readonly Database _db;
    public AttachmentRepository(Database db) => _db = db;

    public long Add(Attachment a)
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO attachments
              (owner_type, owner_id, file_name, mime_type, size_bytes, sha256,
               thumbnail, description, created_at)
            VALUES
              (@OwnerType, @OwnerId, @FileName, @MimeType, @SizeBytes, @Sha256,
               @Thumbnail, @Description, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                a.OwnerType, a.OwnerId, a.FileName, a.MimeType, a.SizeBytes, a.Sha256,
                a.Thumbnail, a.Description, now = DbTime.Now(),
            });
    }

    public IEnumerable<Attachment> ListByOwner(string ownerType, long ownerId)
    {
        using var conn = _db.Open();
        return conn.Query<Attachment>(
            """
            SELECT id AS Id, owner_type AS OwnerType, owner_id AS OwnerId,
                   file_name AS FileName, mime_type AS MimeType, size_bytes AS SizeBytes,
                   sha256 AS Sha256, thumbnail AS Thumbnail, description AS Description,
                   created_at AS CreatedAt
            FROM attachments WHERE owner_type = @ownerType AND owner_id = @ownerId
            ORDER BY id
            """,
            new { ownerType, ownerId }).ToList();
    }

    public Attachment? GetById(long id)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<Attachment>(
            """
            SELECT id AS Id, owner_type AS OwnerType, owner_id AS OwnerId,
                   file_name AS FileName, mime_type AS MimeType, size_bytes AS SizeBytes,
                   sha256 AS Sha256, thumbnail AS Thumbnail, description AS Description,
                   created_at AS CreatedAt
            FROM attachments WHERE id = @id
            """,
            new { id });
    }

    public void Delete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("DELETE FROM attachments WHERE id = @id", new { id });
    }
}

public sealed class CustomExaminationRepository
{
    private readonly Database _db;
    public CustomExaminationRepository(Database db) => _db = db;

    public long Create(string name, string unit)
    {
        using var conn = _db.Open();
        var now = DbTime.Now();
        return conn.ExecuteScalar<long>(
            "INSERT INTO custom_examinations (name, unit, created_at, updated_at) VALUES (@name, @unit, @now, @now); SELECT last_insert_rowid();",
            new { name, unit, now });
    }

    public IEnumerable<CustomExamination> GetAll()
    {
        using var conn = _db.Open();
        return conn.Query<CustomExamination>(
            "SELECT id AS Id, name AS Name, unit AS Unit FROM custom_examinations WHERE deleted_at IS NULL ORDER BY name COLLATE NOCASE").ToList();
    }

    public void Update(long id, string name, string unit)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE custom_examinations SET name = @name, unit = @unit, updated_at = @now WHERE id = @id",
            new { id, name, unit, now = DbTime.Now() });
    }

    public void SoftDelete(long id)
    {
        using var conn = _db.Open();
        conn.Execute("UPDATE custom_examinations SET deleted_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() });
    }
}
