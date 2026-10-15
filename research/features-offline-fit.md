# MedRecPlus → Offline WinUI3 Port: Feature-by-Feature Offline Fit

- **Repo:** `~/workspace/medrecplus` (React + Tauri frontend, `src/`; shared logic in `server/src/mcp/*`; Rust shell in `src-tauri/`). Read-only analysis; no builds run.
- **Target:** a completely-offline WinUI3 rewrite for Windows 11, single PC, internal clinic use — no internet, no cloud, no multi-device sync, no OAuth, no Supabase.
- **Verdicts:** **KEEP** = ports essentially as-is (maybe with a library swap) · **ADAPT** = works offline but needs rework / has a cloud half to strip · **CUT** = cloud-dependent, drop (or replace with an offline equivalent).

---

## Invoicing

**Invoice numbering — ADAPT.** Numbering is client-generated as `INV-YYYYMM-NNNN`: `generateInvoiceNumber` (`src/pages/AppointmentDetails.tsx:602`) fetches up to 1000 invoices from the server (comment says local may be stale), takes `max+1` zero-padded to 4, and falls back to a timestamp suffix on error. Real uniqueness is enforced by the server: unique index `(workspace_id, invoice_number)` (`server/src/db/schema.ts:243`) with HTTP 385 rejections and client retry. For a single-PC SQLite app this gets *safer*: `SELECT MAX(seq)+1` in a transaction plus a local UNIQUE constraint reproduces the guard without any server fetch.

**Invoice snapshot / line items — KEEP.** An invoice freezes the appointment's treatments (name, price, notes, provenance via `describeTreatmentProvenance`), vital signs, operator and patient identity at issue time (`AppointmentDetails.tsx:636–707`); later appointment edits never rewrite the invoice. Pure data — ports 1:1 to an `InvoiceTreatments` table or embedded JSON.

**Discounts & tax — KEEP (port the math verbatim).** Invoice-level discount only (`discountType`/`discountValue`/`discountAmount`, `src/types/index.ts:193–198`) applied before tax; tax config (`tax_label` e.g. "VAT"/"PPN", `tax_type` none|percent|fixed, `tax_value`) comes from invoice settings and is frozen onto the invoice (`InvoiceSettingsService.ts`, `AppointmentDetails.tsx:668–691`). The math is pure: `computeReviewedInvoiceTotals` (`server/src/mcp/invoiceTotals.ts`) does integer-cents arithmetic with basis-point percents, has regression tests (`src/test/utils/invoicePdfGenerator.test.ts`). Port the function to C# as-is; the freeze-tax-at-issue pattern is exactly right offline (PPN 11% = percent tax, label "PPN").

**Payment methods — CUT (nothing to port).** The `Invoice` type has no `paymentMethod` field — status is only `paid | unpaid | void | pending` (`src/types/index.ts:168–208`). `PaymentService.ts` is SaaS billing (PayPal order create/capture for plan upgrades via cloud API), unrelated to clinic tender. Do not port it; if the clinic needs cash/transfer/QRIS tender tracking, that's new work (add a `payment_method` column + status transitions).

**PDF generation — ADAPT (implementation), KEEP (spec).** Fully client-side today: `src/utils/invoicePdfGenerator.ts` wraps shared code (`server/src/mcp/invoicePdf.ts`, bundled into the frontend, not a backend service) built on **jsPDF 4.2.1**, with Inter + VT323 dot-matrix fonts embedded from base64 blobs in the bundle (`interPdfFonts.ts`, `vt323FontBase64.ts`). `InvoiceDocument.tsx` (React preview) is documented as the "single source of truth" mirrored section-for-section by the PDF generator; preview (`InvoicePdfPreview.tsx`) rasterizes real PDF bytes via **pdfjs-dist 6.4.299** with a local worker and local fonts — explicitly no CDN, no network. Port options: (a) re-implement in a .NET PDF library (QuestPDF/MigraDoc/PdfSharp) using `receiptLayout.ts` (`server/src/mcp/receiptLayout.ts`) + `InvoiceDocument.tsx` as the layout spec — this layout file (paper sizes, margins, fonts, section toggles) is the single most valuable port artifact; or (b) host a WebView2 page running the existing JS renderer. The embedded-font approach maps directly to WinUI3 resource embedding.

**Print path — ADAPT.** `handlePrint` (`InvoiceDetails.tsx:189–218`): canonical PDF bytes → every page rasterized to 300-dpi PNG → HTML of full-page `<img>`s with `@page` sized to the paper → printed through the OS driver (hidden iframe `print()` on desktop; native Android bridge on Android). Printing is always rasterized graphics through the OS driver — that works unchanged on WinUI3 (`PrintDocument`/WebView2 print to the LX-310's Windows driver). Dot-matrix/LX-310 support today is **layout-only** (paper preset `dotmatrix120` = 120×138 mm in `receiptLayout.ts:70`, VT323 "dot-matrix look" font, `ReceiptSettings.tsx:315`): grep found **zero** ESC/POS, zero raw-print, zero Tauri printer plugin (`src-tauri/src/lib.rs` only does DB init + an E2E eval server). An offline port *could* add a real text-mode ESC/POS path for the LX-310 (fast, ribbon-cheap) — but that is new code, not a port.

**Invoice settings — ADAPT (strip the cloud half).** `src/services/InvoiceSettingsService.ts` manages currency, tax label/type/value, receipt header/footer, paper preset + full receipt layout, document language — but the architecture is cloud-first: workspace owner publishes via `PUT /invoice-settings` (`ApiClient`) with optimistic versioning (409 handling), sha256 `settings_hash`, drafts, and a local-profile fallback (`createLocalProfile`, version 0). Keep the **schema** (it's good data design) and the local-profile path; cut owner-publish, version/hash conflict machinery, `WorkspaceService`/`SupabaseService` identity. On one PC, settings are a local DB row edited in a settings screen.

**Document snapshot / anti-tamper — CUT the verification layer.** `InvoiceDocumentService.ts` fetches a server-owned snapshot (`GET /invoices/{id}/snapshot`) and verifies it against the invoice's `settings_hash` to guarantee multi-device WYSIWYG — meaningless on one PC. Keep the *freeze-settings-at-issue* concept (reprint old invoices identically after settings change), delete the hash/server layer.

---

## Reports page

**Mechanics — KEEP (mechanics), ADAPT (chart lib).** `src/pages/Reports.tsx` (1,856 lines), chart library **recharts** (only `LineChart`/`Line`/`XAxis`/`YAxis`/`ResponsiveContainer`; plus `LazyLineChart.tsx`), Excel export via **ExcelJS** writing `.xlsx` client-side. Three reports: (1) per-operator performance — appointment count, invoices, revenue per operator, filterable by month/year/operator, with an invoice-list drill-down modal; (2) period export — same filters → `Invoices_<period>_<operators>.xlsx`; (3) per-patient report — patient search → appointment history + vital-signs trend charts (BP systolic/diastolic, heart rate, respiration, Borg scale). All aggregation is client-side JS (`generateReportData`) over `SimpleDataService.getAllAppointments()/getAllInvoices()/getAllOperators()`; no server aggregation, no direct Supabase queries. There are **no diagnosis/ICD frequency reports** and no clinic-level visit-volume dashboard today. **Verdict KEEP** — the fetch→filter→aggregate→chart→XLSX pipeline is already offline-capable (`SimpleDataService` just always takes its offline path against local SQLite in the port). Adaptations: recharts → LiveChartsCore.SkiaSharpView.WinUI or CommunityToolkit charts (only line charts used — shallow port); ExcelJS → ClosedXML/OpenXML with the same `.xlsx` conventions.

---

## AI Insights

**Mechanics — KEEP.** The feature (`src/pages/AIInsights.tsx` + `src/components/ai/`) is a natural-language clinic-analytics chat, but the real work is deterministic: `AIInsightsService.ts` classifies intent by keyword matching, pulls a local data snapshot (`SimpleDataService.getAIContextSnapshot()` — patients, appointments, invoices, operators, treatments), routes to 7 fixed tools (KPI snapshot, patient timeline, revenue summary, invoice follow-ups, operator performance, treatment trends, data quality), and composes plain-text answers from templates. An LLM is only an optional narrative layer via `AIModelProvider.ts` modes `none | local | cloud` — **`local` is an unimplemented placeholder** (`'[Local AI not yet implemented]'`), **`cloud` is a generic fetch() to a user-configured endpoint**, and the settings UI (`src/pages/settings/AISettings.tsx:161,169`) renders both **disabled** — only `none` is selectable. No API keys exist anywhere; nothing leaves the machine. **Verdict KEEP** — the easiest port: re-implement the snapshot + tool functions + Q&A UI in C#, drop `AIModelProvider` entirely. If a narrative layer is ever wanted, phi-4-mini/ONNX via Windows AI Foundry or bundled GGUF+llama.cpp are a later optional addition, not needed for parity.

---

## ICD-11 search

**Mechanics — KEEP (trivially).** `ICDSearchInput.tsx` is a debounced typeahead (2-char minimum, 400 ms) that picks ICD-11 MMS/ICF codes as chips. `ICDService.ts` is dual-mode: the **production path is fully offline** — Tauri commands `search_icd`/`lookup_icd_code` (`ICDService.ts:232–288`) query a **bundled read-only SQLite DB** `src-tauri/resources/icd11.db` (**9.56 MB, 35,417 MMS + 1,663 ICF entities**); Rust (`src-tauri/src/lib.rs:674–751`) runs escaped-LIKE queries over `icd_entities` with relevance ordering (exact code → code prefix → title prefix → substring), limit 20. The WHO API path (`id.who.int` via OAuth client-credentials, `ICDService.ts:42–46, 292–400`) is **web-dev fallback only**, requires internet + credentials, and is absent from production builds. **Verdict KEEP** — ship the same `icd11.db` (or a regenerated one) as a WinUI3 package asset and run the same SQL from `Microsoft.Data.Sqlite` — schema (`code, title, linearization, chapter, is_leaf, entity_uri, foundation_uri`) and the ranking SQL port verbatim. Drop the WHO API path entirely.

---

## Audit trail

**Mechanics — ADAPT (keep core, strip sync).** `AuditService.ts` is a fire-and-forget emitter facade (`logDataEvent` with 60 s dedupe/throttle); persistence is in `SecurityService.ts:640–656` → IndexedDB `MedRecPlusSecurityLogs`, capped at **1000 entries FIFO** (`SecurityService.ts:93`). Events (`AuditEvent`: id, timestamp, event, details, fingerprint, `synced: 0|1`) carry **identity by ID only — no patient names, no clinical content, no contact data** (type-enforced). Event taxonomy (`SecurityService.ts:15–50`): `patient_viewed/created/updated/deleted/exported/printed/erased`, `consent_updated`, `security_incident` (permanent, purge-exempt), `record_created/updated/deleted/searched`, `attachment_uploaded/downloaded`, `user_invited`, `role_changed`, `app_locked`, `unlock_attempt/success/failed`, `react_error_boundary`. Viewer: Settings → Security audit. **Cloud half:** `syncNow()` POSTs unsynced batches (200) to `/audit-events/batch`, driven by a 5-minute loop + `online` trigger; actor ids resolve from Supabase auth. **Verdict ADAPT** — the append-only PHI-access log + owner-visible viewer ports cleanly into the local encrypted SQLite (which also removes the 1000-entry IndexedDB cap and browser-storage volatility). Cut `syncNow`, `startAuditSyncLoop`, the `synced` flag, and `ErasureSync` cross-device marker polling. **Gap to close:** there is no audit-log export UI today (only a viewer); add CSV/PDF export in the WinUI3 port for compliance record-keeping.

---

## GDPR / compliance

**Right to erasure — KEEP (local), CUT (server leg).** `ErasureService.ts` `erasePatient()` (owner-gated, retention-guard-checked via `src/utils/retentionGuard.ts`, typed-name confirmation) performs a true hard delete of the patient, appointments, invoices, and matching AI-log entries from local stores, and drains `OfflineChangeLog` so nothing resurrects the rows. The server leg (`POST /api/patients/:id/erase`, `localStorage` pending-erasure queue, `ErasureSync.pollErasureMarkers()` for cross-device purges) and Supabase Storage object deletion are cloud-bound and meaningless on one PC — cut them. Port the local-purge cascade against local SQLite; keep the `patient_erased` IDs-only audit event as the legal record. Caveat carries over: erased rows survive inside old JSON backup snapshots until those files age out (the UI says so).

**Data portability export — KEEP (nearly as-is).** `PortableExportService.ts` `exportPatientData()` builds a `PatientDataBundle` entirely from on-device stores (record, appointments, invoices, attachments manifest, consent record), validates against `portableExportSchema.ts`, and writes two files — a JSON bundle and a human-readable A4 PDF (jsPDF, helvetica) — logging an IDs-only `patient_exported` audit event and notifying `BreachDetection`. The module docblock says the flow already works offline. Only the file-write plumbing is Tauri-specific (WinUI3: `FileSavePicker` / direct I/O + a .NET PDF lib). Note: the attachments manifest currently assumes cloud-hosted bytes — see Attachments below; the port must bundle actual file bytes in exports.

**Retention / housekeeping — KEEP.** `RetentionService.ts`: per-workspace schedule (`patients/appointments/invoices/attachments` = 3650 days; `audit_events` = 730 days) with legal minima the clinic may only extend, never shorten (enforced in UI + `saveRetentionSettings`); `startRetentionScheduler()` purges on app start and every 24 h (owner-gated); only soft-deleted rows past retention are purged (`patient_erased` and `security_incident` audit events exempt). Pure local logic — ports to a WinUI3 background timer + local settings. **Hard blocker inherited, not introduced:** legal minima are explicitly **DRAFT placeholders** ("a lawyer must confirm the per-Member-State values," e.g. DE §630f BGB 10 years) — counsel sign-off is still required before regulated use. Arguably matters *more* offline (no server-side retention backstop).

**Consent records — KEEP.** No `ConsentService` file — consent is per-row fields: `CONSENT_BASIS_OPTIONS` (`explicit_consent`, `contract`, `legal_obligation`, `vital_interests`, `public_health`) in `src/types/index.ts`, `validateConsentBasis` (never silently defaults) / `normalizeConsentBasis` in `src/utils/consentBasis.ts`; `EditPatient.tsx` records basis + date + recorded-by; `PatientDetails.tsx` shows a "lawful basis not recorded" banner + modal. Changes emit `consent_updated` audit events. Pure field-level bookkeeping — trivial in local SQLite.

**Breach detection — ADAPT (partial).** `BreachDetection.ts`: three local signals → `security_incident` audit events (IDs/scalars only, no PHI) + an unacknowledged-incident counter: (1) local-unlock lockouts ≥5 attempts, escalating at 3 lockouts/24h; (2) API 403-denial spikes >5/10 min (called from the ApiClient interceptor); (3) bulk-export exfiltration >10 patient exports/24h. **Keep signals 1 and 3** (fully local, valuable on a clinic PC); **cut signal 2** (RLS/permission-probing detector tied to the Supabase layer). Note: this is detection/logging only — there is no 72-hour breach-notification workflow today, so the port loses nothing there.

**Lawful-basis card — KEEP.** Banner + modal on the patient detail page (`src/pages/PatientDetails.tsx` ~line 989). Recreate as a WinUI3 info bar + dialog with the same data contract.

---

## Backup / restore

**Mechanics — ADAPT (keep core, harden for offline).** Export: `SettingsBackupService.ts` + `BackupRestoreSettings.tsx` gather operators, treatments, patients, appointments, invoices, custom examinations plus settings (device-preference allowlist, invoice profiles; AI passwords deliberately excluded), stamp `backupDate` + `version: '2.0'`, and write a **single plain-text JSON** (`patient-management-backup-<timestamp>.json`) via Tauri save dialog or browser download. Restore: `EnhancedRestoreDialog.tsx` + `ChunkedDataRestore.ts` (progress UI, quota-aware, replaces local clinical records); settings restore validates the whole envelope first. **Formats: plain JSON — not encrypted, not zipped. Destinations: local file only** — no cloud upload path exists here. **Scheduling: none** — fully manual. What's included: everything local (attachments inline base64 + thumbnails; server-side originals referenced by `storagePath` only). **Verdict ADAPT** — the dump/restore machinery ports cleanly (FileSavePicker/FileOpenPicker replace Tauri dialogs; keep the validate-before-touching-rows design), but the offline port must harden it because the backup becomes the *only* copy: (1) **encryption is missing** — plain-JSON PHI on a clinic PC is a real exposure; add password-based AES-GCM (or DPAPI-wrapped key) — treat as must-add; (2) add **scheduled/automatic backups** (daily to a second disk/USB) since there's no cloud safety net and today's flow is manual-only; (3) the erasure-backup interaction carries over (erased patients linger in old snapshots — UI must say so, as today). The cloud's implicit second-copy redundancy must be replaced deliberately.

---

## Attachments

**Mechanics — ADAPT (re-architect the storage tier).** `AttachmentUploadService.ts` uses a three-tier design: (1) **full-resolution images → Supabase Storage** (cloud) via backend-signed URLs (`ApiClient.getAttachmentUploadUrl`), server-side size validation; (2) **local offline copy** — the full image as a data URL inline in the record, mirrored into IndexedDB (`mirrorLocally`); (3) **~20 KB thumbnails** inline in the record for list views. `flushAll()` is an idempotent, concurrency-3 background uploader (`pending` → `uploaded`); no-op in offline mode. Limits (`src/utils/attachmentLimits.ts`): picker accepts jpeg/png/webp, 10 MB source cap, 5 MB compressed stored cap, 20 KB thumbnails, 1 MB thumbnail payload per record. **Verdict ADAPT — the cloud tier is the hard cut.** Cut Supabase Storage + the signed-URL flow (`flushAll`, `uploadOne`, `getAttachmentUploadUrl`, `countPending`); **keep the local tier but restructure it**: inline base64 balloons SQLite and JSON backups — the natural offline design is a **local attachments folder** (`%LOCALAPPDATA%\MedRecPlus\attachments\<patient-id>\…`) holding original files on disk, with the DB storing filename, mimeType, size, checksum + a small thumbnail blob. Keep the size caps and type policy, keep thumbnail generation, keep backup/export pointed at the folder — crucially, **exports/backups must bundle actual file bytes** (today's portable export ships only a *manifest*, which breaks without the cloud). Erasure deletes the disk files instead of Supabase objects.

---

## Workspace / auth / security

**SQLCipher DB encryption + Argon2 KDF — KEEP.** Password → Argon2 → SQLCipher `PRAGMA key` on `patients.db` via Rust commands `setup_encryption` / `unlock_database` / `rekey_database` (`src-tauri/src/encryption.rs`), setup verified by a canary row round-trip, wrong password fails with "file is not a database". WinUI3 equivalent: `Microsoft.Data.Sqlite` + SQLCipher provider (prefer a NuGet-bundled native, not a system dependency — the vendored-build lesson from `Cargo.toml`), Argon2 via a pure-managed library (`Konscious.Security.Cryptography`).

**Lock screen, auto-lock, lockout, password policy — KEEP.** `UnifiedLockScreen.tsx` modes (`setup`/`unlock`/`migrate`/`locked`); 15-min inactivity auto-lock, 5 failed attempts → 30-min lockout with countdown, all attempts audited (`app_locked`, `unlock_attempt/success/failed`). Password policy (`SecurityService.ts`): min 8 chars, upper+lower+digit+special, common-password denylist, last-5 reuse history, 90-day expiry, migration prompts — all client-side, ports as-is.

**Supabase Google OAuth account — CUT.** `AuthLogin.tsx` offers only Google OAuth (`SupabaseService.signInWithGoogle()`); `AuthGate.tsx` tolerates offline via a cached session but a first login needs internet. No email/password exists. Meaningless on an offline PC — cut entirely.

**Supabase TOTP MFA — ADAPT → Windows Hello.** MFA today is Supabase TOTP only (`enrollTotp`/`challengeTotp`/`verifyTotp` in `SupabaseService.ts`, UI in `PasswordAndSecuritySettings.tsx`); grep for biometric/Hello in `src/` returns nothing (the "fingerprint" in `getBrowserFingerprint()` is a device hash for audit tagging, not auth). Dead offline. WinUI3 equivalent: **Windows Hello** (`UserConsentVerifier`) as an optional second factor for unlock, or Hello-as-unlock with the password-derived key in Credential Locker.

**Workspaces (multi-tenant, invite codes, roles) — CUT (adapt minimal).** `WorkspaceService.ts` is a thin REST client (`/workspaces/me|create|join|members`, invite codes, `DELETE /workspaces/data`); joining another workspace wipes the local mirror. Owner/member roles, server-enforced trial/paid gating. The whole concept is server multi-tenancy — cut for a single PC. If multiple staff need logins, adapt to **local operator profiles** — a local `operators` table already exists (`SimpleDataService.getOperators()`, `OperatorSettings.tsx`) for record attribution; keep operators as attribution, not as auth principals.

**Emergency wipe — KEEP/ADAPT.** The 6-step modal flow (type `DELETE ALL DATA` → password confirm → final confirm) keeps its UX; the Supabase sign-out step drops away, DB-file deletion + IndexedDB/localStorage clears stay (→ local SQLite delete + settings wipe). The "forgot password" path (wipe + restore from backup) is the correct offline password-reset story — keep it.

---

## Updater

**Mechanics — ADAPT (keep the state machine, swap the transport).** `UpdaterService.ts` + `tauri-plugin-updater`: polls `https://github.com/germanygsg/medrecplus/releases/latest/download/latest.json`, installs **Ed25519/minisign-signed** releases (public key pinned in `tauri.conf.json`), `UpdateBanner.tsx` shows check-available-download-install states, polls on mount + every 6 h, quiet failures "common in offline clinics"; Android goes through Firebase App Distribution (irrelevant here). The mechanism assumes internet + a release feed + a pinned key. **Recommended offline story:** keep the state machine (idle→checking→available→downloading→installing→up-to-date/error) and the banner UI verbatim — they're transport-agnostic. Replace the transport with a **signed update package from USB or a clinic network share**: on launch (plus a Settings → "Check USB/share" button) scan a drop folder (e.g. `\\clinic-share\updates\` or removable drive) for a signed manifest + **MSIX** package (Windows 11 native, OS-enforced signature — maps 1:1 to the minisign model; keep the Ed25519 public key baked into the app as a second verification). The 6-hour poll becomes a folder-watch or manual check; reuse the quiet-error UX for "package not found / signature invalid."

---

## Diagnostics

**Mechanics — KEEP (local), CUT (server upload).** There is **no diagnostics/health page** (zero grep hits for "diagnostic"). What exists: `logService.ts` (user-facing activity feed — patient created/updated, appointments, invoices; last 500, via `UnifiedStorage`), the SecurityService audit store, and `errorReporter.ts` — the **only telemetry**: catches `window.onerror` + `unhandledrejection` and **POSTs sanitized reports to `${API_BASE_URL}/client-errors`** (message ≤500 chars, stack ≤2000, pathname only, PHI whitelist, 10/session cap) — cloud-dependent, cut. No PostHog/Sentry/Amplitude. **Verdict:** keep the activity-log and audit-viewer concepts (local-first); replace the `client-errors` POST with an **"Export diagnostics" bundle** (Settings → zipped local logs + DB integrity check + app version + Windows build info) that staff hand to support on USB.

---

## Onboarding tours

**Mechanics — KEEP.** Client-side spotlight tours: `src/components/Onboarding/` (`OnboardingOverlay.tsx` 325 lines — DOMRect targeting, responsive tooltip placement; `OnboardingSpotlight.tsx`; `OnboardingTooltip.tsx`), per-page step definitions in `src/utils/onboardingSteps.ts`, state in `src/contexts/OnboardingContext{,Core}.tsx`, completion/skip persisted via `DbSettings` (and synced to the prod DB via `user_preferences` in the cloud version — that sync leg drops). No network involvement. Trivially portable: WinUI3 overlay with `TeachingTip`/`ContentDialog` anchored to named XAML elements; persist completion in the local settings store.

---

## src-tauri packaging constraints → WinUI3 translations

The Tauri layer is a thin, honest native shell — port the *decisions*, not the code:

| Current (Tauri) | Detail (`src-tauri/`) | WinUI3 equivalent |
|---|---|---|
| Window | 1280×720, min 320×480, **`decorations: false`** (custom title bar; `--title-bar-height` CSS everywhere) | `AppWindow` 1280×720 + `ExtendsContentIntoTitleBar = true`; recreate the custom title bar in XAML — the frameless-window work is the biggest cosmetic port cost; audit every drag region |
| Single instance | `tauri-plugin-single-instance` (desktop-only); 2nd launch focuses main window and forwards `medrecplus://` args | `Microsoft.Windows.AppLifecycle.AppInstance` — `FindOrRegisterForKey` + `Activated` redirect; same focus-and-forward |
| Auto-start | **None** — no autostart plugin or registry code | New decision, not a port: MSIX `StartupTask` extension (`windows.startupTask`) if the clinic wants launch-on-boot |
| Bundled data | `resources/icd11.db` (9.5 MB) shipped in the bundle | MSIX package asset (`ms-appx:///Assets/icd11.db`), opened read-only via SQLite |
| Crypto | SQLCipher via rusqlite vendored OpenSSL (no system OpenSSL needed) | SQLCipher via NuGet-bundled native (`SQLitePCLRaw` bundle); Argon2 pure-managed — never a system dependency |
| Deep links | `medrecplus://` registered for the OAuth callback | Irrelevant once OAuth is cut — drop |
| Installer | Wix/NSIS not customized; publisher "BSP Center Clinic", category Medical | MSIX via Windows App SDK packaging project; **plan the signing cert now** (self-signed + manual trust, no Store) — publisher identity must match the cert |
| Rust command surface | ~40 `invoke` commands: encrypted-SQLite CRUD, kv `settings` store (`get_setting`/`set_setting`), `restart_app`, `eval_js` (E2E only) | Each becomes a C# service; keep the **kv-settings-in-encrypted-SQLite** pattern verbatim — it exists because IndexedDB/localStorage proved unreliable |
| CSP / WebView | Strict CSP in `tauri.conf.json`; `webviewInstallMode: embedBootstrapper` | Disappear with the WebView — XAML has no script-injection surface |

---

## Verdict summary

| Feature | Verdict | Notes |
|---|---|---|
| Invoice numbering | ADAPT | Local UNIQUE + MAX+1 in tx replaces server fetch/index |
| Line items / snapshot | KEEP | 1:1 to local table/JSON |
| Discounts / tax math | KEEP | Port cents-arithmetic verbatim |
| Payment methods | CUT | None exist; PayPal SaaS billing not clinic tender |
| PDF generation | ADAPT | jsPDF/pdfjs → .NET lib (QuestPDF/MigraDoc) or WebView2 reuse of existing JS |
| Print path | ADAPT | Raster-through-OS-driver works unchanged; LX-310 text-mode ESC/POS would be new code |
| Invoice settings | ADAPT | Keep schema; cut publish/version/hash cloud flow |
| Reports page | KEEP / ADAPT | Keep pipeline; recharts→LiveCharts, ExcelJS→ClosedXML |
| AI Insights | KEEP | Deterministic tools already offline; LLM modes unimplemented |
| ICD-11 search | KEEP | Bundled 9.5 MB SQLite ports verbatim; WHO API path is dev-only |
| Audit trail | ADAPT | Local SQLite store; cut server sync; **add CSV/PDF export (gap)** |
| Erasure | KEEP/ADAPT | Keep local purge cascade; cut server leg + ErasureSync |
| Portability export | KEEP | Already offline; bundle attachment *bytes*, not just manifest |
| Retention | KEEP | 100% local; lawyer must still confirm minima (DRAFT today) |
| Consent / lawful basis | KEEP | Field-level bookkeeping, no cloud |
| Breach detection | ADAPT | Keep lockout + export-volume signals; cut 403-denial signal; no notification workflow exists |
| Backup/restore | ADAPT | JSON machinery ports; **add encryption (must-have)** + scheduled auto-backup |
| Attachments | ADAPT | Cut Supabase tier; local folder on disk + DB metadata |
| SQLCipher + Argon2 | KEEP | Direct .NET equivalents |
| Lock screen / policy | KEEP | Pure client logic |
| OAuth / cached session | CUT | Requires internet |
| TOTP MFA | ADAPT → Windows Hello | `UserConsentVerifier` second factor |
| Workspaces | CUT | Server multi-tenancy; keep operators as attribution |
| Emergency wipe | KEEP | Drop the sign-out step |
| Updater | ADAPT | Keep state machine + banner; transport → signed MSIX from USB/share |
| Diagnostics | KEEP/CUT | Keep activity log; cut `client-errors` POST; add export-diagnostics bundle |
| Onboarding tours | KEEP | WinUI3 TeachingTip port of the same steps |
| Tauri shell decisions | ADAPT | Single-instance, custom chrome, kv settings, bundled ICD DB → MSIX equivalents |

### Hard cloud cuts (no offline equivalent to replicate)
Supabase Auth / Google OAuth / cached sessions · Supabase TOTP · Workspace REST API + invite codes + roles + plan gating · Audit-event server sync · `ErasureSync` cross-device markers · Supabase Storage (attachments) + signed-URL upload flow · `/invoice-settings` publish + document-snapshot hash verification · `client-errors` telemetry POST · GitHub Releases update feed · Firebase App Distribution · WHO ICD API dev path · `PaymentService` PayPal billing.

### Gaps the port must close (not introduced by it)
1. **Backup encryption** — plain-JSON PHI is the exposure; password-based AES-GCM is a must-add since the file becomes the only copy.
2. **Scheduled automatic backups** to a second disk/USB — today's flow is manual-only; there's no cloud safety net.
3. **Audit-log export** (CSV/PDF) — viewer exists, export doesn't.
4. **Retention legal minima are DRAFT placeholders** — counsel sign-off required before regulated use.
5. **Attachments in exports/backups** — must include file bytes; the manifest-only design breaks without the cloud.
6. **Payment/tender tracking** — doesn't exist today; a clinic POS needs cash/transfer/QRIS records — new work.
7. No breach-notification (Art. 33/34) workflow exists — detection only; unchanged by the port, but note it.
