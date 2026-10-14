// Domain entities for MedRecPlus Offline. Mirrors PRD §3 (SQLite schema).
// Money is ALWAYS integer cents (long). Dates are ISO-8601 strings (UTC).
// JSON columns (vital_signs, treatments, icd_codes, ...) are stored as TEXT.

namespace MedRecPlus.Core.Models;

/// <summary>GDPR-style lawful basis. Stored as snake_case strings, never defaulted.</summary>
public static class ConsentBases
{
    public const string ExplicitConsent = "explicit_consent";
    public const string Contract = "contract";
    public const string LegalObligation = "legal_obligation";
    public const string VitalInterests = "vital_interests";
    public const string PublicHealth = "public_health";

    public static readonly string[] All =
        { ExplicitConsent, Contract, LegalObligation, VitalInterests, PublicHealth };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}

/// <summary>Invoice lifecycle. 'pending' from the old app is deliberately NOT ported (dead status).</summary>
public static class InvoiceStatuses
{
    public const string Unpaid = "unpaid";
    public const string Paid = "paid";
    public const string Void = "void";
}

public static class DiscountTypes
{
    public const string None = "none";
    public const string Percent = "percent";
    public const string Fixed = "fixed";
}

public static class TaxTypes
{
    public const string None = "none";
    public const string Percent = "percent";
    public const string Fixed = "fixed";
}

public sealed class Patient
{
    public long Id { get; set; }
    public string RecordNumber { get; set; } = "";   // PT{YYYY}{seq:D6}
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string? Gender { get; set; }              // Mr. | Ms. | Mx.
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? InitialDiagnosis { get; set; }
    public string? IcdCodesJson { get; set; }        // [{code,title,type,...}]
    public string? ConsentBasis { get; set; }
    public string? ConsentDate { get; set; }
    public string? ConsentRecordedBy { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? DeletedAt { get; set; }
}

public sealed class Operator
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }                // free-text label, e.g. "Doctor"
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? DeletedAt { get; set; }
}

public sealed class Treatment
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long PriceCents { get; set; }
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? DeletedAt { get; set; }
}

public sealed class CustomExamination
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}

public sealed class Appointment
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public string PatientName { get; set; } = "";    // denormalized snapshot
    public long OperatorId { get; set; }
    public string OperatorName { get; set; } = "";   // denormalized snapshot
    public string Date { get; set; } = "";           // visit datetime, ISO
    public string VitalSignsJson { get; set; } = "{}";
    public string TreatmentsJson { get; set; } = "[]"; // snapshot of TreatmentItems
    public long TotalPriceCents { get; set; }        // Σ treatments, pre-discount
    public string DiscountType { get; set; } = DiscountTypes.None;
    public long DiscountValueCents { get; set; }     // percent as whole number, or cents
    public string? Notes { get; set; }
    public string? Description { get; set; }
    public string? DiagnosisCodesJson { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? DeletedAt { get; set; }
}

public sealed class Invoice
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = "";  // INV-{YYYYMM}-{seq:D4}
    public long AppointmentId { get; set; }          // UNIQUE: one invoice per appointment
    public long PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public long OperatorId { get; set; }
    public string OperatorName { get; set; } = "";
    public string Date { get; set; } = "";           // issue date, ISO
    public string AppointmentDate { get; set; } = "";
    public string VitalSignsJson { get; set; } = "{}";
    public string TreatmentsJson { get; set; } = "[]";
    public long SubtotalCents { get; set; }
    public string DiscountType { get; set; } = DiscountTypes.None;
    public long DiscountValueCents { get; set; }
    public long DiscountCents { get; set; }
    public string? TaxLabel { get; set; }
    public string? TaxType { get; set; }
    public long? TaxValueCents { get; set; }
    public long? TaxCents { get; set; }
    public long TotalCents { get; set; }
    public long PaidCents { get; set; }               // denormalized Σ invoice_payments
    public string Status { get; set; } = InvoiceStatuses.Unpaid;
    public string? PaymentMethod { get; set; }        // last payment method (NEW vs old app)
    public string? PaymentNote { get; set; }
    public string? DocumentProfileJson { get; set; }  // frozen receipt-settings pin
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? DeletedAt { get; set; }
}

/// <summary>Payment methods. The old app had none; v1 adds minimal tracking.</summary>
public static class PaymentMethods
{
    public const string Cash = "cash";
    public const string Transfer = "transfer";
    public const string Qris = "qris";
    public const string Card = "card";
    public const string Other = "other";

    public static readonly string[] All = { Cash, Transfer, Qris, Card, Other };
}

public sealed class InvoicePayment
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long AmountCents { get; set; }
    public string Method { get; set; } = PaymentMethods.Cash;
    public string? Note { get; set; }
    public string PaidAt { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class Attachment
{
    public long Id { get; set; }
    public string OwnerType { get; set; } = "";      // "patient" | "appointment"
    public long OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[]? Thumbnail { get; set; }           // ≤20 KB
    public string? Description { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class AuditEvent
{
    public string Id { get; set; } = "";
    public string At { get; set; } = "";
    public string Event { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }            // IDs only — NEVER names/PHI
    public string Actor { get; set; } = "";
    public string MetaJson { get; set; } = "{}";     // scalars only
    public string ChainHash { get; set; } = "";      // SHA-256 chained
}

/// <summary>JSON shape stored in appointments/invoices treatments columns.</summary>
public sealed class TreatmentItem
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long PriceCents { get; set; }
    public string? Notes { get; set; }
}

/// <summary>JSON shape for ICD-11 / ICF code chips.</summary>
public sealed class IcdCode
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "icd11";      // icd11 | icf
    public string? LinearizationUri { get; set; }
    public string? FoundationUri { get; set; }
}
