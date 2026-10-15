// Functional smoke test for MedRecPlus.Core, run on H410M against the real
// SQLCipher build. Exercises: Argon2id password hashing, key derivation,
// encrypted DB create/open, wrong-key rejection, numbering (first value = 1),
// patient/operator/treatment CRUD, soft+hard delete, money math parity,
// audit hash-chain append/verify/tamper detection, and persistence across reopen.
// Exit code 0 = all PASS, 1 = any FAIL.

using Dapper;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;

var failures = new List<string>();
void Check(bool cond, string name)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) failures.Add(name);
}

var dir = Path.Combine(Path.GetTempPath(), "medrec-smoke-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dir);
var dbPath = Path.Combine(dir, "smoke.db");
var saltPath = Path.Combine(dir, "smoke.salt");
const string password = "Smoke-Test-Password-42";

if (args.Contains("--regressions-only"))
{
    SQLitePCL.Batteries_V2.Init();
    try
    {
        RegressionChecks.RunIcd(dir, Check);
        RegressionChecks.RunRupiah(dir, Check);
        RegressionChecks.RunSearch(dir, Check);
        Console.WriteLine(failures.Count == 0 ? "REGRESSION_RESULT=PASS" : $"REGRESSION_RESULT=FAIL({failures.Count})");
        return failures.Count == 0 ? 0 : 1;
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }
}

try
{
    // --- Password hashing (Argon2id) ---
    var hash = PasswordService.HashPassword(password);
    Check(PasswordService.VerifyPassword(password, hash), "argon2id verify correct password");
    Check(!PasswordService.VerifyPassword("wrong-password", hash), "argon2id reject wrong password");

    // --- Key derivation + encrypted DB creation ---
    var salt = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
    File.WriteAllBytes(saltPath, salt);
    var keyHex = PasswordService.DbKeyHex(password, salt);
    Check(keyHex.Length == 64, "derived key is 64 hex chars");

    var db = new Database(dbPath, keyHex);
    db.Initialize();
    Check(File.Exists(dbPath), "encrypted db file created");
    Check(db.TryUnlock(), "unlock with correct key");
    RegressionChecks.RunIcd(dir, Check);
    RegressionChecks.RunRupiah(dir, Check);
    RegressionChecks.RunSearch(dir, Check);

    // Raw bytes must not contain the plaintext patient name we'll insert later (encryption check).
    // --- Repositories ---
    var ops = new OperatorRepository(db);
    var treats = new TreatmentRepository(db);
    var patients = new PatientRepository(db);
    var numbering = new NumberingService(db);
    var audit = new AuditService(db);

    var opId = ops.Create("Dr. Smoke", "Doctor");
    Check(opId > 0, "operator created");
    var trId = treats.Create("Test Treatment", "desc", 150000, null);
    Check(trId > 0, "treatment created");

    // --- Numbering: first allocation must be 1, then increment ---
    string n1, n2, inv1;
    using (var conn = db.Open())
    using (var tx = conn.BeginTransaction())
    {
        n1 = numbering.NextPatientRecordNumber(conn, tx, 2026);
        n2 = numbering.NextPatientRecordNumber(conn, tx, 2026);
        inv1 = numbering.NextInvoiceNumber(conn, tx, 2026, 10);
        tx.Commit();
    }
    Check(n1 == "PT2026000001", $"first patient number = 1 (got {n1})");
    Check(n2 == "PT2026000002", $"second patient number = 2 (got {n2})");
    Check(inv1 == "INV-202610-0001", $"first invoice number (got {inv1})");

    // --- Patient CRUD ---
    var p = new Patient
    {
        RecordNumber = n1, Name = "Smoke Test Patient", Age = 30, Gender = "Mr.",
        PhoneNumber = "08123456789", Address = "Jl. Test 1",
        InitialDiagnosis = "Testitis", ConsentBasis = "consent",
        ConsentDate = DateTime.UtcNow.ToString("o"), ConsentRecordedBy = "Dr. Smoke",
    };
    var pid = patients.Create(p);
    Check(pid > 0, "patient created");
    var got = patients.GetById(pid);
    Check(got?.Name == "Smoke Test Patient" && got?.RecordNumber == n1, "patient read back");
    Check(patients.Search("Smoke").Any(), "patient search finds row");
    Check(patients.Count() == 1, "patient count = 1");
    got!.Address = "Jl. Test 2";
    patients.Update(got);
    Check(patients.GetById(pid)?.Address == "Jl. Test 2", "patient update persisted");
    patients.SoftDelete(pid);
    Check(patients.Count() == 0, "soft delete hides row");
    Check(patients.GetById(pid) is null, "soft-deleted row not returned");

    // Hard delete path (exercises the fixed transaction ownership).
    var pid2 = patients.Create(new Patient { RecordNumber = n2, Name = "Erase Me" });
    patients.HardDelete(pid2);
    Check(patients.GetById(pid2) is null, "hard delete removes row");

    // --- Money math parity (port of computeReviewedInvoiceTotals) ---
    var t = MoneyMath.Compute(150000, DiscountTypes.Percent, 10, TaxTypes.Percent, 11);
    Check(t.SubtotalCents == 150000, "money: subtotal");
    Check(t.DiscountCents == 15000, "money: 10% discount = 15000");
    Check(t.DiscountedSubtotalCents == 135000, "money: discounted subtotal");
    Check(t.TaxCents == 14850, "money: 11% tax on discounted = 14850");
    Check(t.TotalCents == 149850, "money: total = 149850");
    var t2 = MoneyMath.Compute(10000, DiscountTypes.Fixed, 99999, TaxTypes.None, 0);
    Check(t2.DiscountCents == 10000 && t2.TotalCents == 0, "money: fixed discount capped at subtotal");

    // --- Audit hash chain ---
    audit.Append(AuditEvents.PatientCreated, "patient", pid.ToString(), "smoke", null);
    audit.Append(AuditEvents.PatientViewed, "patient", pid.ToString(), "smoke", null);
    audit.Append(AuditEvents.InvoiceIssued, "invoice", inv1, "smoke", null);
    Check(audit.VerifyChain(), "audit chain verifies");
    using (var conn = db.Open())
        conn.Execute("UPDATE audit_events SET chain_hash = 'tampered' WHERE id = (SELECT MIN(id) FROM audit_events)");
    Check(!audit.VerifyChain(), "audit chain detects tampering");

    // --- Encryption at rest: plaintext must not appear in raw file bytes ---
    // (Clear pooled SQLite connections first, or the file stays locked.)
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    var raw = File.ReadAllBytes(dbPath);
    Check(!ContainsAscii(raw, "Smoke Test Patient"), "db file does not contain plaintext name");

    // --- Wrong key rejected ---
    var wrongSalt = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(wrongSalt);
    var wrongDb = new Database(dbPath, PasswordService.DbKeyHex("wrong-password", wrongSalt));
    Check(!wrongDb.TryUnlock(), "wrong password rejected");

    // --- Persistence across reopen ---
    var db2 = new Database(dbPath, PasswordService.DbKeyHex(password, File.ReadAllBytes(saltPath)));
    Check(db2.TryUnlock(), "reopen with salt file + password");
    Check(new OperatorRepository(db2).GetAll().Any(o => o.Name == "Dr. Smoke"), "data persists across reopen");
}
catch (Exception ex)
{
    Console.WriteLine("FAIL exception: " + ex);
    failures.Add("exception: " + ex.GetType().Name);
}
finally
{
    try { Directory.Delete(dir, true); } catch { }
}

Console.WriteLine(failures.Count == 0 ? "SMOKE_RESULT=PASS" : $"SMOKE_RESULT=FAIL({failures.Count})");
return failures.Count == 0 ? 0 : 1;

static bool ContainsAscii(byte[] haystack, string needle)
{
    var n = System.Text.Encoding.ASCII.GetBytes(needle);
    for (var i = 0; i + n.Length <= haystack.Length; i++)
    {
        var found = true;
        for (var j = 0; j < n.Length; j++)
            if (haystack[i + j] != n[j]) { found = false; break; }
        if (found) return true;
    }
    return false;
}
