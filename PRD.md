# PRD — MedRecPlus WinUI3 Offline Clinic Edition

**Status:** Draft v1.0 for review
**Date:** 2026-10-13
**Author:** Muse (analysis of the MedRecPlus codebase)
**Audience:** boss (product owner / implementer)
**Target:** A native Windows 11 desktop app (WinUI 3), single clinic PC, **fully offline**, internal use only.

---

## 1. Background

MedRecPlus today is an offline-first, cloud-syncing clinic management system (React 19 + Vite + Joy UI,
shipped as Tauri desktop/Android and a Vercel web app, backed by Supabase Postgres + Auth + Storage and
an Express API). It manages patients, visit records ("appointments"), invoices/receipts, treatments catalog,
operators, custom examinations, reports, and a deterministic AI analytics assistant, with GDPR-oriented
features (consent basis, erasure, portable export, audit trail, retention).

For the new clinic computer, the requirement is different: **a native Windows 11 app that runs completely
offline** — no Supabase, no Google login, no cloud sync, no multi-device anything. This PRD specifies a
WinUI 3 rewrite ("MedRecPlus Offline") that ports the proven clinical workflows and domain rules 1:1 where
they are already offline-capable, strips every cloud half, and hardens the places where the cloud used to
be the safety net (backups, audit export, attachment storage).

### 1.1 What was analyzed

Three read-only deep dives over `~/workspace/medrecplus` (branch `online`, Oct 2026). Source research:

- `research/ui-workflows.md` — screen-by-screen UI, workflows, dead ends (985 lines)
- `research/data-model.md` — full entity model, numbering, money math, sync/auth semantics (346 lines)
- `research/features-offline-fit.md` — per-feature KEEP / ADAPT / CUT verdicts with file paths (176 lines)

All section references below (`§`) point at those research files for traceability.

### 1.2 Goals

1. **Parity of the clinic workflow:** register patient → record visit (vitals, exams, treatments, discount)
   → generate invoice → mark paid/void → print/PDF receipt → dashboard/reports review.
2. **Zero network dependency:** every feature must work with the network cable unplugged, forever.
3. **Data safety without a cloud:** encrypted local DB, encrypted scheduled backups, exportable audit trail.
4. **Native Windows feel:** WinUI 3 NavigationView, system theme, standard title bar, MSIX install, single instance.

### 1.3 Non-goals

- No cloud sync, no multi-PC replication, no web/Android ports of this edition.
- No multi-tenant workspaces, no SaaS billing, no trial limits, no PayPal.
- No online AI/LLM features (the deterministic analytics engine ports as-is; optional local LLM is a later phase).
- Not a legal-compliance certification project: privacy-notice text and retention minima are still placeholders
  pending counsel (see §14) — same as the source app.

---

## 2. Tech stack (recommended)

| Concern | Choice | Why |
|---|---|---|
| UI framework | **WinUI 3** (Windows App SDK 1.7+), C# / .NET 9+ | Native Windows 11, NavigationView, TeachingTip, InfoBar, long-term support |
| MVVM | CommunityToolkit.Mvvm + CommunityToolkit.WinUI | Source-generated INotifyPropertyChanged, dialogs, settings helpers |
| Local database | **SQLite + SQLCipher** via `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlcipher` | Encrypted-at-rest whole DB; mirrors the Tauri app's SQLCipher 4.x design |
| Key derivation | **Argon2id** via `Konscious.Security.Cryptography` (pure managed) | Same KDF as the source app; never a system dependency |
| PDF generation | **QuestPDF** (check current license) or MigraDoc | Re-implements the jsPDF layout; preview/print/PDF from one layout spec |
| Charts | LiveChartsCore.SkiaSharpView.WinUI | Only line/area charts are used today — shallow port |
| Excel export | ClosedXML | Same `.xlsx` conventions as the ExcelJS exports |
| ICD-11 search | Ship `icd11.db` (9.5 MB, already in repo `src-tauri/resources/`) as MSIX asset, query via `Microsoft.Data.Sqlite` | Bundled read-only SQLite, same ranking SQL |
| Backup encryption | AES-GCM (`System.Security.Cryptography`) with Argon2id-derived key | Backups are the *only* copy — must not be plain JSON |
| Installer | MSIX packaging project, **self-signed cert + manual trust** | No Store; plan publisher identity = cert subject now |
| Single instance | `Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey` | Same behavior as the Tauri single-instance plugin |
| Serialization | System.Text.Json | Backup/export bundles, settings |
| Settings storage | Encrypted SQLite `kv_settings` table (same pattern as Tauri) | Proven reliable; IndexedDB/localStorage unreliability was why Tauri moved to this |

### 2.1 Architecture

```
┌─────────────────────────────────────────────────────────┐
│ WinUI 3 Views (XAML) + ViewModels (CommunityToolkit)    │
│  Dashboard · Patients · Appointments · Invoices ·        │
│  Reports · AI Insights · Settings (+ sub-pages)          │
└──────────────────────────┬──────────────────────────────┘
                           │  (no network boundary)
┌──────────────────────────┴──────────────────────────────┐
│ Domain services (C#)                                     │
│  PatientService · AppointmentService · InvoiceService    │
│  (numbering, totals math, freeze rules)                   │
│  ReportService · AiInsightsService (deterministic)       │
│  PdfService (QuestPDF) · PrintService · BackupService    │
│  AuditService · RetentionService · ErasureService        │
│  SecurityService (lock, Argon2, SQLCipher key mgmt)      │
└──────────────────────────┬──────────────────────────────┘
┌──────────────────────────┴──────────────────────────────┐
│ Data layer                                               │
│  SQLCipher-encrypted SQLite (single file)                │
│  Attachments folder: %LOCALAPPDATA%\MedRecPlus\           │
│    attachments\<patientId>\… (original files on disk)     │
└─────────────────────────────────────────────────────────┘
```

No repository-per-entity over-engineering needed; the domain is small. One `AppDb` context
(plain `Microsoft.Data.Sqlite`, Dapper optional), services own the business rules.

---

## 3. Data model (SQLite)

Single-clinic schema. **Dropped from the source model:** `workspaces`, `workspace_members`,
`sync_id`, `rev`, `workspace_id` on every row, `subscription_payments`, `api_tokens`, MCP tables,
`invoice_settings_versions` (server side — keep the *concept* as a settings hash, see §4.5).
**Kept:** everything else, simplified.

### 3.1 Tables

**patients**
| Column | Type | Notes |
|---|---|---|
| id | INTEGER PK AUTOINCREMENT | |
| record_number | TEXT NOT NULL UNIQUE | `PT{YYYY}{6-digit}` — never reused; soft-deleted rows still occupy numbers |
| name | TEXT NOT NULL | |
| age | INTEGER NOT NULL | 1–150 (source UI rejects 0; keep, and make it consistent) |
| gender | TEXT | `Mr.` / `Ms.` / `Mx.` (titles — kept as-is, see §12.8) |
| phone_number | TEXT | optional; validate `^[+]?[\d\s-()]{7,25}$` |
| address | TEXT | |
| initial_diagnosis | TEXT | |
| icd_codes | TEXT (JSON) | `[{code,title,type:'icd11'|'icf',linearizationUri?,foundationUri?}]` |
| consent_basis | TEXT | `explicit_consent\|contract\|legal_obligation\|vital_interests\|public_health` — required at creation, never defaulted |
| consent_date | TEXT (ISO) | |
| consent_recorded_by | TEXT | operator name/id |
| created_at / updated_at / deleted_at | TEXT (ISO) | soft delete via `deleted_at` |

**operators** — `id, name NOT NULL, role TEXT (free text), created_at/updated_at/deleted_at`.
Attribution only (who performed the visit), **not** login accounts.

**treatments** (price catalog) — `id, name NOT NULL, description, price_cents INTEGER NOT NULL, notes, created_at/updated_at/deleted_at`.
**Money is stored as integer cents everywhere.** The source normalizes to numbers on write; the port
uses cents end-to-end to avoid float errors.

**custom_examinations** — `id, name NOT NULL, unit NOT NULL`.

**appointments** (visit records)
| Column | Type | Notes |
|---|---|---|
| id | INTEGER PK | |
| patient_id | INTEGER NOT NULL FK → patients | |
| patient_name | TEXT NOT NULL | denormalized snapshot |
| operator_id / operator_name | INTEGER / TEXT | denormalized snapshot |
| date | TEXT (ISO) NOT NULL | visit datetime (stamped at creation) |
| vital_signs | TEXT (JSON) | `{bloodPressure, respirationRate, heartRate, borgScale, custom_<id>:{name,unit,value}}` |
| treatments | TEXT (JSON) | snapshot `[{id,name,description,price_cents,notes}]` |
| total_price_cents | INTEGER NOT NULL | Σ treatment prices (pre-discount) |
| discount_type / discount_value_cents | TEXT / INTEGER | `none\|percent\|fixed`; percent 0–100 |
| diagnosis_codes | TEXT (JSON) | ICD chips |
| notes / description | TEXT | |
| created_at / updated_at / deleted_at | TEXT | |

**invoices** (immutable once issued)
| Column | Type | Notes |
|---|---|---|
| id | INTEGER PK | |
| invoice_number | TEXT NOT NULL UNIQUE | `INV-{YYYYMM}-{4-digit}`, sequential within month |
| appointment_id | INTEGER NOT NULL FK → appointments UNIQUE | **one invoice per appointment** |
| patient_id / patient_name / operator_id / operator_name | | denormalized snapshots |
| date / appointment_date | TEXT (ISO) | issue date; snapshot of visit date |
| vital_signs / treatments | TEXT (JSON) | snapshots carried from the appointment |
| subtotal_cents / discount_type / discount_value_cents / discount_cents | | frozen breakdown |
| tax_label / tax_type / tax_value_cents / tax_cents | TEXT / TEXT / INTEGER / INTEGER | frozen tax snapshot |
| total_cents | INTEGER NOT NULL | final charged amount |
| status | TEXT NOT NULL DEFAULT 'unpaid' | `paid\|unpaid\|void` (`pending` is dead in source — **do not port it**) |
| payment_method / payment_note | TEXT | **NEW** — cash / transfer / QRIS / other (see §12.3) |
| document_profile | TEXT (JSON) | `{version:1, settings_version, settings_hash, patient:{…}}` — pins receipt settings at issue |
| created_at / updated_at / deleted_at | TEXT | |

**attachments** (metadata; bytes on disk) — `id, owner_type ('patient'|'appointment'), owner_id,
file_name, mime_type, size_bytes, sha256, thumbnail (BLOB, ≤20 KB), description, created_at`.
Files live in `%LOCALAPPDATA%\MedRecPlus\attachments\<owner_type>\<owner_id>\<id>_<file_name>`.
Erasure deletes the disk files.

**audit_events** — `id TEXT PK (guid), at TEXT (ISO), event TEXT, entity_type TEXT,
entity_id TEXT (IDs only — never names/PHI), actor TEXT (operator), meta TEXT (JSON scalars only),
chain_hash TEXT` (SHA-256 chained per insert, tamper-evident).
Event taxonomy (port from `SecurityService.ts`): `patient_viewed/created/updated/deleted/erased/exported/printed`,
`consent_updated`, `record_created/updated/deleted/searched`, `attachment_added/deleted`,
`invoice_issued/status_changed/printed/exported`, `security_incident`, `app_locked/unlock_failed`,
`backup_created/restored`, `retention_purged`. **PHI rule is absolute:** no names, contact data, or
clinical content in the log — IDs only.

**settings** (kv) — `key TEXT PK, value TEXT (JSON), updated_at`. Holds: currency config
`{code,symbol,decimalPlaces,thousandSeparator,decimalSeparator,taxLabel,taxType,taxValue}`,
receipt/invoice document settings (paper, margins, fonts, 11 section toggles — schema ported from
`InvoiceSettingsService.ts`), retention schedule, backup schedule, AI settings, onboarding completion,
theme/language/view preferences.

**sequences** — `name TEXT PK, year_or_month TEXT, next_value INTEGER`. Local counters for
`PT{YYYY}` and `INV-{YYYYMM}` numbering (replaces the Postgres sequence + client MAX+1 scan).

**ai_logs** (optional) — `id, at, question, intent, tools_used (JSON), row_counts (JSON),
duration_ms`. Local-only query history, 30-day retention.

### 3.2 What was deliberately dropped

`workspaces`, `workspace_members`, `sync_id`, `rev`, SaaS billing, MCP/API tokens, the entire
sync pipeline (pull/merge/push, changelog, conflict log), Supabase Storage references
(`storagePath`, `uploadStatus`), the `synced` audit flag, `ErasureSync` markers, demo/trial limits.
If multi-PC sync is ever wanted, it is a new project — the schema keeps no sync scaffolding.

---

## 4. Domain rules (port verbatim)

These are the rules the source app got right; the port must not "improve" them silently.

### 4.1 Numbering
- Patient: `PT{YYYY}{6-digit}`, from `sequences` (`patient_record_number`, year). Gaps from
  generated-but-unused numbers are acceptable. UNIQUE enforced; soft-deleted rows keep their numbers.
- Invoice: `INV-{YYYYMM}-{4-digit}`, from `sequences` (`invoice_number`, YYYYMM) **inside the same
  transaction that inserts the invoice** — this is strictly safer than the source's client-side MAX+1.
- One invoice per appointment: generating when one exists navigates to the existing invoice (no duplicates).

### 4.2 Money math (port `computeInvoiceTotals` exactly)
- All arithmetic in **integer cents**.
- `subtotal = Σ treatment price_cents`
- Discount applied **first**: percent clamped 0–100; fixed capped at `min(value, subtotal)` (never exceeds subtotal).
- Tax computed **on the discounted subtotal**. Percent tax 0–100; fixed tax as cents.
- `total = subtotal − discount + tax`. The full breakdown is **frozen onto the invoice at issuance**;
  later settings changes never rewrite it.

### 4.3 Invoice immutability
Once issued, only `status`, `payment_method`/`payment_note`, `invoice_number`, `deleted_at` may change.
Any other edit is rejected with the source's message: *"Issued invoice details are frozen. Create a new
invoice for a correction."* (Correction flow: void the wrong invoice, generate a new one from the
appointment — the appointment keeps its single-invoice invariant by allowing re-issue only after void.)

### 4.4 Denormalization
`patient_name`/`operator_name`, treatment snapshots, vital signs, ICD codes are copied onto appointments
and invoices at creation. Renaming an operator/treatment/patient later never rewrites history.

### 4.5 Document profile pinning
Each invoice pins `{settings_version, settings_hash, patient snapshot}`. Reprinting an old invoice after
receipt-settings changes reproduces the **issue-time** layout, not the current one. (The source's
server-side hash *verification* is cut — meaningless on one PC — but the pinning concept stays.)

### 4.6 Soft delete everywhere
`deleted_at` on all clinical tables; reads filter it. **Erasure (GDPR Art. 17) is the separate,
explicit hard-delete path** (§10.3).

### 4.7 Appointment = visit record (not scheduling)
The source has **no calendar, no date/time picker, no check-in, no appointment status** — an
"appointment" is a same-day visit record stamped at creation. The port keeps this model for v1
(see §12.1 for the scheduling decision).

---

## 5. Feature scope: keep / adapt / cut

### 5.1 KEEP (port as-is, library swaps only)
- Patient CRUD + record numbers + ICD-11/ICF chips (bundled `icd11.db`, same ranking SQL).
- Visit record creation: vitals (BP/HR/RR/SpO₂-temp/Borg), custom examinations, treatments picker,
  per-visit discount, ICD codes, attachments (drag/drop/paste, WebP ≤1600px, thumbnails).
- Invoice generation from appointment, frozen snapshots, status lifecycle, receipt document
  (the "TREATMENT RECEIPT" layout, 11 section toggles, paper presets incl. dot-matrix 120×138).
- Dashboard: 5 cards (new patients, appointments, revenue from **paid** invoices only, patient quick
  access, recent invoices), per-card 7d/30d/3m ranges, area charts.
- Reports: operator performance (month/year/operator filters, invoice drill-down modal, XLSX export),
  patient vital-signs trend charts (BP sys/dia, HR, RR, Borg).
- **AI Insights deterministic engine**: keyword intent classification (8 intents), 7 fixed tools, template
  answers, KPI cards, "Data Sources & Evidence" table, query logging. (LLM modes were never implemented —
  not ported. Optional local LLM via Windows AI Foundry/ONNX is a later phase, explicitly out of v1.)
- Consent/lawful-basis: required picker on patient create (never defaulted), consent card on details,
  `consent_updated` audit events.
- Audit trail: same taxonomy, ID-only/PHI-free rule, hash-chained, owner-visible viewer.
- Lock screen + security: password setup/unlock, 15-min default auto-lock (5/15/30/60/120/180/Never),
  5 failed attempts → 30-min lockout, password policy (≥8, upper+lower+digit+special, denylist,
  history, 90-day expiry), emergency wipe (`DELETE ALL DATA` typed confirmation), SQLCipher+Argon2id.
- Onboarding tours: port the step content; reduce 12 tours to a single first-run walkthrough + per-page
  hints if desired (decision §12.10).
- Theming: light/dark + blue/yellow/green accents; honor Windows system theme by default; UI scale 75–175%.
- i18n resource design from day one; ship **Indonesian + English** minimum (source has 7 locales; port the
  `id`/`en` dictionaries first, others as resources allow).

### 5.2 ADAPT (works offline, needs rework)
| Feature | Adaptation |
|---|---|
| Invoice numbering | `sequences` table + UNIQUE constraint in a transaction (safer than source) |
| PDF generation | Re-implement in QuestPDF/MigraDoc using `server/src/mcp/receiptLayout.ts` + `InvoiceDocument.tsx` as the layout spec. Print == PDF == preview from frozen bytes. |
| Print path | PrintDocument/WebView2 print through the OS driver to the LX-310 Windows driver (raster path works unchanged). **NEW:** optional text-mode ESC/POS path for the LX-310 — fast, ribbon-cheap (new code, §8.3). |
| Invoice settings | Keep the schema (currency, tax, receipt layout, document language, date format, timezone, decimals); **cut** owner-publish/version/conflict machinery — one local settings row. |
| Reports charts | recharts → LiveChartsCore (line/area only). ExcelJS → ClosedXML, same `.xlsx` column conventions. |
| Audit store | IndexedDB → local encrypted SQLite; remove 1000-entry cap; **ADD CSV/PDF export** (gap in source). |
| Erasure | Keep local purge cascade (patient + appointments + invoices + attachments + AI logs); cut server leg + cross-device markers. |
| Portable export (Art. 20) | Keep JSON bundle + human-readable PDF; **bundle actual attachment bytes** (source ships a manifest that assumes cloud). |
| Retention | Keep per-category schedule + purge-on-startup/24h timer; minima still **DRAFT placeholders** — counsel sign-off required (§14). |
| Breach detection | Keep lockout-escalation + bulk-export-volume signals → `security_incident` events; cut the 403-denial signal (cloud-only). No notification workflow exists — unchanged. |
| Backup/restore | Keep JSON envelope + validate-before-restore; **ADD encryption (AES-GCM, must-have)** + **scheduled automatic backups** to a second disk/USB (source is manual-only). |
| Attachments | Cut Supabase tier; local folder on disk + DB metadata + thumbnails (see §3.1). |
| Auth | Cut Google OAuth + cached sessions; first-run = local clinic setup (name, currency, operator, password). |
| MFA | TOTP was Supabase-only — cut; **Windows Hello** (`UserConsentVerifier`) as optional unlock second factor (phase 2). |
| Updater | Keep the state machine + banner UX; transport → **signed MSIX from USB or clinic network share** scanned on launch + manual "Check for updates" (see §11). |
| Diagnostics | Cut `client-errors` POST; **ADD "Export diagnostics" bundle** (logs + DB integrity check + version) for USB handoff to support. |
| Onboarding persistence | Local settings only; cut server merge. |

### 5.3 CUT (cloud-only, no offline equivalent)
Google OAuth · workspaces/invite codes/roles · trial/paid plans + PayPal · MCP server + tokens + clinical
draft approvals · audit-event server sync · `ErasureSync` · Supabase Storage + signed URLs ·
invoice-settings publish/conflict · `client-errors` telemetry · GitHub Releases feed · WHO ICD API web path ·
`PaymentService` (SaaS billing — unrelated to clinic tender) · demo record limits · onboarding server merge ·
the dead `pending` invoice status · orphaned AI provider modes UI.

---

## 6. Navigation & app shell

- **WinUI `NavigationView`** (left, collapsible): Dashboard · Patients · Appointments · Invoices ·
  Reports · AI Insights · Settings (footer). The source hides Reports/AI Insights from the sidebar —
  the port promotes them to first-class nav items (they are core clinic workflows).
- Standard Windows title bar (`ExtendsContentIntoTitleBar` **not** required — the source's custom
  traffic-light chrome is dropped; use the OS chrome). Keep in the top area: theme toggle, activity/audit
  entry point, and (replacing the offline badge, which is meaningless now) a **backup-health indicator**
  (last successful backup age).
- Global patterns ported: **InfoBar** toasts (success/warning/error/info, auto-dismiss) replace the web
  toast stack; `ContentDialog` for confirm/prompt/alert (destructive = danger styling, always confirmed);
- shimmer/skeleton loading on first load, small ProgressRing for section refreshes;
- table ⇄ card view toggle per list (persisted), column-visibility pickers (persisted), 10/25/50
  page sizes (persisted), FAB "Add" per list screen, dashed empty states with create CTA.
- **Fix in port:** PatientList sort bug (descending broken), client-side sort of loaded page only
  (sort in SQL over the full dataset instead).

---

## 7. Screen specifications

### 7.1 Dashboard (`/`)
Same 5 cards as source, 2-column grid (1 on narrow): New Patients, Appointments, Revenue
(**paid invoices only**), Patient Quick Access (top-10 by visits in range → click starts a new visit),
Recent Invoices (10 latest, any status → click opens details). Per-card range 7d/30d/3m persisted.
CountUp animations, area charts, K/M/B shortening. First-run: if no operators exist, route to
operator setup before the dashboard (mirrors the source's forced `/settings/operators` redirect).
Add: "View all" link on Recent Invoices (gap in source). Cards are not drill-down clickable (keep).

### 7.2 Patients
- **List:** debounced search (name/record/phone), table/card toggle, column visibility, sortable headers
  (fixed), 10/25/50 pagination, row actions View/Edit/Delete, bulk delete with confirm + result dialog.
- **Add:** record number auto (disabled), name (required), gender Mr./Ms./Mx. (required, default Mr.),
  age 1–150 (required), phone (optional, regex-validated), address, ICD chips, initial diagnosis,
  **lawful-basis select (required, no default — blocks save)**. Save → details page.
- **Edit:** same; record number immutable ("Cannot modify"); gender immutable (surface the reason in UI —
  source gives none); basis pre-filled, empty leaves existing basis untouched.
- **Details:** stacked cards —
  1. General info (consent warning banner + "Record basis" when missing; ICD chips → **local** code detail
     dialog, NOT the WHO website — offline, §12.9);
  2. Attachments (click/drag-drop/**paste** from clipboard, WebP ≤1600px, thumbnail grid, zoom 50–300%
     preview, per-file notes + delete);
  3. Appointment history (collapsible; summary chips: last visit, total billed; month-grouped timeline
     with search/month/operator/invoice-status filters, 12-per-page "show more", per-visit vitals with
     **high-BP alert ≥140/90**, invoice status chips);
  4. Consent card (basis + recorded-on/by, or warning; "Change basis" dialog → audit event).
  Header: back, **Export** (Art. 20 → save dialog, JSON + PDF, audit `patient_exported`), **Erase**
  (owner-equivalent confirm; typed full name; scope summary with counts; backup-file caveat; audit
  `patient_erased`), Edit. FAB: "Create Appointment" (pre-selects patient).

### 7.3 Appointments (visit records)
- **List:** search + date-range filter (**default = today**, as source), column visibility, sortable,
  paginated, View/Delete row actions, bulk delete. No status column exists — show linked **invoice status**
  chip (Paid/Unpaid/Void/No invoice) as the source's timeline does.
- **New visit:** patient picker (search incl. record-number deep search, inline "Add patient"),
  ICD chips, **operator picker (required**, inline "Create operator" dialog), vitals (BP free text
  `120/80`, RR, HR, Borg 0–10 with helper; **add range validation — source has none**),
  custom-examination inputs (name + unit templates), treatments picker modal (search, price, live total;
  inline "Create treatment"), discount (none/percent/fixed, behind "+ Add discount"). **Date stamped at
  creation** (no picker in v1). Validation: patient + operator required. Create → details.
- **Details:** sticky header (back, title, **Generate invoice → View invoice** once issued); info card;
  vitals card (Borg color chips); additional examinations; diagnosis chips; treatments (per-treatment
  **Notes** editable post-creation); attachments (same dropzone as patients); totals card
  (subtotal/discount/payable); created-on meta; Delete (soft-delete; explicit warning that the linked
  invoice is also removed).
  - **Edit policy (decision §12.7):** vitals/treatments/discount editable until invoice generation;
    after invoicing the visit is frozen (matches the invoice-freeze rule; source silently forbids edits —
    the port states it explicitly).
  - **Generate invoice:** dedup check → invoice-settings precondition (block with clear error if clinic
    settings unsaved) → number from `sequences` → snapshot everything → tax/discount math → status
    `unpaid` → navigate to invoice with success InfoBar.

### 7.4 Invoices
- **List:** search, date-range filter (**default should be "This month", not today** — source defaults to
  today and shows an empty list; fix in port), **ADD status filter** (paid/unpaid/void — missing in source),
  columns (number, patient, operator, appointment date, total, status; issue date optional), sortable,
  paginated, table/card views, View/Delete, bulk delete. Empty state links to new visit (invoices only
  come from visits — there is **no standalone invoice creation**, keep it that way).
- **Details:** back/title; actions Delete, **Download PDF**, **Print**; **status card** — replace the
  source's silent free-form `<Select>` with a **"Record payment" dialog** (see §12.3): choosing Paid
  requires a payment method (Cash/Transfer/QRIS/Other + note); Void requires a typed/short reason and
  confirmation; transitions audited. Receipt document preview = **the actual PDF bytes** rendered
  (WYSIWYG: print == PDF == preview, from the frozen `document_profile`). Footer: created/updated +
  "View appointment". Legacy invoices without a pinned profile show the source's warning and render
  with current settings.

### 7.5 Reports
- **Operator performance:** Month / Year (required) / multi-operator filters; summary cards (period,
  appointments, revenue); per-operator rows sorted by revenue (appointments, paid invoices, revenue);
  **View** → invoice modal (rows now link to invoice details — gap fixed); **Export XLS** per-operator
  and whole-period (`Invoices_<period>_<operators>.xlsx`, columns No/Invoice Number/Date/Patient/
  Operator/Amount/Status).
- **Patient analysis:** patient search → vital-sign trend line charts (BP sys/dia, HR, RR, Borg vs visit date).
- ADD (small, high value): clinic visit-volume summary (visits/day in range) — the source has no
  clinic-level volume view.

### 7.6 AI Insights
Chat-first page: header + settings; empty state ("Ask Anything About Your Data", capability chips);
suggested-prompt chips; message list (user right, `AIAnswerCard` full-width with KPI cards +
collapsible **"Data Sources & Evidence"** table); sticky input (Enter send, Shift+Enter newline).
Port the deterministic engine: 8 intents, 7 tools, template builders, snapshot provider. Settings:
enabled, PHI redaction (default ON), max evidence rows (10), max response length (2000), log retention
(30d). **Must add:** the **non-clinical / analytics-only disclaimer** (missing in source — port it in the
empty state, answer cards, and settings) and wire the privacy notice. Log queries locally with
retention purge.

### 7.7 Settings hub
NavigationView-embedded or hub page with grouped sections (mirroring the source's 8 groups):

| Group | Section | Contents (offline port) |
|---|---|---|
| Clinic | Appearance | Theme (Light/Dark/Blue/Yellow/Green; default = Windows system), UI scale 75–175%, language (id/en), table/card default |
| Clinic | Operators | Roster CRUD (name + role label), search, 10/page |
| Clinic | Treatments | Catalog CRUD (name/description/price_cents), search, soft-delete |
| Clinic | Custom examinations | Templates (name + unit), duplicate-name guard |
| Clinic | Currency & tax | ~180-currency picker w/ live sample, tax label/type/value + preview on 100,000 sample; decimal places, date format, timezone |
| Billing | Receipt designer | Full document designer: language, paper presets (A4/Letter/Thermal 80/58mm/**Dot-matrix 120×138**/Custom), margins, fonts (embedded Inter/VT323/Courier), per-section font sizes, line height, **11 section toggles**, live WYSIWYG preview, Save / Reset to defaults. Header+footer required. |
| Data | Backup & restore | Manual encrypted backup now; **schedule** (daily/weekly, destination folder — second disk/USB); restore with validate-first dialog; Excel export of patients/invoices; **Erase all data** (local 3-stage gauntlet, no cloud leg) |
| Data | Storage | Local inventory (per-entity counts, DB file size, attachments folder size); DB integrity check; "Export diagnostics" bundle |
| Updates | App updates | Update state (idle/checking/available/installing/up-to-date/error) + "Check USB / network share" button; manifest + signature verification |
| Security | Password & lock | Setup/change/re-encrypt/reset password (strength checklist), auto-lock toggle + timeout, failed-attempt lockout toggle, "Lock now", Windows Hello second factor (phase 2) |
| Security | Security audit | Owner-visible, IDs-only trail, filters (action/actor/date), incident banner (lockouts, export-volume spikes) + "Mark reviewed", **CSV/PDF export** |
| Privacy | Consent & retention | Retention per category (days; minima DRAFT — legal review), "Run purge now", privacy notice (still draft text) |
| Audit | Activity log | Local CRUD feed w/ deep links, 7-day auto-purge (configurable), mark-read, clear-all |
| AI | AI Insights settings | Folded-in working toggles (no orphaned page, no dead provider modes) |
| Help | About / onboarding | Version, "Restart guided tour", diagnostics export |

**Cut from settings:** Accounts & workspaces, cloud sync/offline-mode page, MCP/AI connections, TOTP MFA,
PayPal upgrade, attachment migration, sync-conflict UI.

---

## 8. Printing

### 8.1 Raster path (default, works today)
PDF bytes → render pages → print via `PrintDocument`/WebView2 through the OS driver. Unchanged semantics
from the source: **print == PDF == preview**. Paper sizes honored via `@page`-equivalent print ticket.
Target printer: the clinic's **Epson LX-310** via its Windows driver.

### 8.2 Receipt profiles
Keep the paper presets: A4, Letter, Thermal 80mm, Thermal 58mm, **Dot-matrix 120×138mm** (tuned from
real LX-310 print photos: Courier, tuned line spacing, multiline blocks render line-by-line), Custom.
Default/fallback: dot-matrix 120×138 (matches the clinic's existing setup).

### 8.3 Text-mode ESC/POS path (NEW, recommended phase 2)
The source's LX-310 support is **layout-only** — zero raw-print code exists. A real text-mode path
(ESC/POS over the driver bypass / raw spool) would print receipts in seconds instead of minutes and
save ribbon. This is new development, not a port: evaluate after v1 with the physical printer on hand.

---

## 9. Backup, restore, disaster recovery

The cloud used to be the second copy. Offline, the backup system **is** the DR plan.

- **Format:** versioned JSON envelope (`{version:'3.0', backupDate, operators, treatments, patients,
  appointments, invoices, customExaminations, settings, attachmentsManifest}`) + **attachment file bytes**
  (zipped alongside or in a sidecar folder — manifest-only is a source gap that breaks offline).
- **Encryption (must-have, new):** AES-256-GCM with Argon2id-derived key from a backup password
  (may differ from the app password). Plain-JSON PHI backups are not acceptable.
- **Schedule (must-have, new):** daily/weekly automatic backup to a configurable destination
  (second internal disk, USB stick, clinic share). Keep N most recent; verify each backup by
  decrypt + schema-validate after write. Surface backup health in the title bar area
  (green/amber/red by age).
- **Manual:** "Back up now" → `FileSavePicker` → `medrecplus-backup-<timestamp>.mrbak` (encrypted).
- **Restore:** `FileOpenPicker` → decrypt → **validate entire envelope before touching any row**
  (source's `EnhancedRestoreDialog` pattern) → progress UI → replaces local clinical data. Import path
  also accepts the source app's v1.0/v2.0 plain-JSON backups (migration from existing MedRecPlus, §13).
- **Erasure interaction:** erased patients linger in old backup files until those age out — the UI must
  say so (as the source does).

---

## 10. Security & compliance

### 10.1 Encryption & lock (KEEP, ported)
SQLCipher whole-DB encryption, Argon2id KDF, canary-row setup verification, rekey support, "file is not
a database" on wrong password. Password-only lock screen (setup/unlock/locked), 15-min default auto-lock
on inactivity, 5 failures → 30-min lockout with countdown, emergency wipe (`DELETE ALL DATA` typed +
password + final confirm). Password policy enforced: ≥8 chars, upper+lower+digit+special, denylist,
last-5 history, 90-day expiry warning. All lock/unlock attempts audited.

### 10.2 Privacy features (KEEP, adapted)
- **Lawful basis:** required at patient creation (5 options, never defaulted); change flow with audit.
- **Erasure (Art. 17-style):** typed-full-name confirmation, scope summary (counts of visits/invoices/
  files/AI logs), retention-guard hook, hard delete of patient + visits + invoices + attachment files +
  AI logs, `patient_erased` audit event as the permanent record. No server leg.
- **Portability (Art. 20-style):** per-patient JSON bundle (schema-validated) + human-readable PDF;
  audit `patient_exported`; attachment **bytes** included.
- **Retention:** per-category day counts, purge of soft-deleted rows past retention on startup + every
  24h, `security_incident`/`patient_erased` exempt. **Legal minima are DRAFT placeholders — the clinic's
  counsel must confirm values for Indonesia (UU PDP No. 27/2022), not the EU defaults.**
- **Breach signals:** lockout escalation (3+/24h) + bulk-export volume (>10/24h) → `security_incident`
  events + banner with "Mark reviewed". No notification workflow exists (unchanged from source).

### 10.3 What "compliance" means here
Same posture as the source: *designed to support* record-keeping obligations, not certified. The
privacy-notice text stays draft until counsel reviews. Do not market compliance claims.

---

## 11. Updates without internet

Keep the source's update state machine (idle → checking → available → downloading → installing →
up-to-date/error) and banner UX. Transport: on launch + manual "Check for updates", scan a configured
drop folder (USB drive, e.g. `E:\medrecplus-updates\`, or clinic network share) for a signed manifest +
**MSIX** package. Verify **Ed25519 signature** (public key baked into the app — same trust model as the
source's minisign pinning) *and* the MSIX OS signature before installing. Quiet, dismissible errors for
"no package found / signature invalid". **Decide the signing cert now:** self-signed + manual trust on
the clinic PC; MSIX publisher identity must match the cert subject.

---

## 12. Deliberate product decisions (v1)

Decisions the source left implicit, half-finished, or that the offline context forces. Each is marked
**[DECIDED]** (recommended, implement) or **[OPEN]** (needs boss's call).

### 12.1 Scheduling — [DECIDED] keep visit-record model for v1
"Appointments" stay same-day visit records (no calendar, no reminders). Real future-scheduling
(date/time picker, status, reminders) is the most-requested-shaped gap and becomes phase 2 if the
clinic wants it — it needs new fields (`scheduled_for`, `status`) the source never had.

### 12.2 Invoice status transitions — [DECIDED] confirm + audit, no silent flips
Replace the silent free-form select with guarded transitions: `unpaid → paid` via Record-payment dialog;
`→ void` via confirm + reason; `void → unpaid` (un-void) via confirm. All transitions audited.
Drop the dead `pending` status.

### 12.3 Payment method tracking — [DECIDED] add (new, minimal)
The source tracks no tender at all. v1 adds `payment_method` (Cash / Transfer / QRIS / Other) + free-text
`payment_note`, captured in the Record-payment dialog, shown on the invoice and in XLS exports.
No partial payments in v1 (single tender per invoice; flag as phase 2 if needed).

### 12.4 Line items — [DECIDED] keep catalog-only, qty 1 for v1
No quantities, no manual free-text line items (source has neither). Phase 2 if the clinic asks.

### 12.5 Due dates / payment terms — [DECIDED] out of v1
The source has no due dates. Add only if the clinic does credit billing.

### 12.6 Invoice settings precondition — [DECIDED] keep, with better UX
Invoices still require saved clinic settings, but first-run setup forces clinic/currency/receipt
configuration before the dashboard (mirrors the source's forced operator-setup redirect), so the
error path rarely triggers.

### 12.7 Visit editing — [DECIDED] editable until invoiced, then frozen
Vitals/treatments/discount/notes editable until "Generate invoice"; afterwards the visit is frozen
(explicit banner). The source silently forbids edits — the port states the rule in the UI.

### 12.8 Gender field — [DECIDED] keep `Mr./Ms./Mx.` titles
Preserved as-is for data compatibility with existing backups. Allow editing on the Edit screen
(source inexplicably locks it) — with the change audited.

### 12.9 ICD code links — [DECIDED] local detail dialog
Chips open a local dialog (code, title, chapter from the bundled DB) instead of `https://icd.who.int`.
Offline, external links are dead ends.

### 12.10 Onboarding — [DECIDED] single first-run walkthrough
Collapse 12 auto-starting tours into one guided first-run (setup → patients → visit → invoice →
reports) + "Restart tour" in Help. Keep the TeachingTip-style spotlight engine.

### 12.11 Vitals validation — [DECIDED] add ranges
Source accepts anything (HR 9999). v1 adds sane range warnings (not hard blocks): HR 20–250,
RR 4–60, BP format `sys/dia` with sys 50–300 / dia 20–200, Borg 0–10. Warnings, not rejections —
clinic staff override.

### 12.12 Invoice list default range — [DECIDED] "This month"
Source defaults to *today* and shows an empty list. Default to this month.

### 12.13 Multi-user — [OPEN]
v1 is single-operator-per-PC (operators are attribution labels). If the clinic needs separate logins,
design local profiles + PINs (phase 2) — do not resurrect the cloud RBAC.

### 12.14 Language scope — [OPEN]
Recommend shipping Indonesian + English; the source's other 5 locales (de/fr/es/it/ja) port only if
wanted. Boss's working language is Indonesian — default `id`.

---

## 13. Migration from existing MedRecPlus

The clinic may already have data in the Tauri/web MedRecPlus (IndexedDB/SQLite/Supabase).

1. **Primary path:** in the old app, Settings → Backup → export JSON (v2.0). In the new app,
   Settings → Restore → select file → the importer accepts v1.0/v2.0 envelopes, maps fields
   (cents conversion, `deleted_at` preserved, numbering counters **seeded above imported maxima**),
   validates fully before writing, reports counts + skipped rows.
2. **Numbering continuity:** `sequences` seeded from `max(existing)` so `PT…`/`INV-…` never collide
   or regress.
3. **Attachments:** old backups carry inline base64 — the importer writes them to the attachments
   folder and generates thumbnails.
4. **Passwords:** not migrated (deliberately excluded from backups, as in source) — fresh password
   setup on first run.
5. **ICD DB:** ships with the app; nothing to migrate.

---

## 14. Legal & compliance notes (not legal advice)

- Retention minima and privacy-notice text are **placeholders** in the source and stay placeholders
  here. Before regulated use, the clinic's counsel must confirm: retention periods under **Indonesia's
  PDP Law (UU No. 27 Tahun 2022)** and any Ministry of Health medical-record retention rules
  (historically 5–10 years for certain records — verify, do not assume), lawful-basis mapping for the
  Indonesian context, and breach-notification duties (PDP Law has its own notification regime —
  the source implements detection only).
- Keep the "designed to support compliance, not certified" posture in all clinic-facing text.

---

## 15. Non-functional requirements

| Area | Requirement |
|---|---|
| Offline | 100% of features work with no network, indefinitely. No telemetry, no update checks that fail loudly. |
| Performance | Lists virtualized; 10k patients / 50k visits searchable with <1s filter response; dashboard loads <2s on the clinic PC. |
| Data integrity | SQLite WAL mode; write transactions for numbering + invoice issue; DB integrity check in Settings → Storage; backup verified after write. |
| Security | SQLCipher at rest; Argon2id; lock screen; audit hash chain; encrypted backups. No secrets in logs. |
| Reliability | Crash-safe writes (transactions); restore validates before touching rows; emergency wipe is the password-reset story. |
| Accessibility | Keyboard navigation for lists/dialogs; UI scale 75–175%; honor Windows high-contrast. |
| Printing | Raster path via OS driver (default); LX-310 dot-matrix profile 120×138mm verified against the clinic printer. |

---

## 16. Acceptance criteria (v1)

1. Fresh install → first-run setup (clinic name, currency=IDR, tax PPN 11%, receipt profile,
   operator, password) → dashboard, all offline.
2. Add patient with lawful basis → record number `PT2026…` assigned; basis banner absent.
3. New visit: patient + operator + vitals + 2 treatments + 10% discount → totals card shows
   subtotal − discount = payable; details show frozen-after-invoice banner once invoiced.
4. Generate invoice → `INV-202610-…`, status unpaid, breakdown frozen; change receipt settings →
   reprint shows **issue-time** layout.
5. Record payment (Cash) → status paid, method on invoice; revenue card + operator report include it;
   XLS export contains the row.
6. Print to LX-310 (dot-matrix profile) and Download PDF: **print == PDF == preview**.
7. Erase patient (typed name) → patient + visits + invoices + files gone; `patient_erased` audited;
   old backups still contain the data (documented).
8. Encrypted scheduled backup runs → restore on a fresh profile reproduces data incl. attachments.
9. Lock: 5 wrong passwords → 30-min lockout; auto-lock at 15 min inactivity; `security_incident` logged.
10. AI Insights answers "revenue this month" and "unpaid invoices" with evidence table; disclaimer visible.
11. ICD search "diabetes" → ranked local results, no network.
12. Audit viewer shows the session's events; CSV export downloads.
13. Update: signed MSIX on USB detected → banner → install → version bump.
14. Import of a v2.0 JSON backup from the old app restores patients/visits/invoices with numbering intact.

---

## 17. Phased roadmap

**Phase 0 — Decisions:** resolve the [OPEN] items (§12.13, §12.14), get the MSIX signing cert,
confirm receipt paper profiles against the physical LX-310, counsel review of retention/privacy text.
**Phase 1 — MVP (this PRD):** schema + services + all screens in §7, printing, encrypted manual backup,
migration importer, lock/security, audit viewer. **Phase 2 — Hardening:** scheduled backups + health
indicator, audit CSV/PDF export, ESC/POS text-mode printing, Windows Hello, visit-volume report,
status-transition reasons. **Phase 3 — Optional:** real scheduling (calendar), quantities/manual line
items, partial payments, local LLM narratives (Windows AI Foundry), multi-operator PIN profiles.

---

## 18. Key source artifacts to port (by value)

Highest-value files to have open while implementing (paths relative to `~/workspace/medrecplus`):

- `server/src/mcp/invoiceTotals.ts` — money math (port verbatim to C#)
- `server/src/mcp/receiptLayout.ts` — receipt geometry/paper presets (layout spec)
- `src/components/InvoiceDocument.tsx` — invoice layout, section-for-section source of truth
- `src/utils/invoicePdfGenerator.ts` + `src/utils/invoicePdfPages.ts` — PDF generation semantics
- `src/services/InvoiceSettingsService.ts` — invoice settings schema
- `src-tauri/resources/icd11.db` — bundled ICD-11/ICF database (ship as-is)
- `src-tauri/src/lib.rs:674–751` — ICD ranking SQL
- `src-tauri/src/encryption.rs` — SQLCipher + Argon2 setup/rekey/unlock semantics
- `src/services/SecurityService.ts` — audit taxonomy, password policy, lockout, breach signals
- `src/services/ErasureService.ts` — erasure cascade + guards
- `src/services/RetentionService.ts` — retention schedules + purge
- `src/services/AIInsightsService.ts` + `src/utils/ai/*` — deterministic insights engine
- `src/utils/backupFormat.ts` — backup envelope versions (import compatibility)
- `src/utils/portableExportSchema.ts` — Art. 20 bundle schema
- `src/utils/consentBasis.ts` + `src/utils/retentionGuard.ts` — consent/erasure guards
- `src/utils/onboardingSteps.ts` — tour content
- `src/i18n/locales/id/*` — Indonesian strings (largest head start on localization)

---

## 19. Open questions for boss

1. **Multi-user?** Single operator per PC for v1, or do you need separate staff logins/PINs on day one? (§12.13)
2. **Languages:** Indonesian + English enough, or port all 7 locales? (§12.14)
3. **Scheduling:** keep the visit-record model, or do you want real future-appointment scheduling in v1? (§12.1)
4. **Printers:** is the LX-310 the only printer, or should A4/thermal profiles be first-class too?
5. **App name/identity:** keep "MedRecPlus", or a distinct name (e.g. "MedRecPlus Offline") to avoid confusion with the cloud edition? MSIX identity + cert depend on this.
6. **Backup destination:** USB stick, second disk, or clinic network share as the scheduled target?
7. **Payment methods:** Cash/Transfer/QRIS/Other covers it, or do you need more (e.g. split tender, partial payments) in v1?

---

*End of PRD v1.0. Research files: `research/ui-workflows.md`, `research/data-model.md`, `research/features-offline-fit.md`.*
