using ClosedXML.Excel;
using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;

internal static class UpdateChecks
{
    internal static void KeyIsolation(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "key-isolation.db");
        var valid = new Database(path, new string('a', 64));
        valid.Initialize();
        check(valid.TryUnlock(), "key isolation: valid key opens database");
        check(!new Database(path, new string('b', 64)).TryUnlock(), "key isolation: distinct raw key rejected while a valid handle was pooled");
    }

    internal static void Run(string directory, Action<bool, string> check)
    {
        var original = AppData.BaseDir;
        AppData.BaseDir = Path.Combine(directory, "security");
        try
        {
            check(LocalAppSettings.Load() == new SecuritySettings(), "security: missing settings use locked/15-minute defaults");
            LocalAppSettings.Save(new SecuritySettings { RequirePassword = false, AutoLockMinutes = 30 });
            check(!LocalAppSettings.Load().RequirePassword && LocalAppSettings.Load().AutoLockMinutes == 30, "security: settings round-trip");
            File.WriteAllText(LocalAppSettings.FilePath, "{bad-json");
            check(LocalAppSettings.Load() == new SecuritySettings() && File.ReadAllText(LocalAppSettings.FilePath).Contains("require_password"), "security: corrupt file safely repaired");
            File.WriteAllText(LocalAppSettings.FilePath, "{\"autolock_minutes\":-5}");
            check(LocalAppSettings.Load().AutoLockMinutes == 0 && LocalAppSettings.Load().RequirePassword, "security: negative timeout becomes Never and absent password flag defaults ON");
            File.WriteAllText(LocalAppSettings.FilePath, "null");
            check(LocalAppSettings.Load() == new SecuritySettings(), "security: null JSON repaired");
            LocalAppSettings.Save(new SecuritySettings());
            CredentialStore.DeleteDbKey();
            check(!CredentialStore.TryGetDbKey(out _), "vault: missing key returns false without throwing");
            var key = new string('a', 64);
            if (CredentialStore.SaveDbKey(key))
            {
                check(CredentialStore.TryGetDbKey(out var saved) && saved == key, "vault: Windows key round-trip");
                check(CredentialStore.SaveDbKey(new string('b', 64)) && CredentialStore.TryGetDbKey(out saved) && saved == new string('b', 64), "vault: replace existing key");
            }
            else Console.WriteLine("SKIP vault round-trip: Windows Credential Locker unavailable");
            CredentialStore.DeleteDbKey();
            CredentialStore.DeleteDbKey();
            check(!CredentialStore.TryGetDbKey(out _) && !CredentialStore.SaveDbKey("invalid"), "vault: deletion idempotent and invalid keys rejected");

            var setupDb = AppData.Setup("Update-Test-Password-42!", "Sample clinic", "Sample clinician");
            check(LocalAppSettings.Load() == new SecuritySettings(), "security: setup writes defaults");
            check(AppData.Unlock("incorrect") == null, "security: wrong password cannot prove ownership");
            var cached = CredentialStore.SaveDbKey(AppData.DeriveDbKey("Update-Test-Password-42!"));
            LocalAppSettings.Save(new SecuritySettings { RequirePassword = false });
            if (cached)
            {
                check(AppData.TrySilentUnlock()?.TryUnlock() == true, "security: password-free startup unlocks encrypted database");
                LocalAppSettings.Save(new SecuritySettings());
                check(AppData.TrySilentUnlock() == null, "security: password requirement ignores cached key");
                LocalAppSettings.Save(new SecuritySettings { RequirePassword = false });
                CredentialStore.SaveDbKey(new string('0', 64));
                check(AppData.TrySilentUnlock() == null, "security: stale vault key falls back to unlock");
                CredentialStore.SaveDbKey(AppData.DeriveDbKey("Update-Test-Password-42!"));
                var changed = AppData.ChangePassword(setupDb, "Update-Test-Password-42!", "Changed-Password-43!", out var refreshFailed);
                check(!refreshFailed && changed.TryUnlock() && AppData.TrySilentUnlock()?.TryUnlock() == true, "security: password change refreshes cached key");
            }
            CredentialStore.DeleteDbKey();
            check(AppData.TrySilentUnlock() == null, "security: missing vault falls back to unlock");
            AppData.WipeAllData();
            check(AppData.IsFirstRun && File.Exists(LocalAppSettings.FilePath) && !CredentialStore.TryGetDbKey(out _), "security: wipe removes database and key, preserves app.json");
        }
        finally { CredentialStore.DeleteDbKey(); AppData.BaseDir = original; }

        var db = new Database(Path.Combine(directory, "export.db"), new string('c', 64));
        db.Initialize();
        var patients = new PatientRepository(db);
        var first = patients.Create(new Patient { RecordNumber = "001", Name = "Dewi Sari – é", Age = 31, PhoneNumber = "081234567890", ConsentBasis = "explicit_consent" });
        var second = patients.Create(new Patient { RecordNumber = "002", Name = "=SUM(A1:A2)", Age = 40 });
        var deleted = patients.Create(new Patient { RecordNumber = "003", Name = "Deleted patient", Age = 20 });
        var operators = new OperatorRepository(db);
        var op1 = operators.Create("Dr: Sample / Clinician With A Very Long Name A", "Doctor");
        var op2 = operators.Create("Dr: Sample / Clinician With A Very Long Name B", "Doctor");
        operators.Create("Inactive", "Doctor");
        var visits = new AppointmentRepository(db);
        var invoices = new InvoiceRepository(db);
        long Visit(long patient, long op, string date) => visits.Create(new Appointment
        {
            PatientId = patient, PatientName = patient == first ? "Dewi Sari – é" : "Second patient",
            OperatorId = op, OperatorName = "Snapshot clinician", Date = date,
            TotalPriceCents = 100000, TreatmentsJson = "[{\"Name\":\"Consultation\",\"PriceCents\":100000}]",
            DiagnosisCodesJson = "[{\"Code\":\"A09\"},{\"code\":\"J06.9\"}]",
        });
        long Invoice(long visit, long patient, long op, string date, string status = "unpaid") => invoices.Create(new Invoice
        {
            InvoiceNumber = $"TEST-{visit}", AppointmentId = visit, PatientId = patient, PatientName = "Dewi Sari – é",
            OperatorId = op, OperatorName = "Snapshot clinician", Date = date, AppointmentDate = date,
            SubtotalCents = 100000, DiscountCents = 10000, TaxCents = 9900, TotalCents = 99900, Status = status,
        });
        var v1 = Visit(first, op1, "2026-06-15T12:00:00Z"); var i1 = Invoice(v1, first, op1, "2026-06-15T12:00:00Z");
        var v2 = Visit(second, op2, "2026-07-15T12:00:00Z"); var i2 = Invoice(v2, second, op2, "2026-07-15T12:00:00Z");
        Visit(second, op2, "2026-08-15T12:00:00Z");
        var oldVisit = Visit(first, op1, "2025-06-15T12:00:00Z"); var oldInvoice = Invoice(oldVisit, first, op1, "2025-06-15T12:00:00Z");
        var voidVisit = Visit(first, op1, "2026-09-15T12:00:00Z"); Invoice(voidVisit, first, op1, "2026-09-15T12:00:00Z", "void");
        var deletedVisit = Visit(deleted, op1, "2026-06-15T12:00:00Z"); var deletedInvoice = Invoice(deletedVisit, deleted, op1, "2026-06-15T12:00:00Z");
        using (var conn = db.Open())
        {
            void Pay(long id, long amount, string date) => conn.Execute("INSERT INTO invoice_payments(invoice_id,amount_cents,method,paid_at,created_at) VALUES(@id,@amount,'cash',@date,@date)", new { id, amount, date });
            Pay(i1, 20000, "2026-06-16T12:00:00Z"); Pay(i1, 10000, "2027-01-01T12:00:00Z");
            Pay(i2, 99900, "2026-07-16T12:00:00Z"); Pay(oldInvoice, 15000, "2026-02-01T12:00:00Z"); Pay(deletedInvoice, 500, "2026-02-01T12:00:00Z");
            conn.Execute("UPDATE invoices SET paid_cents=30000 WHERE id=@i1; UPDATE invoices SET paid_cents=99900,status='paid' WHERE id=@i2", new { i1, i2 });
        }
        patients.SoftDelete(deleted);
        var service = new ExcelExportService(db);
        var data = service.ExportDataPointsWithCounts();
        using (var book = new XLWorkbook(new MemoryStream(data.Bytes)))
        {
            check(book.Worksheets.Select(s => s.Name).SequenceEqual(new[] { "Patients", "Visits", "Invoices", "Payments" }), "excel: four sheets in exact order");
            check(data.Patients == 2 && data.Visits == 5 && data.Invoices == 4 && book.Worksheet("Payments").LastRowUsed()!.RowNumber() == 5, "excel: row counts exclude deleted patient and related records");
            var p = book.Worksheet("Patients"); var i = book.Worksheet("Invoices");
            check(p.Cell(1, 1).GetString() == "RecordNo" && p.Cell(1, 12).GetString() == "TotalPaidRp" && i.Cell(1, 12).GetString() == "Status", "excel: headers match contract");
            check(p.Cell(2, 6).DataType == XLDataType.Text && p.Cell(2, 6).GetString() == "081234567890", "excel: phone leading zero preserved as text");
            check(p.Cell(2, 3).IsEmpty() && p.Cell(2, 4).GetDouble() == 31, "excel: unknown DOB blank, recorded age retained");
            check(i.Cell(2, 9).DataType == XLDataType.Number && i.Cell(2, 9).GetDouble() == 999 && i.Cell(2, 9).Style.NumberFormat.Format == "#,##0", "excel: cents convert to numeric rupiah");
            check(p.Cell(3, 2).GetString() == "=SUM(A1:A2)" && !p.Cell(3, 2).HasFormula && p.Cell(2, 2).GetString() == "Dewi Sari – é", "excel: user text never becomes a formula and Unicode survives");
            check(book.Worksheet("Visits").Cell(3, 5).GetString() == "A09; J06.9", "excel: ICD codes joined from stored JSON");
            check(book.Worksheets.All(s => s.AutoFilter.IsEnabled && s.SheetView.SplitRow == 1 && s.Row(1).Style.Font.Bold && s.ColumnsUsed().All(c => c.Width <= 40.1)), "excel: shared filter, frozen bold headers and bounded widths");
            check(i.Cell(2, 2).DataType == XLDataType.DateTime, "excel: dates stored as sortable dates");
        }
        using (var range = new XLWorkbook(new MemoryStream(service.ExportDataPoints(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)))))
            check(range.Worksheet("Visits").LastRowUsed()!.RowNumber() == 2 && range.Worksheet("Payments").LastRowUsed()!.RowNumber() == 2, "excel: inclusive date ranges filter visits and payments independently");
        using (var performance = new XLWorkbook(new MemoryStream(service.ExportOperatorPerformance(2026, null))))
        {
            var s = performance.Worksheet("Summary_2026");
            check(performance.Worksheets.Count == 3 && s.LastRowUsed()!.RowNumber() == 4, "performance: active operators only plus TOTAL");
            check(s.Cell(2, 2).GetDouble() == 2 && s.Cell(2, 3).GetDouble() == 1 && s.Cell(2, 4).GetDouble() == 999, "performance: visits include void visit, issued count and gross exclude void invoice");
            check(s.Cell(2, 5).GetDouble() == 350 && s.Cell(2, 6).GetDouble() == 799, "performance: cross-year receipt counted, future receipt excluded from annual balance");
            check(s.Cell(4, 4).GetDouble() == 1998 && s.Cell(4, 5).GetDouble() == 1349 && s.Cell(4, 6).GetDouble() == 799, "performance: TOTAL math reconciles");
            check(performance.Worksheets.All(w => w.Name.Length <= 31 && !w.Name.Any(c => "[]:*?/\\".Contains(c))) && performance.Worksheet(3).Name.EndsWith(" (2)"), "performance: invalid and colliding operator sheet names sanitized");
        }
        using (var single = new XLWorkbook(new MemoryStream(service.ExportOperatorPerformance(2026, op1))))
            check(single.Worksheets.Count == 2 && single.Worksheet(1).LastRowUsed()!.RowNumber() == 3, "performance: single operator summary and detail");
        using (var empty = new XLWorkbook(new MemoryStream(service.ExportOperatorPerformance(2024, null))))
            check(empty.Worksheets.Count == 1 && empty.Worksheet(1).Cell(2, 1).GetString() == "TOTAL", "performance: empty year exports clean summary");
        try { service.ExportOperatorPerformance(1999, null); check(false, "performance: invalid year rejected"); }
        catch (ArgumentOutOfRangeException) { check(true, "performance: invalid year rejected"); }
        db.Revoke();
        try { db.Open(); check(false, "security: stale database cannot reopen after session revocation"); }
        catch (ObjectDisposedException) { check(true, "security: stale database cannot reopen after session revocation"); }
        try { db.ForPath(db.DbPath); check(false, "security: stale database cannot copy its key into a fresh wrapper"); }
        catch (ObjectDisposedException) { check(true, "security: stale database cannot copy its key into a fresh wrapper"); }
    }
}
