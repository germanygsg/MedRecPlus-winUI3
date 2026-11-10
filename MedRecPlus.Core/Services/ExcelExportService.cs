using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;

namespace MedRecPlus.Core.Services;

public sealed record DataExport(byte[] Bytes, int Patients, int Visits, int Invoices);

public sealed class ExcelExportService(Database db)
{
    public byte[] ExportDataPoints(DateOnly? from, DateOnly? to) => ExportDataPointsWithCounts(from, to).Bytes;

    public DataExport ExportDataPointsWithCounts(DateOnly? from = null, DateOnly? to = null)
    {
        if (from > to) throw new ArgumentException("From must be on or before To.");
        var data = ReadSnapshot();
        var patients = data.Patients.Where(p => InRange(p.CreatedAt, from, to)).ToList();
        var visits = data.Visits.Where(v => InRange(v.Date, from, to)).ToList();
        var invoices = data.Invoices.Where(i => InRange(i.Date, from, to)).ToList();
        var payments = data.Payments.Where(p => InRange(p.PaidAt, from, to)).ToList();
        var patientMap = data.Patients.ToDictionary(p => p.Id);
        var invoiceMap = data.Invoices.ToDictionary(i => i.Id);
        var visitInvoices = data.Invoices.ToDictionary(i => i.AppointmentId);
        var visitCounts = visits.GroupBy(v => v.PatientId).ToDictionary(g => g.Key, g => g.Count());
        var invoiced = invoices.Where(i => i.Status != InvoiceStatuses.Void).GroupBy(i => i.PatientId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalCents));
        var paid = payments.Where(p => invoiceMap[p.InvoiceId].Status != InvoiceStatuses.Void)
            .GroupBy(p => invoiceMap[p.InvoiceId].PatientId).ToDictionary(g => g.Key, g => g.Sum(p => p.AmountCents));

        using var book = new XLWorkbook();
        var sheet = ExcelStyle.Create(book, "Patients", XLColor.CornflowerBlue,
            "RecordNo", "FullName", "DateOfBirth", "Age", "Gender", "Phone", "Address", "LawfulBasis", "CreatedAt", "VisitCount", "TotalInvoicedRp", "TotalPaidRp");
        var row = 2;
        foreach (var p in patients)
        {
            Text(sheet, row, 1, p.RecordNumber); Text(sheet, row, 2, p.Name);
            // The existing schema records age, not DOB; do not invent a birth date.
            sheet.Cell(row, 4).Value = p.Age;
            Text(sheet, row, 5, p.Gender); Text(sheet, row, 6, p.PhoneNumber); Text(sheet, row, 7, p.Address);
            Text(sheet, row, 8, p.ConsentBasis); Date(sheet, row, 9, p.CreatedAt);
            sheet.Cell(row, 10).Value = visitCounts.GetValueOrDefault(p.Id);
            Money(sheet, row, 11, invoiced.GetValueOrDefault(p.Id)); Money(sheet, row, 12, paid.GetValueOrDefault(p.Id));
            row++;
        }
        ExcelStyle.Finish(sheet);

        sheet = ExcelStyle.Create(book, "Visits", XLColor.ForestGreen,
            "VisitDate", "PatientName", "PatientRecordNo", "Operator", "Diagnoses", "Treatments", "SubtotalRp", "DiscountRp", "TaxRp", "TotalRp", "InvoiceNo", "InvoiceStatus");
        row = 2;
        foreach (var v in visits)
        {
            visitInvoices.TryGetValue(v.Id, out var invoice);
            Date(sheet, row, 1, v.Date); Text(sheet, row, 2, v.PatientName);
            Text(sheet, row, 3, patientMap[v.PatientId].RecordNumber); Text(sheet, row, 4, v.OperatorName);
            Text(sheet, row, 5, JsonNames(v.DiagnosisCodesJson, "Code")); Text(sheet, row, 6, JsonNames(v.TreatmentsJson, "Name"));
            var totals = MoneyMath.Compute(v.TotalPriceCents, v.DiscountType,
                v.DiscountType == DiscountTypes.Percent ? v.DiscountValueCents : v.DiscountValueCents / 100m, TaxTypes.None, 0);
            Money(sheet, row, 7, invoice?.SubtotalCents ?? totals.SubtotalCents);
            Money(sheet, row, 8, invoice?.DiscountCents ?? totals.DiscountCents);
            Money(sheet, row, 9, invoice?.TaxCents ?? 0); Money(sheet, row, 10, invoice?.TotalCents ?? totals.TotalCents);
            Text(sheet, row, 11, invoice?.InvoiceNumber); Text(sheet, row, 12, invoice == null ? "" : Status(invoice.Status));
            row++;
        }
        ExcelStyle.Finish(sheet);

        sheet = ExcelStyle.Create(book, "Invoices", XLColor.Orange,
            "InvoiceNo", "IssueDate", "PatientName", "PatientRecordNo", "Operator", "SubtotalRp", "DiscountRp", "TaxRp", "TotalRp", "PaidRp", "BalanceRp", "Status");
        row = 2;
        foreach (var i in invoices)
        {
            Text(sheet, row, 1, i.InvoiceNumber); Date(sheet, row, 2, i.Date); Text(sheet, row, 3, i.PatientName);
            Text(sheet, row, 4, patientMap[i.PatientId].RecordNumber); Text(sheet, row, 5, i.OperatorName);
            Money(sheet, row, 6, i.SubtotalCents); Money(sheet, row, 7, i.DiscountCents); Money(sheet, row, 8, i.TaxCents ?? 0);
            Money(sheet, row, 9, i.TotalCents); Money(sheet, row, 10, i.PaidCents);
            Money(sheet, row, 11, i.Status == InvoiceStatuses.Void ? 0 : Math.Max(0, i.TotalCents - i.PaidCents));
            Text(sheet, row, 12, Status(i.Status)); row++;
        }
        ExcelStyle.Finish(sheet);

        sheet = ExcelStyle.Create(book, "Payments", XLColor.MediumPurple,
            "PaymentDate", "InvoiceNo", "PatientName", "AmountRp", "Method", "Operator", "Note");
        row = 2;
        foreach (var p in payments)
        {
            var i = invoiceMap[p.InvoiceId];
            Date(sheet, row, 1, p.PaidAt); Text(sheet, row, 2, i.InvoiceNumber); Text(sheet, row, 3, i.PatientName);
            Money(sheet, row, 4, p.AmountCents); Text(sheet, row, 5, p.Method); Text(sheet, row, 6, i.OperatorName);
            Text(sheet, row, 7, p.Note); row++;
        }
        ExcelStyle.Finish(sheet);
        return new DataExport(Bytes(book), patients.Count, visits.Count, invoices.Count);
    }

    public byte[] ExportOperatorPerformance(int year, long? operatorId)
    {
        if (year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(year));
        var data = ReadSnapshot();
        var from = new DateOnly(year, 1, 1); var to = new DateOnly(year, 12, 31);
        var visits = data.Visits.Where(v => InRange(v.Date, from, to) && (operatorId == null || v.OperatorId == operatorId)).ToList();
        var invoiceMap = data.Invoices.Where(i => i.Status != InvoiceStatuses.Void).ToDictionary(i => i.Id);
        var yearPayments = data.Payments.Where(p => InRange(p.PaidAt, from, to) && invoiceMap.ContainsKey(p.InvoiceId))
            .GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(p => p.AmountCents));
        var visitIds = visits.Select(v => v.Id).ToHashSet();
        var invoices = invoiceMap.Values.Where(i => visitIds.Contains(i.AppointmentId)).ToList();
        var collectedInvoices = invoiceMap.Values.Where(i => yearPayments.ContainsKey(i.Id) && (operatorId == null || i.OperatorId == operatorId)).ToList();
        var active = visits.Select(v => v.OperatorId).Concat(collectedInvoices.Select(i => i.OperatorId)).Distinct().ToList();
        // A selected operator still gets a detail sheet in an otherwise empty year.
        var detailIds = active.ToList();
        if (operatorId.HasValue && !detailIds.Contains(operatorId.Value)) detailIds.Add(operatorId.Value);
        string OperatorName(long id) => data.Operators.FirstOrDefault(o => o.Id == id)?.Name
            ?? data.Visits.FirstOrDefault(v => v.OperatorId == id)?.OperatorName ?? $"Operator {id}";

        using var book = new XLWorkbook();
        var summary = ExcelStyle.Create(book, $"Summary_{year}", XLColor.CornflowerBlue,
            "Operator", "Visits", "InvoicesIssued", "GrossRp", "CollectedRp", "OutstandingRp", "AvgInvoiceRp");
        var row = 2;
        foreach (var id in active.OrderBy(OperatorName).ThenBy(id => id))
        {
            var issued = invoices.Where(i => i.OperatorId == id).ToList();
            Text(summary, row, 1, OperatorName(id));
            summary.Cell(row, 2).Value = visits.Count(v => v.OperatorId == id);
            summary.Cell(row, 3).Value = issued.Count;
            Money(summary, row, 4, issued.Sum(i => i.TotalCents));
            Money(summary, row, 5, collectedInvoices.Where(i => i.OperatorId == id).Sum(i => yearPayments[i.Id]));
            Money(summary, row, 6, issued.Sum(i => Math.Max(0, i.TotalCents - yearPayments.GetValueOrDefault(i.Id))));
            if (issued.Count > 0) Money(summary, row, 7, issued.Sum(i => i.TotalCents) / (decimal)issued.Count);
            row++;
        }
        Text(summary, row, 1, "TOTAL");
        for (var col = 2; col <= 7; col++)
        {
            // The plan requests sums for every numeric column, including row averages.
            summary.Cell(row, col).Value = Enumerable.Range(2, row - 2).Sum(r => summary.Cell(r, col).IsEmpty() ? 0 : summary.Cell(r, col).GetDouble());
            if (col >= 4) summary.Cell(row, col).Style.NumberFormat.Format = "#,##0";
        }
        summary.Row(row).Style.Font.Bold = true;
        ExcelStyle.Finish(summary, row - 1);

        foreach (var id in detailIds.OrderBy(OperatorName).ThenBy(id => id))
        {
            var sheet = ExcelStyle.Create(book, SheetName(book, OperatorName(id)), XLColor.ForestGreen,
                "Date", "PatientName", "VisitRef", "InvoiceNo", "TotalRp", "PaidRp", "BalanceRp", "Status");
            row = 2;
            foreach (var visit in visits.Where(v => v.OperatorId == id))
            {
                var invoice = data.Invoices.FirstOrDefault(i => i.AppointmentId == visit.Id);
                Date(sheet, row, 1, visit.Date); Text(sheet, row, 2, visit.PatientName); Text(sheet, row, 3, visit.Id.ToString(CultureInfo.InvariantCulture));
                Text(sheet, row, 4, invoice?.InvoiceNumber);
                var total = invoice == null || invoice.Status == InvoiceStatuses.Void ? 0 : invoice.TotalCents;
                var paid = invoice == null || invoice.Status == InvoiceStatuses.Void ? 0 : yearPayments.GetValueOrDefault(invoice.Id);
                Money(sheet, row, 5, total); Money(sheet, row, 6, paid); Money(sheet, row, 7, Math.Max(0, total - paid));
                Text(sheet, row, 8, invoice == null ? "" : Status(invoice.Status)); row++;
            }
            // Carry-in receipts contribute to Collected, never to this year's issued Gross.
            foreach (var invoice in collectedInvoices.Where(i => i.OperatorId == id && !visitIds.Contains(i.AppointmentId)))
            {
                Date(sheet, row, 1, data.Payments.First(p => p.InvoiceId == invoice.Id && InRange(p.PaidAt, from, to)).PaidAt);
                Text(sheet, row, 2, invoice.PatientName); Text(sheet, row, 3, invoice.AppointmentId.ToString(CultureInfo.InvariantCulture));
                Text(sheet, row, 4, invoice.InvoiceNumber); Money(sheet, row, 5, 0);
                Money(sheet, row, 6, yearPayments[invoice.Id]); Money(sheet, row, 7, 0);
                Text(sheet, row, 8, Status(invoice.Status) + " (carry-in payment)"); row++;
            }
            ExcelStyle.Finish(sheet);
        }
        return Bytes(book);
    }

    private Snapshot ReadSnapshot()
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction(deferred: true);
        var patients = conn.Query<Patient>("""
            SELECT id Id, record_number RecordNumber, name Name, age Age, gender Gender,
            phone_number PhoneNumber, address Address, consent_basis ConsentBasis, created_at CreatedAt
            FROM patients WHERE deleted_at IS NULL ORDER BY id
            """, transaction: tx).ToList();
        var visits = conn.Query<Appointment>("""
            SELECT a.id Id, a.patient_id PatientId, a.patient_name PatientName, a.operator_id OperatorId,
            a.operator_name OperatorName, a.date Date, a.treatments TreatmentsJson, a.diagnosis_codes DiagnosisCodesJson,
            a.total_price_cents TotalPriceCents, a.discount_type DiscountType, a.discount_value_cents DiscountValueCents
            FROM appointments a JOIN patients p ON p.id = a.patient_id
            WHERE a.deleted_at IS NULL AND p.deleted_at IS NULL ORDER BY a.date, a.id
            """, transaction: tx).ToList();
        var invoices = conn.Query<Invoice>("""
            SELECT i.id Id, i.invoice_number InvoiceNumber, i.appointment_id AppointmentId, i.patient_id PatientId,
            i.patient_name PatientName, i.operator_id OperatorId, i.operator_name OperatorName, i.date Date,
            i.subtotal_cents SubtotalCents, i.discount_cents DiscountCents, i.tax_cents TaxCents,
            i.total_cents TotalCents, i.paid_cents PaidCents, i.status Status
            FROM invoices i JOIN patients p ON p.id = i.patient_id JOIN appointments a ON a.id = i.appointment_id
            WHERE i.deleted_at IS NULL AND p.deleted_at IS NULL AND a.deleted_at IS NULL ORDER BY i.date, i.id
            """, transaction: tx).ToList();
        var survivingInvoiceIds = invoices.Select(i => i.Id).ToHashSet();
        var payments = conn.Query<InvoicePayment>("""
            SELECT id Id, invoice_id InvoiceId, amount_cents AmountCents, method Method, note Note, paid_at PaidAt
            FROM invoice_payments ORDER BY paid_at, id
            """, transaction: tx).Where(p => survivingInvoiceIds.Contains(p.InvoiceId)).ToList();
        var operators = conn.Query<Operator>("SELECT id Id, name Name FROM operators ORDER BY id", transaction: tx).ToList();
        tx.Commit();
        return new(patients, visits, invoices, payments, operators);
    }

    private sealed record Snapshot(List<Patient> Patients, List<Appointment> Visits, List<Invoice> Invoices,
        List<InvoicePayment> Payments, List<Operator> Operators);

    private static DateTime? LocalDate(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var date) ? date.LocalDateTime.Date : null;
    private static bool InRange(string date, DateOnly? from, DateOnly? to)
    {
        if (from == null && to == null) return true;
        var local = LocalDate(date);
        return local.HasValue && (from == null || DateOnly.FromDateTime(local.Value) >= from)
            && (to == null || DateOnly.FromDateTime(local.Value) <= to);
    }
    private static void Date(IXLWorksheet sheet, int row, int col, string value)
    {
        var date = LocalDate(value);
        if (date.HasValue) sheet.Cell(row, col).Value = date.Value;
        sheet.Cell(row, col).Style.NumberFormat.Format = "yyyy-mm-dd";
    }
    private static void Text(IXLWorksheet sheet, int row, int col, string? value)
    {
        sheet.Cell(row, col).Style.NumberFormat.Format = "@";
        sheet.Cell(row, col).Value = value ?? "";
    }
    private static void Money(IXLWorksheet sheet, int row, int col, decimal cents)
    {
        sheet.Cell(row, col).Value = (double)(cents / 100m);
        sheet.Cell(row, col).Style.NumberFormat.Format = "#,##0";
    }
    private static string Status(string value) => value == InvoiceStatuses.Void ? "VOID" : value == InvoiceStatuses.Paid ? "PAID" : "ISSUED";
    private static string JsonNames(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        using var document = JsonDocument.Parse(json);
        return string.Join("; ", document.RootElement.EnumerateArray().Select(item => item.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals(property, StringComparison.OrdinalIgnoreCase)).Value)
            .Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()));
    }
    private static string SheetName(XLWorkbook book, string name)
    {
        var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c) && !char.IsControl(c)).ToArray()).Trim().Trim('\'');
        if (clean.Length == 0) clean = "Operator";
        var candidate = clean[..Math.Min(clean.Length, 31)];
        for (var n = 2; book.Worksheets.Any(s => s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)) || candidate.Equals("History", StringComparison.OrdinalIgnoreCase); n++)
        {
            var suffix = $" ({n})";
            candidate = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
        }
        return candidate;
    }
    private static byte[] Bytes(XLWorkbook book)
    {
        using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }
}

internal static class ExcelStyle
{
    internal static IXLWorksheet Create(XLWorkbook book, string name, XLColor color, params string[] headers)
    {
        var sheet = book.AddWorksheet(name); sheet.TabColor = color;
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        return sheet;
    }
    internal static void Finish(IXLWorksheet sheet, int? filterLastRow = null)
    {
        sheet.Range(1, 1, Math.Max(1, filterLastRow ?? sheet.LastRowUsed()!.RowNumber()), sheet.LastColumnUsed()!.ColumnNumber()).SetAutoFilter();
        sheet.ColumnsUsed().AdjustToContents(1, Math.Min(1001, sheet.LastRowUsed()!.RowNumber()), 12, 40);
        foreach (var column in sheet.ColumnsUsed()) column.Width = Math.Min(40, column.Width + 3); // Space for filter arrows.
    }
}
