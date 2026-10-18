// Invoice lifecycle. Exactly one path: generated from a visit (no standalone
// create form, mirroring the old app). Totals frozen at issue time; money math
// via MoneyMath (discount before tax). Status transitions are guarded and audited.
// NEW vs old app: minimal payment-method tracking + partial payments.

using System.Text.Json;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.Core.Services;

public sealed class InvoiceService
{
    private readonly Database _db;
    private readonly InvoiceRepository _invoices;
    private readonly AppointmentRepository _visits;
    private readonly PaymentRepository _payments;
    private readonly NumberingService _numbering;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public InvoiceService(Database db)
    {
        _db = db;
        _invoices = new InvoiceRepository(db);
        _visits = new AppointmentRepository(db);
        _payments = new PaymentRepository(db);
        _numbering = new NumberingService(db);
        _settings = new SettingsService(db);
        _audit = new AuditService(db);
    }

    public Invoice GetInvoice(long id) =>
        _invoices.GetById(id) ?? throw new InvalidOperationException("Invoice not found.");

    /// <summary>Generates the (single) invoice for a visit. Idempotent: returns the
    /// existing invoice if one was already generated.</summary>
    public Invoice GenerateFromAppointment(long appointmentId, string actor)
    {
        var existing = _invoices.GetByAppointmentId(appointmentId);
        if (existing != null) return existing;

        var visit = _visits.GetById(appointmentId)
            ?? throw new InvalidOperationException("Visit not found.");

        var currency = _settings.GetOrDefault(SettingKeys.Currency, new CurrencyConfig());
        var clinic = _settings.GetOrDefault(SettingKeys.ClinicProfile, new ClinicProfile());
        var receiptLayout = ReceiptLayout.Load(_settings);

        // Discount snapshot from the visit: percent stored as whole number, fixed as cents.
        var discountValue = visit.DiscountType == DiscountTypes.Percent
            ? (decimal)visit.DiscountValueCents
            : visit.DiscountValueCents / 100m;
        var taxValue = currency.TaxType == TaxTypes.None ? 0m : currency.TaxValue;

        var totals = MoneyMath.Compute(visit.TotalPriceCents, visit.DiscountType, discountValue,
            currency.TaxType, taxValue);

        var now = DateTime.UtcNow;
        string number;
        Invoice created;
        using (var conn = _db.Open())
        using (var tx = conn.BeginTransaction())
        {
            number = _numbering.NextInvoiceNumber(conn, tx, now.Year, now.Month);
            var documentProfile = JsonSerializer.Serialize(new
            {
                issued_at = DbTime.Now(),
                currency = currency,
                clinic,
                receipt_layout = receiptLayout,
                settings_version = 2,
            });
            var inv = new Invoice
            {
                InvoiceNumber = number,
                AppointmentId = visit.Id,
                PatientId = visit.PatientId, PatientName = visit.PatientName,
                OperatorId = visit.OperatorId, OperatorName = visit.OperatorName,
                Date = DbTime.Now(), AppointmentDate = visit.Date,
                VitalSignsJson = visit.VitalSignsJson,
                TreatmentsJson = visit.TreatmentsJson,
                SubtotalCents = totals.SubtotalCents,
                DiscountType = visit.DiscountType,
                DiscountValueCents = visit.DiscountValueCents,
                DiscountCents = totals.DiscountCents,
                TaxLabel = currency.TaxLabel, TaxType = currency.TaxType,
                TaxValueCents = currency.TaxType == TaxTypes.Percent
                    ? (long?)currency.TaxValue
                    : (long?)(long)Math.Round(currency.TaxValue * 100m, MidpointRounding.AwayFromZero),
                TaxCents = totals.TaxCents,
                TotalCents = totals.TotalCents,
                PaidCents = 0,
                Status = InvoiceStatuses.Unpaid,
                DocumentProfileJson = documentProfile,
            };
            var id = _invoices.Create(inv, conn, tx);
            tx.Commit();
            created = _invoices.GetById(id)!;
        }

        _audit.Append(AuditEvents.InvoiceIssued, "invoice", created.Id.ToString(), actor,
            new Dictionary<string, object> { ["number"] = number, ["total_cents"] = created.TotalCents });
        return created;
    }

    /// <summary>Records a payment (partial allowed). Full payment flips status to paid.</summary>
    public void RecordPayment(long invoiceId, long amountCents, string method, string? note, string actor)
    {
        if (amountCents <= 0) throw new ArgumentException("Amount must be > 0.", nameof(amountCents));
        if (!PaymentMethods.All.Contains(method)) throw new ArgumentException("Unknown payment method.", nameof(method));

        var inv = GetInvoice(invoiceId);
        if (inv.Status == InvoiceStatuses.Void)
            throw new InvalidOperationException("Cannot take payment on a void invoice.");
        var remaining = inv.TotalCents - inv.PaidCents;
        if (amountCents > remaining)
            throw new InvalidOperationException($"Amount exceeds remaining balance ({remaining} cents).");

        var newPaid = inv.PaidCents + amountCents;
        var newStatus = newPaid >= inv.TotalCents ? InvoiceStatuses.Paid : InvoiceStatuses.Unpaid;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        _payments.Add(invoiceId, amountCents, method, note, conn, tx);
        _invoices.ApplyPayment(invoiceId, amountCents, method, note, newStatus, conn, tx);
        tx.Commit();

        _audit.Append(AuditEvents.InvoiceStatusChanged, "invoice", invoiceId.ToString(), actor,
            new Dictionary<string, object>
            {
                ["payment_cents"] = amountCents, ["method"] = method, ["status"] = newStatus,
            });
    }

    /// <summary>Guarded status transitions. Manual unpaid→paid requires full payment
    /// (use RecordPayment); void is terminal.</summary>
    public void SetStatus(long invoiceId, string newStatus, string actor)
    {
        if (newStatus != InvoiceStatuses.Unpaid && newStatus != InvoiceStatuses.Paid && newStatus != InvoiceStatuses.Void)
            throw new ArgumentException("Unknown status.", nameof(newStatus));

        var inv = GetInvoice(invoiceId);
        var from = inv.Status;
        if (from == newStatus) return;
        if (from == InvoiceStatuses.Void)
            throw new InvalidOperationException("Void is terminal; a void invoice cannot change status.");
        if (from == InvoiceStatuses.Paid && newStatus == InvoiceStatuses.Unpaid)
            throw new InvalidOperationException("A paid invoice cannot go back to unpaid; void it instead.");
        if (newStatus == InvoiceStatuses.Paid && inv.PaidCents < inv.TotalCents)
            throw new InvalidOperationException("Invoice is not fully paid; record the payment first.");

        _invoices.SetStatus(invoiceId, newStatus);
        _audit.Append(AuditEvents.InvoiceStatusChanged, "invoice", invoiceId.ToString(), actor,
            new Dictionary<string, object> { ["from"] = from, ["to"] = newStatus });
    }

    public void DeleteInvoice(long id, string actor)
    {
        _ = GetInvoice(id);
        _invoices.SoftDelete(id);
        _audit.Append(AuditEvents.RecordDeleted, "invoice", id.ToString(), actor, null);
    }

    public IEnumerable<InvoicePayment> GetPayments(long invoiceId) =>
        _payments.ListByInvoice(invoiceId);
}

/// <summary>Clinic profile snapshot, frozen into each invoice's document profile.</summary>
public sealed class ClinicProfile
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string HeaderText { get; set; } = "";
    public string FooterText { get; set; } = "";
}
