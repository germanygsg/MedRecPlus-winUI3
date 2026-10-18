# MedRecPlus Offline (WinUI 3)

Native Windows 11 clinic-management app — a complete offline port of MedRecPlus.
Patient records, visits, invoicing, receipts, reports, and AI insights run
entirely on the clinic PC: **no internet, no cloud, no accounts, no sync.**

> **Status (2026-10-18):** v1 functional and E2E-verified on H410M. All seven
> main pages work end-to-end (patient → visit → invoice → payment → receipt
> print). See [Verification](#verification) for how it was tested and
> [Known gaps](#known-gaps) for what's still missing.

---

## Table of contents

- [Features](#features)
  - [First-run setup & unlock](#first-run-setup--unlock)
  - [Dashboard](#dashboard)
  - [Patients](#patients)
  - [Visits (appointments)](#visits-appointments)
  - [Invoices & payments](#invoices--payments)
  - [Receipt printing](#receipt-printing)
  - [Reports](#reports)
  - [AI Insights](#ai-insights)
  - [Global search](#global-search)
  - [Settings](#settings)
  - [Backup & restore](#backup--restore)
- [Architecture](#architecture)
- [Security model](#security-model)
- [Data model](#data-model)
- [Build](#build)
- [Run](#run)
- [Verification](#verification)
- [Known gaps](#known-gaps)
- [Repository layout](#repository-layout)

---

## Features

### First-run setup & unlock

- **First-run setup** (`Views/SetupPage`): clinic name, operator name, and a
  password. Creates the Argon2id salt and the SQLCipher-encrypted database,
  seeds the clinic profile and the first operator (Admin). Password policy is
  enforced (min 8 chars, upper + lower + digit + special).
- **Unlock** (`Views/UnlockPage`): password prompt on every launch once a
  database exists. Wrong passwords are rejected; an empty password shows
  "Enter your password." instead of leaking a crypto error.
- **Emergency unlock**: a "Forgot password? Emergency unlock (wipes all data)"
  link on the lock screen. Because the database is encrypted with a
  password-derived key, a forgotten password is cryptographically
  unrecoverable — the only way back in is to wipe. The confirmation dialog
  explains that **everything** (patients, visits, invoices, attachments,
  backups) is permanently deleted, and the "Wipe everything" button stays
  disabled until you type `WIPE`. After wiping, the app returns to first-run
  setup. The wipe drains the SQLite connection pool first, so a leftover
  handle from a failed unlock attempt can't silently block deletion.

### Dashboard

Redesigned responsive dashboard (`Views/DashboardPage`, commit `74b8c87`):
visit/invoice/revenue KPIs, charts (via `DashboardChart.cs`), and a responsive
layout that adapts to window width (`ResponsivePanels.cs`). Uses the Inter
typeface (bundled under `MedRecPlus.App/Assets/Fonts/`).

### Patients

- List with search (name, record number, phone), add/edit via `PatientDialog`.
- Patient detail page: demographics, lawful-basis/consent record, visit
  history, invoice history, attachments.
- **Portable export**: per-patient data bundle export.
- **Erasure** (`ErasePatientDialog`): true hard delete of the patient and
  their visits/invoices (typed-name confirmation), with an IDs-only audit
  event kept as the legal record.
- Attachments stored as local files under `%LOCALAPPDATA%\MedRecPlusOffline\attachments\`
  with metadata (filename, MIME type, size, checksum) in the database.

### Visits (appointments)

- Visit list (`Views/AppointmentsPage`) with same-day visit records.
- **New visit dialog** (`Views/VisitDialog`): patient picker, vital signs
  (BP, heart rate, respiration, temperature, SpO2, Borg scale), ICD-11
  diagnosis picker (search over the bundled `icd11.db` — 37,080 entities,
  relevance-ranked: exact code → code prefix → title prefix → substring),
  treatment lines (inline creation; entered in Rupiah, stored as cents),
  custom examinations, and invoice-level discount.
- Visit detail page (`Views/VisitDetailPage`): vitals, diagnoses, treatments,
  notes; treatment notes are editable.
- **Visit freeze**: once an invoice is generated for a visit, the visit
  becomes read-only — later edits can't rewrite issued invoices.

### Invoices & payments

- Invoice generation from a visit (`InvoiceService`): sequential numbers
  `INV-YYYYMM-NNNN`, allocated inside a transaction against a UNIQUE
  constraint (safe without a server).
- **Frozen snapshot**: the invoice freezes treatments, prices, vitals,
  operator, patient identity, and receipt layout at issue time — reprinting
  later reproduces the original exactly even after settings change.
- **Money math** (`MoneyMath.cs`, `RupiahAmount.cs`): integer-cents
  arithmetic, discount applied **before** tax (PPN 11% default, configurable).
- **Payments** (`Views/PaymentDialog`): partial and full payments, remaining
  balance tracking, overpayment rejection. Payment history per invoice.
- **Guarded transitions**: `DRAFT → ISSUED → PAID`, `VOID` terminal.
  (Note: the Mark-void button was removed from the invoice detail page per
  explicit product decision — voiding is a data-layer capability, not UI.)

### Receipt printing

Native WinUI printing (`InvoicePrinter.cs`, `InvoicePaperLayout.cs`):

- Fixed-layout receipt at exactly **120 mm × 138 mm** (LX-310 dot-matrix
  profile), rendered from the frozen invoice snapshot.
- **Receipt designer** (`ReceiptSetupControl`, commit `6f16110`): visual
  editor for margins (millimetre), six font sizes, line spacing, ten section
  switches, five divider switches, bundled VT323 dot-matrix-look font.
  Invoices keep the layout they were issued with; new invoices use current
  settings.
- The print flow flags a mismatched printer driver media size in the system
  print dialog. Set the printer's custom paper to 120 × 138 mm portrait.
- **No PDF export yet** — printing goes through the Windows print driver;
  there is no PDF library in the project.

### Reports

Basic reports page (`Views/ReportsPage`): revenue/visit summaries. Charts and
Excel (`.xlsx`) export are **not yet implemented**.

### AI Insights

Deterministic, fully local analytics chat (`AiInsightsService`): keyword-based
intent classification (Indonesian + English) over a local data snapshot,
with fixed tools (KPI snapshot, revenue summary, operator performance,
treatment trends, invoice follow-ups, patient timeline, data quality) and
evidence rows. No LLM, no network — the narrative layer is intentionally
absent.

### Global search

`GlobalSearchService` + `SearchResultsPage` (commit `00565d8`): categorized
suggestions and grouped results across patients, visits, and invoices, with
record-detail navigation. Covered by smoke-test regression checks.

### Settings

`Views/SettingsPage` sections:

- Clinic profile (name, address, phone)
- Tax configuration (label, type, value)
- Operators: add/delete (no edit yet)
- Treatments: add/delete (no edit yet)
- Custom examinations: add/delete (no edit yet)
- Receipt settings: the full receipt designer (see above)
- Backups: manual encrypted backup, backup list, restore
- Password change (Argon2id re-hash + SQLCipher rekey + salt rotation)
- Form validation throughout (`FormValidation.cs`)

### Backup & restore

`BackupService` — manual, encrypted, fully offline:

1. WAL checkpoint, then file copy of the live database
2. Reopen-verification of the backup copy with the same key (a backup that
   can't be decrypted is rejected)
3. Backup list UI with restore (restore targets the current database)
4. Retention: latest 30 backups kept

Backups live under `%LOCALAPPDATA%\MedRecPlusOffline\backups\`.
**Scheduled/automatic backups are not implemented** — backups are manual only.

---

## Architecture

Three projects in `MedRecPlus.sln` (flattened layout, no `src/` prefix):

| Project | Target | Purpose |
|---|---|---|
| `MedRecPlus.Core` | `net10.0` | Entities, encrypted-SQLite data layer, domain services. **No UI dependencies.** |
| `MedRecPlus.App` | `net10.0-windows10.0.19041.0` | Unpackaged WinUI 3 app: shell, pages, dialogs, printing. |
| `MedRecPlus.SmokeTest` | `net10.0` | Console test harness (31+ checks). |

### Core services (`MedRecPlus.Core/Services/`)

- `AppData` — well-known paths (`%LOCALAPPDATA%\MedRecPlusOffline\`),
  first-run detection, setup, unlock, password change, emergency wipe
- `PasswordService` — Argon2id hashing/verification (Konscious.Security.Cryptography)
- `Database` / `Schema` / repositories — SQLCipher via
  `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`, Dapper
  for data access; idempotent schema migrations on unlock
- `VisitService`, `InvoiceService` — visit lifecycle, invoice issuance,
  payments, status guards
- `MoneyMath`, `RupiahAmount`, `NumberingService` — cents arithmetic,
  Rupiah formatting, sequential numbering
- `BackupService` — encrypted backup / verify / restore / retention
- `AttachmentService` — local file storage + DB metadata
- `AuditService` — append-only hash-chained audit log (tamper-evident)
- `AiInsightsService` — deterministic analytics engine
- `IcdService` — ICD-11 search over bundled `icd11.db`
- `GlobalSearchService` — cross-entity search
- `SettingsService`, `ReceiptLayout` — settings store, receipt geometry

### App (`MedRecPlus.App/`)

- `App.xaml(.cs)` — single-instance via `AppInstance.FindOrRegisterForKey`,
  unhandled-exception logging
- `MainWindow` — NavigationView shell, 7 pages; window icon set at runtime
  via `AppWindow.SetIcon` (the MedRecPlus purple-cross `Assets/appicon.ico`,
  also embedded as the exe icon via `<ApplicationIcon>`)
- `Views/` — 15 pages + dialogs (see [Features](#features))
- `InvoicePrinter.cs`, `InvoicePaperLayout.cs`, `ReceiptSetupControl` —
  native receipt printing pipeline
- `DashboardChart.cs`, `ResponsivePanels.cs`, `FormValidation.cs`,
  `TableColumnPicker.cs` — UI infrastructure
- `Assets/Fonts/` — bundled Inter + VT323 (offline, no web fonts)

### Dependencies

App: `CommunityToolkit.Mvvm` 8.4.2, `Microsoft.WindowsAppSDK` 2.5.1
(sdk-pinned; do **not** add an explicit duplicate reference).
Core: `Microsoft.Data.Sqlite.Core` 10.0.12,
`SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11, `Dapper` 2.1.89,
`Konscious.Security.Cryptography.Argon2` 1.3.1.

---

## Security model

- **Encryption**: whole-database SQLCipher encryption; the 32-byte key is
  derived from the password via Argon2id with a per-database salt
  (`medrec.salt`). No key is stored anywhere.
- **Wrong password**: decryption fails with "file is not a database" —
  surfaced as "Wrong password."
- **No recovery**: there is no password reset. The emergency-wipe flow on
  the lock screen is the only path when the password is lost.
- **Audit**: security-relevant events append to a hash-chained log;
  tampering is detectable (`AuditService` verifies the chain).
- **Backups are encrypted** (same SQLCipher key) — a stolen backup file is
  useless without the password.
- **Single operator** (v1): no user accounts, no roles, no network auth.
- **Not yet implemented**: inactivity auto-lock, failed-attempt lockout,
  activity-log viewer UI.

---

## Data model

Encrypted SQLite (`medrec.db`). Key tables:

- `patients` (soft-delete), `visits`, `visit_vitals`, `visit_diagnoses`,
  `visit_treatments`, `custom_examinations`
- `invoices` (frozen snapshot columns + `paid_cents`), `invoice_payments`,
  `invoice_lines`
- `operators`, `treatments`, `settings` (key-value: clinic profile, tax,
  receipt layout)
- `attachments` (metadata; files on disk), `audit_events` (hash chain),
  `ai_logs`

Conventions: money in **integer cents**, timestamps in `DbTime` format,
numbering `PT<YYYY><10-digit seq>` for patients and `INV-YYYYMM-NNNN` for
invoices (UNIQUE-constrained, allocated in-transaction).

The ICD-11 reference database (`MedRecPlus.App/icd11.db`, ~9.5 MB, 37,080
entities) ships as a content file, opened read-only.

---

## Build

### Prerequisites (H410M)

- Visual Studio 2026 Community with **.NET desktop development** and
  **WinUI application development** workloads
- .NET 10 SDK, Windows SDK 10.0.26100+, Developer Mode ON
- Internet access for the first NuGet restore only

### Command line

```powershell
# from the repo root (flattened layout)
dotnet build MedRecPlus.sln -c Debug -p:Platform=x64
```

Or open `MedRecPlus.sln` in Visual Studio and press F5.

> WinUI 3 requires an architecture-specific platform (`x64`/`arm64`) —
> `AnyCPU` is not supported. Always pass `-p:Platform=x64`.
>
> Stop the running app before rebuilding — the exe file stays locked
> while it runs.

### Build notes (learned the hard way)

- `UseWinUI=true` alone doesn't resolve `Microsoft.UI.Xaml` under raw
  `dotnet build` — the `Microsoft.WindowsAppSDK` PackageReference is required.
- Don't combine `EnableMsixTooling=true` with unpackaged
  (`WindowsPackageType=None`) — the XAML compiler crashes (WMC9999).
- `<UseXamlCompilerExecutable>false</UseXamlCompilerExecutable>` is required;
  the out-of-proc `XamlCompiler.exe` crashes on this machine.
- `Microsoft.Data.Sqlite` has no "Busy Timeout" connection-string keyword —
  use `PRAGMA busy_timeout` after opening.
- `SQLitePCL.Batteries_V2.Init()` must be called (in `Database`'s static
  constructor) — the SQLCipher bundle doesn't self-initialize.
- Solution configuration mappings: every project must declare `<Platforms>`
  including `x64`, or Visual Studio shows "unknown project configuration
  mappings."

---

## Run

The app is unpackaged: run the exe directly.

```
MedRecPlus.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MedRecPlus.App.exe
```

All state lives under `%LOCALAPPDATA%\MedRecPlusOffline\`:

| Path | Contents |
|---|---|
| `medrec.db` | Encrypted clinic database |
| `medrec.salt` | Argon2id salt |
| `attachments\` | Patient attachment files |
| `backups\` | Encrypted backups (latest 30) |

To start completely fresh, stop the app and delete this folder.

---

## Verification

### Smoke tests

`MedRecPlus.SmokeTest` — console harness, **31/31 passing** on H410M:

Argon2id hash/verify · SQLCipher create/unlock · wrong-password rejection ·
patient numbering (`PT2026000001`) · invoice numbering (`INV-202610-0001`) ·
patient CRUD/search/count/update · soft/hard delete · discount-before-tax
arithmetic · audit hash-chain verification + tamper detection · no patient
plaintext in raw encrypted DB bytes · persistence across reopen · global-search
regression checks (current/saved names, related records, deletion, literal
terms, pagination).

### Native UI verification scripts (`verification/`)

PowerShell scripts that build a temporary app copy with a separate sample
database and drive the real UI (the real clinic database is untouched).
See `verification/README.md` for details:

- `Build-ColumnChecks.ps1` — table column visibility picker, resize handles,
  pagination, saved preferences across restarts
- `Build-NavigationChecks.ps1` — responsive layout at 640/900/1280 px window
  widths, sidebar behavior, light/dark mode
- `Build-ValidationChecks.ps1` — form validation on every native form
  (patient, treatment, payment, setup, unlock, visit, password, operator,
  examination dialogs)
- `Build-SearchChecks.ps1` — global search suggestions, grouped results,
  rapid typing, empty results
- `Build-InvoiceChecks.ps1` — receipt layout at exactly 120 × 138 mm,
  frozen settings, monetary totals, partial payment, pagination, legacy
  receipts, receipt-designer controls

### Manual E2E (WinRemote, 2026-10-14)

Full visible walkthrough on H410M through the real UI: setup → unlock →
patient create → visit create (vitals, ICD, treatment) → invoice generate →
partial + full payment → receipt preview → dashboard updates → AI Insights
query → close/reopen persistence → wrong-password rejection. All 7 nav pages
render without crashing.

---

## Known gaps

Things that are cut, stubbed, or planned but not built:

- **PDF export** — receipts print through the Windows driver; no PDF library
  is integrated yet.
- **Scheduled automatic backups** — backup is manual only ("Create backup now").
- **Reports** — basic summaries only; no charts, no Excel export.
- **Edit flows** — operators, treatments, and custom examinations are
  add/delete only; full Edit Visit is still a placeholder.
- **Security hardening** — no inactivity auto-lock, no failed-attempt
  lockout, no activity-log viewer UI (audit events are recorded, just not
  browsable).
- **Attachments** — no appointment attachments; patient attachment
  preview/thumbnail generation is incomplete.
- **Multi-user** — single operator by design (v1); operator profiles are
  attribution only.
- **Receipt settings history** — invoices freeze their issue-time layout
  (good); there is no per-invoice layout override UI beyond the designer.
- **Updater** — no update mechanism (no MSIX packaging in v1; runs as a
  plain exe).

Deliberate product decisions (not gaps): completely offline (no sync,
no accounts), Indonesian + English keyword support in AI Insights,
discount-before-tax, visit-freeze-after-invoicing, no Mark-void button in
invoice UI (per explicit decision), LX-310 120 × 138 mm receipt profile.

---

## Repository layout

```
MedRecPlus-winUI3/              # https://github.com/germanygsg/MedRecPlus-winUI3
├── README.md                   # this file
├── PRD.md                      # v1.0 product requirements
├── MedRecPlus.sln
├── MedRecPlus.App/             # WinUI 3 unpackaged app
│   ├── Views/                  # 15 pages + dialogs
│   ├── Assets/                 # appicon.ico, tile art, bundled fonts
│   ├── icd11.db                # ICD-11 reference DB (read-only asset)
│   ├── InvoicePrinter.cs       # native receipt printing
│   ├── ReceiptSetupControl*    # receipt designer
│   └── ...
├── MedRecPlus.Core/            # data layer + domain services (no UI)
├── MedRecPlus.SmokeTest/       # 31-check console test harness
├── verification/               # native UI verification scripts (H410M)
├── research/                   # UI workflows, data model, offline-fit analysis
├── audit_h410m.py              # H410M readiness audit script
└── sync_to_h410m.py            # file sync helper (dev machine → H410M)
```
