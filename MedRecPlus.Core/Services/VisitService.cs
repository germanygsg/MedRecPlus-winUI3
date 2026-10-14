// Visit (appointment) lifecycle. A visit is a same-day record (no scheduling in v1).
// Rule (PRD): editable until invoiced, then frozen except treatment notes.

using System.Text.Json;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;

namespace MedRecPlus.Core.Services;

public sealed class VisitService
{
    private readonly Database _db;
    private readonly AppointmentRepository _repo;
    private readonly AuditService _audit;

    public VisitService(Database db)
    {
        _db = db;
        _repo = new AppointmentRepository(db);
        _audit = new AuditService(db);
    }

    public long CreateVisit(long patientId, long operatorId,
        Dictionary<string, object?> vitalSigns, List<TreatmentItem> treatments,
        string discountType, decimal discountValue, string? notes, string? description,
        List<IcdCode>? diagnosisCodes, string actor)
    {
        using var conn = _db.Open();
        var patient = conn.QueryFirstOrDefault<Patient>(
            "SELECT id AS Id, name AS Name FROM patients WHERE id = @id AND deleted_at IS NULL",
            new { id = patientId })
            ?? throw new InvalidOperationException("Patient not found.");
        var op = conn.QueryFirstOrDefault<Operator>(
            "SELECT id AS Id, name AS Name FROM operators WHERE id = @id AND deleted_at IS NULL",
            new { id = operatorId })
            ?? throw new InvalidOperationException("Operator not found.");

        if (!DiscountTypes.None.Equals(discountType) &&
            discountType != DiscountTypes.Percent && discountType != DiscountTypes.Fixed)
            throw new ArgumentException("Invalid discount type.", nameof(discountType));

        var total = treatments.Sum(t => t.PriceCents);
        // discount_value_cents: percent stored as whole number, fixed stored as cents.
        var discountValueCents = discountType == DiscountTypes.Percent
            ? (long)discountValue
            : (long)Math.Round(discountValue * 100m, MidpointRounding.AwayFromZero);

        var appt = new Appointment
        {
            PatientId = patientId, PatientName = patient.Name,
            OperatorId = operatorId, OperatorName = op.Name,
            Date = DbTime.Now(),
            VitalSignsJson = JsonSerializer.Serialize(vitalSigns),
            TreatmentsJson = JsonSerializer.Serialize(treatments),
            TotalPriceCents = total,
            DiscountType = discountType,
            DiscountValueCents = discountValueCents,
            Notes = notes, Description = description,
            DiagnosisCodesJson = diagnosisCodes == null ? null : JsonSerializer.Serialize(diagnosisCodes),
        };
        var id = _repo.Create(appt);
        _audit.Append(AuditEvents.RecordCreated, "appointment", id.ToString(), actor,
            new Dictionary<string, object> { ["patient_id"] = patientId });
        return id;
    }

    public Appointment GetVisit(long id) =>
        _repo.GetById(id) ?? throw new InvalidOperationException("Visit not found.");

    public bool IsInvoiced(long appointmentId) => _repo.HasInvoice(appointmentId);

    /// <summary>Full edit — only before an invoice exists.</summary>
    public void UpdateVisit(Appointment updated, string actor)
    {
        if (IsInvoiced(updated.Id))
            throw new InvalidOperationException("Visit is invoiced and frozen; only treatment notes can be edited.");
        _repo.Update(updated);
        _audit.Append(AuditEvents.RecordUpdated, "appointment", updated.Id.ToString(), actor, null);
    }

    /// <summary>Treatment-notes edit — allowed even after invoicing.</summary>
    public void UpdateTreatmentNotes(long id, List<TreatmentItem> treatments, string actor)
    {
        _ = GetVisit(id);
        _repo.UpdateTreatmentNotes(id, JsonSerializer.Serialize(treatments));
        _audit.Append(AuditEvents.RecordUpdated, "appointment", id.ToString(), actor,
            new Dictionary<string, object> { ["notes_only"] = true });
    }

    public void DeleteVisit(long id, string actor)
    {
        _ = GetVisit(id);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // Old app behavior: deleting a visit soft-deletes its invoice too.
        conn.Execute("UPDATE invoices SET deleted_at = @now, updated_at = @now WHERE appointment_id = @id AND deleted_at IS NULL",
            new { id, now = DbTime.Now() }, tx);
        conn.Execute("UPDATE appointments SET deleted_at = @now, updated_at = @now WHERE id = @id",
            new { id, now = DbTime.Now() }, tx);
        tx.Commit();
        _audit.Append(AuditEvents.RecordDeleted, "appointment", id.ToString(), actor, null);
    }
}
