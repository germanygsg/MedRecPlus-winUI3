# Update Plan — Auto-lock, Optional App Lock, Excel Export

**Target repo:** `MedRecPlus-winUI3` (H410M working tree `F:\app\medrecplus-winui`)
**Author:** Abdul (Muse) — 2026-10-18
**Scope:** three features. No cloud, no new infrastructure, no schema redesign.
All work is local-first and must keep the existing E2E flows green.

## Execution status — 2026-10-18 (H410M)

- [x] A/B foundation: resilient `app.json`, default password ON / 15 minutes.
- [x] A: inactivity timer, handled pointer/key input, popup input, audit event,
  revoked session/services, clean unlock page, immediate Settings changes.
- [x] B: Windows Credential Locker, verified opt-out, silent startup/fallback,
  password-change refresh and warning, wipe cleanup, opt-in deletion.
- [x] C/D: ClosedXML 0.105.1, four-sheet data export and annual operator export,
  background generation, native save pickers, progress and completion/error UI.
- [x] H410M build: 0 warnings/errors; 126 smoke assertions pass. Native update,
  search, validation, invoice/receipt suites pass. Production five-minute idle
  setting verified over 330 seconds (19:34:30–19:40:00 Asia/Jakarta).
- [x] Both sample exports opened in installed ONLYOFFICE; visible amounts,
  dates, Unicode and leading-zero phone checked. Workbook read-back assertions
  also verify numeric types, filters, totals, voids and cross-year payments.
- [ ] Excel and LibreOffice-specific manual acceptance: neither is installed
  on H410M. No claims of verification in these applications.

Implementation findings superseding assumptions below:

1. The schema uses `appointments` for visits and stores patient age, not DOB.
   DOB exports blank and Age exports the recorded age; no invented dates or
   schema changes. A later DOB migration is outside this plan.
2. Hiding a page or replacing `Window.Content` does **not** remove ContentDialog
   popups in WinUI. Auto-lock replaces/closes the old native window, preserving
   its bounds/state. This abandons the old popup layer without individually
   dismissing/resuming dialogs. Native tests verify no popup survives unlock.
3. Existing SQLCipher connections were pooled by path without key identity.
   A direct two-key regression proved that a pooled, already-decrypted handle
   accepted a subsequent wrong key. Encrypted connections now disable pooling;
   password proof and stale-vault fallback fail safely even in a live session.
4. Core retains portable `net10.0` and additionally targets Windows for the
   real PasswordVault adapter. The app and Windows smoke runner use that
   target; portable callers get graceful vault-unavailable behavior.
5. Annual gross/counts use this year's visits; collections include receipts
   in the year against older invoices. Carry-in detail rows have zero annual
   gross and explicitly identify carry-in payments. Annual outstanding sums
   per-invoice max(total minus payments **in the selected year**, 0), as
   specified. TOTAL sums each numeric column, including operator averages.
6. Background exports use a consistent read snapshot. Lock revokes further DB
   access; an already-running read/build can finish, but its bytes are discarded
   by the UI if its session ended. Existing Reports/dashboard summaries keep
   their separate paid-invoice/issue-date revenue definition.

See [verification/Updates.md](verification/Updates.md) for reproduction commands
and evidence. The original specification follows.

---

## Part A — Auto-lock on inactivity

### A.1 Behavior specification

1. When the app is unlocked and the user is idle for **N minutes** (default
   **15**, matching the online app), the app locks itself:
   - the session is torn down (see A.3),
   - the unlock screen appears with the message "Locked due to inactivity",
   - an `app_locked` audit event is recorded with `reason = "auto"`.
2. **Activity** = any pointer movement, pointer press, or key press inside the
   app window. Each activity event restarts the idle countdown from zero.
3. The timeout is user-configurable: **5 / 10 / 15 / 30 / 60 minutes**, plus
   **Never** (auto-lock off). See Part B for the separate "no password at all"
   option — "Never" here still requires the password on app start; it only
   disables the *inactivity* lock.
4. Auto-lock is **inactive** while no session exists (Setup page, Unlock page).
   It starts when the shell is entered and stops when the session ends.
5. Minimizing the app does **not** count as activity — the timer keeps running
   and will lock a minimized app. This is intended (clinic PC left unattended).

### A.2 Settings storage

New plaintext JSON file (NOT in the encrypted DB — see why below):

```
%LOCALAPPDATA%\MedRecPlusOffline\app.json
{
  "require_password": true,      // Part B
  "autolock_minutes": 15         // 0 = Never
}
```

**Why plaintext, not `SettingsService`:** `SettingsService` lives inside the
SQLCipher database, which is only readable *after* unlock. `require_password`
must be evaluated *before* unlock to decide whether to show the lock screen at
all. `autolock_minutes` rides along in the same file so both lock-related
settings share one load path and survive an emergency wipe (after a wipe the
file keeps the user's last choice; first-run setup writes defaults).
Neither value is secret, so plaintext is appropriate.

- New class: `MedRecPlus.Core/Services/LocalAppSettings.cs`
  - `Load()` → returns settings with defaults (`require_password: true`,
    `autolock_minutes: 15`) when the file is missing or corrupt (corrupt =
    defaults + rewrite file, never crash).
  - `Save(SecuritySettings)`.
- New `SettingKeys` are **not** used for these two; document that split in a
  code comment.

### A.3 Implementation

**New file: `MedRecPlus.App/Security/AutoLockService.cs`**

```csharp
public sealed class AutoLockService
{
    public AutoLockService(TimeSpan timeout);  // TimeSpan.Zero = disabled
    public event EventHandler? AutoLocked;
    public void Start();
    public void Stop();
    public void NotifyActivity();              // resets the countdown
}
```

- Internals: a `DispatcherQueueTimer` (UI thread; use
  `DispatcherQueue.GetForCurrentThread()` captured at construction on the UI
  thread). `NotifyActivity()` stops and restarts the timer. On tick → raise
  `AutoLocked`.
- Constructor takes `TimeSpan` (not minutes) so the smoke test can inject
  2-second timeouts. Production code maps `autolock_minutes` → `TimeSpan`
  (0 → disabled, service not started).

**Activity hooking — `MainWindow.xaml.cs`:**

- After `EnterShell()`, attach to the window's content root:
  ```csharp
  var root = (FrameworkElement)Content;
  root.PointerMoved += (_, _) => _autoLock?.NotifyActivity();
  root.PointerPressed += (_, _) => _autoLock?.NotifyActivity();
  root.KeyDown += (_, _) => _autoLock?.NotifyActivity();
  ```
  These are bubbling routed events; attaching at the root catches input from
  all pages and dialogs. (ContentDialogs are popups in the same visual tree —
  typing in a dialog counts as activity, which is correct.)
- Subscribe: `_autoLock.AutoLocked += (_, _) => LockDueToInactivity();`
- `LockDueToInactivity()`:
  1. `_autoLock.Stop()`
  2. `AppSession.End()` (new — see below)
  3. `Audit` is gone with the session; record `app_locked` **before**
     teardown (the Database is still open at that point).
  4. Show `AuthFrame`, navigate to `UnlockPage` with a parameter
     `"Locked due to inactivity."`, collapse the shell content.

**New: `AppSession.End()`** (`MedRecPlus.App/AppSession.cs`) — does not exist
today; add:

```csharp
public static void End()
{
    // Close pooled SQLite handles so the DB file is truly released.
    SqliteConnection.ClearAllPools();
    Db = null!;
    // Null out all service singletons...
    IsReady = false;
}
```

Null every service property (Patients, Operators, …, Search) so a stale page
can't touch a disposed session. Pages are rebuilt on next unlock via the
existing `Start()` path.

**`UnlockPage`:** accept an optional navigation parameter (string message);
display it above the password box in accent color when present. Keep the
existing empty-password guard and WIPE-typed emergency wipe untouched.

**Settings UI** (`SettingsPage` → new **Security** section):
- ComboBox "Lock after inactivity": Never / 5 / 10 / 15 / 30 / 60 minutes.
  Changing it applies immediately: if the new value is Never → `_autoLock.Stop()`;
  else → recreate/restart the service with the new timeout (via a
  `MainWindow.ApplyAutoLockSettings()` method).
- This section will also host the Part B toggle (below).

### A.4 Edge cases

- **Unlock restarts the timer.** `UnlockPage` success path calls
  `App.Shell.EnterShell()` → `EnterShell()` (re)starts `AutoLockService`.
  Make `EnterShell()` idempotent: stop any existing service before starting.
- **Dialog open at timeout:** the lock navigates `AuthFrame` over everything;
  any open `ContentDialog` is abandoned. Acceptable v1 behavior — document it.
  (Do NOT try to programmatically dismiss dialogs; abandoned popups are
  cleaned up with the visual tree.)
- **Timer tick while already locking:** guard with an `_isLocking` flag.
- **System sleep:** `DispatcherQueueTimer` doesn't fire during sleep; on
  resume the elapsed time is wall-clock — the app will lock shortly after
  resume if the timeout elapsed. Correct behavior.
- **0-minute / negative values in a hand-edited `app.json`:** clamp to
  `0 = Never`; never crash on corrupt config.

### A.5 Testing

- **SmokeTest:** add `AutoLockService` timing tests — construct with
  `TimeSpan.FromMilliseconds(300)`, assert `AutoLocked` fires; call
  `NotifyActivity()` at 150 ms and assert it does *not* fire before 450 ms;
  `TimeSpan.Zero` never fires.
- **Manual E2E (H410M):** set timeout to 5 min is too slow to watch — for the
  E2E run only, temporarily add "1 minute" to the ComboBox? **No** — do not
  ship test-only UI. Instead: E2E by setting `autolock_minutes: 1` via a
  temporary debug step is also hacky. Decision: verify with 5 minutes using
  WinRemote timestamps (start timer, wait 5:30, screenshot unlock screen).
  Slow but honest. Alternatively add an internal `TestHook` — rejected, keep
  the shipped UI clean.
- **Regression:** existing unlock/setup/smoke flows must stay green.

---

## Part B — Allow disabling the app lock completely

### B.1 Behavior specification & security tradeoff

A new setting, **"Require password to open the app"** (default **ON** =
current behavior).

- **ON:** today's flow — every launch shows the Unlock page.
- **OFF:** the app opens straight into the shell. No password prompt, no
  auto-lock (auto-lock without a password to return to is meaningless — when
  the toggle is OFF, force `autolock_minutes` to 0/Never in the UI and ignore
  the stored value).

**Explicit tradeoff (must appear verbatim-ish in the Settings UI):**

> "Anyone who can log in to this Windows account can open the clinic data.
> Only turn this off if the Windows account itself is secured."

The database file itself remains SQLCipher-encrypted at rest. What changes is
*where the key lives*: with the lock off, the 32-byte DB key is kept in the
**Windows Credential Locker** (DPAPI-encrypted, bound to the Windows user
account), instead of only in the user's memory. An attacker who steals the raw
disk but doesn't know the Windows password still can't read the database.

### B.2 Key storage — Credential Vault

**New: `MedRecPlus.Core/Services/CredentialStore.cs`**

Uses `Windows.Security.Credentials.PasswordVault` (WinRT API — works in
unpackaged WinUI 3 apps):

```csharp
public static class CredentialStore
{
    private const string Resource = "MedRecPlusOffline";
    private const string UserName = "dbkey";
    public static void SaveDbKey(string keyHex);       // 64 hex chars
    public static bool TryGetDbKey(out string keyHex); // false if missing
    public static void DeleteDbKey();                  // idempotent
}
```

- `SaveDbKey` first calls `DeleteDbKey()` (vault throws on duplicate
  resource+username).
- All methods catch `Exception` and return false / no-op on failure — a
  broken vault must **never** crash startup; the fallback is the Unlock page.

### B.3 Startup flow changes (`App.xaml.cs` / `MainWindow`)

Current: `IsFirstRun ? SetupPage : UnlockPage`.

New:

```
if (AppData.IsFirstRun)                          → SetupPage
else if (!LocalAppSettings.Load().require_password
         && CredentialStore.TryGetDbKey(out var key))
                                                  → open DB with key silently,
                                                    AppSession.Start(db, actor),
                                                    EnterShell()
else                                              → UnlockPage
```

- Silent-open failure (vault key stale, DB moved) → fall through to
  `UnlockPage`. Never show a raw exception at startup.
- `actor` for the silent path: first operator's name (same as `UnlockPage`
  does today).

### B.4 Settings UI & flows

In the new **Security** section of `SettingsPage`:

- `ToggleSwitch` "Require password to open the app" + the warning text from
  B.1 (TextBlock, `TextWrapping`, below the switch).
- **Turning OFF:** the user must prove ownership first — show a
  `ContentDialog` asking for the *current* password (single PasswordBox).
  On success: derive the key (`AppData` already exposes the KDF path used by
  unlock — reuse it, do not duplicate Argon2 parameters), `SaveDbKey()`,
  write `require_password: false` to `app.json`, force auto-lock UI to Never.
- **Turning ON:** `DeleteDbKey()`, write `require_password: true`. Next
  launch shows Unlock. No password prompt needed (enabling the lock is always
  safe).
- **Password change** (`ChangePassword` flow): if a vault entry exists,
  re-derive with the new password and overwrite it. If the vault write fails,
  warn but don't fail the password change — worst case the user sees the
  Unlock page next launch.
- **Emergency wipe:** also calls `CredentialStore.DeleteDbKey()` (wipe must
  leave no key material behind). `app.json` itself is kept (it holds the
  user's lock preference for the fresh setup).

### B.5 Edge cases

- Vault entry exists but `require_password` is true (user re-enabled the
  lock): ignore the vault entry; delete it opportunistically on next
  successful unlock to avoid stale keys lying around.
- `app.json` deleted by hand: defaults (`require_password: true`) → safe
  direction (locked).
- Roaming Windows profiles: Credential Locker roams with the user profile by
  default; the DB file does not roam. A roamed credential on a PC without the
  DB is harmless (silent-open fails → Unlock page).
- Backup/restore: untouched — the DB key doesn't change, only where a copy
  is cached.

### B.6 Testing

- **E2E (H410M):** toggle OFF (enter password) → close app → relaunch →
  lands directly in shell/dashboard. Toggle ON → relaunch → Unlock page.
  Wrong-password dialog on toggle-OFF with bad password → stays ON.
- **Negative:** delete the vault entry manually via Credential Manager →
  relaunch with toggle OFF → falls back to Unlock page (no crash).
- **SmokeTest:** `CredentialStore` round-trip is environment-dependent
  (needs a Windows user profile); gate behind `[WindowsOnly]`-style skip when
  no vault is available — assert graceful false, not throw.

---

## Part C — Excel export: data points (patients, visits, invoices)

### C.1 Library choice

**ClosedXML** (MIT license) via NuGet, referenced from `MedRecPlus.Core`
(export logic is data transformation, not UI — Core stays UI-free).

- Verify at implementation time that ClosedXML restores and builds for
  `net10.0` on H410M. If it doesn't, fallback is the raw **OpenXML SDK**
  (more verbose, zero risk).
- Do NOT use COM/Interop (`Microsoft.Office.Interop.Excel`) — requires Excel
  installed; the clinic PC may run LibreOffice or nothing.

### C.2 New service: `MedRecPlus.Core/Services/ExcelExportService.cs`

```csharp
public sealed class ExcelExportService(Database db)
{
    public byte[] ExportDataPoints(DateOnly? from, DateOnly? to);
}
```

One workbook, four worksheets. Money is stored as **cents** in the DB —
convert to whole Rupiah (`/100`) and write as **numbers** (never text) so
Excel can sum them.

**Sheet 1 — `Patients`:**
| Column | Source | Format |
|---|---|---|
| RecordNo | `patients.record_number` | Text |
| FullName | name | Text |
| DateOfBirth | dob | Date `yyyy-mm-dd` (blank if unknown) |
| Age | computed at export | Number |
| Gender | gender | Text |
| Phone | phone | Text (keep leading 0 — set text format) |
| Address | address | Text |
| LawfulBasis | consent basis | Text |
| CreatedAt | created_at | Date |
| VisitCount | count of visits | Number |
| TotalInvoicedRp | sum of invoice totals | Number `#,##0` |
| TotalPaidRp | sum of payments | Number `#,##0` |

**Sheet 2 — `Visits`:**
VisitDate (date) | PatientName | PatientRecordNo | Operator |
Diagnoses (ICD codes joined `"; "`, e.g. `A09; J06.9`) | Treatments (names joined
`"; "`) | SubtotalRp | DiscountRp | TaxRp | TotalRp (numbers `#,##0`) |
InvoiceNo (blank if none) | InvoiceStatus

**Sheet 3 — `Invoices`:**
InvoiceNo | IssueDate (date) | PatientName | PatientRecordNo | Operator |
SubtotalRp | DiscountRp | TaxRp | TotalRp | PaidRp | BalanceRp (all `#,##0`) |
Status (`ISSUED`/`PAID`/`VOID`)

**Sheet 4 — `Payments`:**
PaymentDate (date) | InvoiceNo | PatientName | AmountRp (`#,##0`) |
Method | Operator | Note

**Workbook conventions (all sheets):** row 1 = bold header, freeze panes,
auto-filter on, columns auto-fitted (`AdjustToContents()` with a sane max
width ~40), sheet tab colors (Patients=blue, Visits=green, Invoices=orange,
Payments=purple — cheap visual parsing).

**Date-range filter:** `from`/`to` optional; when null, export everything.
v1 UI exposes no date filter for this export (full export); the parameters
exist so the per-page buttons (C.4) can pass "this month" later without a
service change.

**Soft-deleted rows:** excluded (consistent with the app's list views).

### C.3 UI wiring

- **Reports page** (`ReportsPage`): new "Export" card with a primary button
  **"Export data (Excel)"**. Click → `FileSavePicker` (reuse the
  `Dialogs.cs` pattern with `InitializeWithWindow`) →
  suggested name `MedRecPlus_Data_2026-10-18.xlsx` →
  `File.WriteAllBytes(path, excelBytes)` → success `ContentDialog`
  ("Exported N patients, M visits, K invoices") / failure shows the
  exception message.
- Export runs the query + workbook build on a background thread
  (`Task.Run`) — a full clinic export may take seconds; keep the UI
  responsive. Show a determinate-less `ProgressRing` on the button while
  running.

### C.4 Follow-up hooks (v1 scope decision)

v1 = full-export from Reports only. The service signature already supports
ranges so these can land later without rework:
- Patients page → "Export patients" (filtered to current search)
- Invoices page → "Export invoices" (filtered to current status filter)

Note them as commented-out TODOs in the page code-behinds, not as dead UI.

### C.5 Testing

- **SmokeTest:** build a small in-memory scenario (2 patients, 3 visits,
  2 invoices, 3 payments incl. a partial), run `ExportDataPoints`, then
  re-open the bytes with ClosedXML and assert: 4 sheets with exact names,
  header row values, row counts, one money cell is numeric (not text) with
  the right value, phone kept as text with leading zero.
- **Manual (H410M):** export the E2E sample DB, open in Excel *and*
  LibreOffice Calc — verify numbers sum, dates sort, no `#####` columns
  (auto-fit check), Indonesian characters render (UTF-8).

---

## Part D — Excel export: operator performance (per year)

### D.1 Specification

**One workbook per export.** The user picks a **year** (default: current
year) and an **operator scope**: "All operators" or one operator.

**Sheet 1 — `Summary_<year>`:**
| Operator | Visits | InvoicesIssued | GrossRp | CollectedRp | OutstandingRp | AvgInvoiceRp |
|---|---|---|---|---|---|---|
Numbers `#,##0`. One row per operator active in that year. A final
`TOTAL` row sums the numeric columns.

Definitions (be exact — this is where reports get argued about):
- *Visits*: count of visit records attributed to the operator in the year
  (by visit date, not creation date).
- *InvoicesIssued*: count of non-void invoices for those visits.
- *GrossRp*: sum of invoice totals (after discount, incl. tax).
- *CollectedRp*: sum of payments received against those invoices (payments
  dated in the year; a 2025 invoice paid in 2026 counts in 2026).
- *OutstandingRp*: `GrossRp − CollectedRp` floored at 0 per invoice, then
  summed (never negative — overpayments are rejected at entry).
- *AvgInvoiceRp*: `GrossRp / InvoicesIssued` (blank when 0 invoices).

**One sheet per operator** (`<OperatorName>` truncated to 31 chars —
Excel's sheet-name limit; disambiguate with ` (2)` suffix on collision):
Date | PatientName | VisitRef | InvoiceNo | TotalRp | PaidRp | BalanceRp |
Status. Same number/date formats as Part C.

When a single operator is selected, the workbook contains the Summary sheet
(filtered to that operator) + that operator's detail sheet.

**Filename:** `MedRecPlus_OperatorPerformance_2026.xlsx`, or
`MedRecPlus_OperatorPerformance_2026_<OperatorName>.xlsx` for single-operator.

### D.2 Service

```csharp
public byte[] ExportOperatorPerformance(int year, long? operatorId);
```

- `year` validated: 2000–2100, else `ArgumentOutOfRangeException`.
- Operators with zero activity in the year are omitted from Summary
  (cleaner than zero-rows), but the operator picker still lists them.
- Reuse the workbook-conventions helper from Part C (header style, freeze,
  filter, autofit) — put it in a small internal `ExcelStyle` static class so
  C and D can't drift.

### D.3 UI wiring

**Reports page**, same Export card as Part C:

- `ComboBox` year: current year down to (current − 10), default current.
- `ComboBox` operator: "All operators" + each operator name, default All.
- Button **"Export operator performance"** → FileSavePicker →
  `Task.Run` build → success dialog with the year/operator echoed.

### D.4 Testing

- **SmokeTest:** seed 2 operators × 2 years of data (incl. a void invoice and
  a cross-year payment), export 2026/all → assert Summary rows, TOTAL math,
  void invoice excluded from Gross, cross-year payment attributed to the
  payment's year. Export single operator → 2 sheets.
- **Manual (H410M):** open in Excel/LibreOffice, eyeball the TOTAL row.

---

## Build order (do not parallelize across parts sharing files)

1. **Settings foundation** (A.2 + B.1 storage): `LocalAppSettings.cs`,
   `app.json` load/save with corruption fallback. Smoke-test the fallback.
2. **Part A**: `AutoLockService` + `AppSession.End()` + MainWindow wiring +
   UnlockPage message + Settings Security section (auto-lock ComboBox only).
3. **Part B**: `CredentialStore` + startup flow + Security toggle + wipe
   integration + password-change integration.
4. **Part C**: ClosedXML reference → `ExcelExportService.ExportDataPoints` →
   SmokeTest → Reports UI.
5. **Part D**: `ExportOperatorPerformance` → SmokeTest → Reports UI.
6. **E2E on H410M** (WinRemote, real UI): auto-lock wait-test, lock
   toggle round-trip, both Excel exports opened in LibreOffice.
7. Update `verification/` READMEs if new scripts are added; update this
   plan's status section.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| ClosedXML doesn't restore for net10.0 | Verify on H410M in step 4 before writing service code; fallback OpenXML SDK |
| `PasswordVault` throws in unpackaged context | All vault calls are try/catch → Unlock-page fallback; tested in B.6 negative case |
| Auto-lock fires during a long print | Printing shows a modal dialog; input events during it reset the timer. If a print runs > timeout with zero input, lock-after-print is acceptable v1 |
| Excel sheet-name collisions (operator names) | 31-char truncation + ` (2)` disambiguation (D.2) |
| Phone numbers losing leading zero | Text cell format (C.2) + SmokeTest assertion |
| `app.json` hand-edited to `autolock_minutes: -5` | Clamp ≤ 0 → Never (A.4) |
| Session teardown races a running DB write | `AppSession.End()` runs on the UI thread; all DB writes in the app are synchronous on the UI thread today — no background writers exist. If background writers are added later, this needs a lock |

## Out of scope (explicitly not in this plan)

- PDF export of reports (no PDF library in the project — separate decision)
- Indonesian column headers (English v1; the data is what matters)
- Per-page filtered exports (service supports it; UI deferred per C.4)
- Cloud sync / multi-user (never — offline product)
- Changing the 15-minute default (matches the online app; user-configurable)
