# MedRecPlus — UI & Workflow Analysis

> Source: read-only exploration of `~/workspace/medrecplus` (React + Joy UI web app,
> also shipped as Tauri desktop/Android). Every claim cites the source file.
> Purpose: feed for a PRD for a **WinUI 3, fully offline** port for a new clinic
> computer running Windows 11.

---

## 1. Global navigation & layout

### 1.1 Navigation model

- **Desktop**: fixed left sidebar (`src/components/Layout/Sidebar.tsx`). Five nav
  items total — four main tabs + Settings pinned at the bottom with a divider:
  1. **Dashboard** (`/`) — Dashboard icon
  2. **Patients** (`/patients`) — People icon
  3. **Appointments** (`/appointments`) — CalendarMonth icon
  4. **Invoices** (`/invoices`) — Receipt icon
  5. **Settings** (`/settings`) — bottom-pinned Settings icon
- Sidebar is collapsible (icon-only 3.75rem vs full 12.5rem labels) via a toggle
  button in the sidebar header; auto-collapses below 900px viewport width.
- Active item gets a filled background + primary-colored icon.
- **Mobile (<900px)**: sidebar becomes an auto-hiding bottom tab bar (hides on
  scroll down, reappears on scroll up; all 5 items icon-only).
- All nav labels are i18n (`common:nav.*`); each has a `data-onboarding` hook.
- Reports, AI Insights have **no sidebar entry** — Reports is reached via route
  `/reports` (linkage TBD), AI Insights via `/ai-insights`; detail pages drill
  down from list rows. WinUI port should decide where these live (e.g.
  NavigationView footer items or toolbar buttons).

### 1.2 Title bar (desktop only)

`src/components/Layout/TitleBar.tsx` — rendered only in the Tauri desktop build
(`isTauriDesktop()`; returns `null` on web/mobile). Custom 40px draggable bar:
- macOS-style traffic-light window controls (close/minimize/maximize-restore)
  with a **close-confirmation modal** ("Exit the app?" — Cancel / Exit danger).
- Double-click toggles maximize; single-click drags the window.
- **Offline-mode indicator**: amber "Offline (N pending)" badge when
  `appMode === 'offline'`; red "No connection" badge when the network probe fails.
- Light/dark theme toggle button; notification bell with **unread activity-log
  badge** (polls every 5s, dropdown shows 5 most recent activity entries,
  "View All Logs" → `/settings/logs`).
- For WinUI 3: drop the custom traffic lights — use the standard Windows
  title bar, but keep the equivalents: offline/pending-changes indicator, theme
  toggle, and an activity/audit entry point in the top chrome.

### 1.3 Cross-cutting UI patterns

- **Feedback**: global toast system (`src/components/Toast.tsx` — success /
  warning / error / info, auto-dismiss 5s, top-right, stacked) plus
  `ConfirmDialog` / `AlertDialog` / `PromptDialog`
  (`src/components/ConfirmDialog.tsx`) used for deletes, errors, and text
  prompts. Destructive actions always use a `danger` confirm dialog; many
  deletes also show an `AlertDialog` afterwards (success/warning/danger).
- **Loading**: shimmer skeletons everywhere (`src/components/shimmer/*` —
  ShimmerTable, ShimmerList, ShimmerStats, ShimmerDetail, ShimmerCard,
  ShimmerPlaceholder) during first load; small spinners for in-section refreshes.
- **View mode**: global table-vs-card view toggle persisted per context
  (`src/contexts/GlobalViewContext.tsx`) — the patient/appointment/invoice
  lists and the custom-examinations settings all share it.
- **Column visibility**: per-list column pickers persisted in localStorage
  (e.g. `medrecplus:patients:columnVisibility`, `medrecplus:appointments:columnVisibility`).
- **Items per page**: 10/25/50 selectors persisted per list.
- **FABs**: each main list screen has a floating "New/ Add" action button
  (bottom-right, repositioned above mobile bottom nav).
- **Empty states**: dashed-border placeholders with illustration icons and a
  "create one" CTA link; card/table variants differ.

---

## 2. Data model (entities the UI works with)

From `src/types/index.ts` — the domain model the WinUI port must mirror
(field-for-field where marked):

- **Patient** (`id?: number`, `record_number` string, `name`, `age` number,
  `address?`, `phone_number`, `initial_diagnosis?`, `icd_codes?: ICDCode[]`,
  `gender?: 'Mr.' | 'Ms.' | 'Mx.'`, `created_at/updated_at/deleted_at`,
  `attachments?`, **`consent_basis?: ConsentBasis | null`** — GDPR lawful basis,
  one of `explicit_consent | contract | legal_obligation | vital_interests |
  public_health` (`CONSENT_BASIS_OPTIONS`), plus `consent_date`,
  `consent_recorded_by`).
- **Appointment** (`id`, `patientName`, `patientId`, `operatorName`,
  `operatorId`, `date`, **`vitalSigns: VitalSigns`**, `treatments: Treatment[]`,
  `attachments?`, `totalPrice`, `discountType?: 'none'|'percent'|'fixed'`,
  `discountValue?`, `notes?`, `description?`, `diagnosis_codes?: ICDCode[]`).
  **No status field** — lifecycle is implicit (created → optionally invoiced).
- **VitalSigns** (`bloodPressure: string`, `respirationRate: number`,
  `heartRate: number`, `borgScale: number`, plus dynamic
  `custom_<id>: string|number` for custom examinations).
- **Treatment** (`id`, `name`, `description?`, **`price`**, `notes?` + clinical
  provenance fields). The treatment catalog is admin-configured.
- **Invoice** (`id`, `invoiceNumber`, `appointmentId`, `patientName`,
  `patientId`, `operatorName`, `operatorId`, `date`, `appointmentDate`,
  `vitalSigns`, `treatments`, `totalAmount`,
  **`status: 'paid' | 'unpaid' | 'void' | 'pending'`**,
  `subtotalAmount?`, `taxLabel?`, `taxType?: 'none'|'percent'|'fixed'`,
  `taxValue?`, `taxAmount?`, `discountType?`, `discountValue?`,
  `discountAmount?`, `documentProfile?` — frozen settings snapshot at issue time).
- **Operator** (`id`, `name`, `role` string — free text, no fixed role enum).
- **CustomExamination** (`id`, `name`, `unit` — just name + unit).
- **ICDCode** (`code`, `title`, `type: 'icd11'|'icf'`, `linearizationUri?`,
  `foundationUri?`).
- **AppointmentAttachment** (`id`, `name`, `mimeType`, `size`, `data?` full
  image device-local, `thumbnail?` travels with record, `storagePath?`,
  `uploadStatus?: 'pending'|'uploaded'`, `description?`, `created_at`).
- **ReceiptConfig** (`header`, `footer`, `paper?: 'a4'|'dotmatrix'`).

Key identity/numbering schemes:
- Patient record numbers: auto-generated (`SimpleDataService.generateRecordNumber('patient')`).
- Invoice numbers: `INV-YYYYMM-####` — sequential within the month, deduped by
  scanning existing numbers (see §6.1); timestamp fallback on failure.
- Appointments/invoices keyed by numeric `id` (+ `sync_id` for cloud sync,
  irrelevant offline).

---

## 3. Theming & i18n

### 3.1 Theming

- Joy UI `CssVarsProvider`; theme presets in
  `src/contexts/ThemeSettingsContextCore.tsx`: **`light`, `dark`, `blue`,
  `yellow`, `green`** (`THEME_PRESETS`). Toggle in title bar switches
  light ↔ dark (or to opposite brightness if on a color theme).
- Appearance settings (`src/pages/settings/AppearanceSettings.tsx`) expose:
  theme preset picker, language picker (see below), and more (child analyst
  details in §9). Radius is uniformly 8px (medrec) per alignment notes.
- WinUI 3 mapping: WinUI has native light/dark via `ApplicationTheme`; the
  three color presets (blue/yellow/green accents) can map to WinUI accent-color
  theming. Honor Windows system theme by default.

### 3.2 i18n scope

- `src/i18n/index.ts`: **7 languages** — en, id (Indonesian), ja, fr, de, es,
  it — × **12 namespaces**: common, dashboard, settings, security, sync,
  patients, appointments, invoices, reports, encryption, ai, onboarding.
- Language persisted in localStorage (`medrecplus:global:language`), switchable
  in Appearance settings; fallback `en`. All UI strings go through
  `react-i18next` (no hardcoded user-facing strings in the main flows).
- **Port note**: the WinUI rebuild should keep a resource-dictionary i18n
  design from day one; Indonesian (`id`) is the boss's working language.
  Some dates are hardcoded to `en-GB`/`en-US` formats in places (rebuild should
  centralize date formatting on locale).

---

## 4. Patients workflow

### 4.1 Patient List — `/patients` (`src/pages/PatientList.tsx`)

**Purpose:** searchable, sortable patient registry; entry point to everything
patient-related.

**Sections & controls:**
- Search box (debounced 300ms, server-side search over name/record/phone via
  `SimpleDataService.getPatients`).
- View toggle: table ⇄ card (global `viewMode`).
- Column-visibility dropdown (checkbox menu), persisted
  (`medrecplus:patients:columnVisibility`): Record Number (hidden by default),
  Name, Age, Address (shown), Phone Number (hidden), Initial Diagnosis
  (hidden), Date Added (shown).
- Table view: sortable column headers (click toggles asc/desc — **bug**:
  `handleSort` calls `setSortDirection` twice, second call forces `'asc'`,
  breaking descending), row checkboxes, per-row View / Edit / Delete; sorting
  applies to the loaded page only (misleading on multi-page data).
- Card view: clickable cards (name, age, record-number chip, phone/address/
  diagnosis rows per visibility, date added) with View/Edit/Delete icon buttons.
- Entries-per-page 10/25/50 (persisted `patients:itemsPerPage`); first/prev/
  next/last pagination + "Showing X–Y of Z" (hidden on single page).
- FAB: "Add Patient" → `/patients/add`.

**Dialogs:** single/bulk delete → `ConfirmDialog` (danger); afterwards an
`AlertDialog`; bulk delete shows a full-screen "Deleting" spinner modal.
Deletion here is permanent via `SimpleDataService.deletePatient(s)` — **no
soft-delete/restore UI** on this screen (though `softDeleteUtils.ts` helpers
exist elsewhere in the codebase).

**Empty/loading:** shimmer skeletons on first load; card view has a dashed
empty state with a "create one" link.

### 4.2 Add Patient — `/patients/add` (`src/pages/AddPatient.tsx`)

**Purpose:** register a new patient; the operator must explicitly choose a
GDPR lawful basis before saving.

**Form fields** (single card, max-width 640px, sticky header):
| Field | Type | Required | Notes |
|---|---|---|---|
| Record Number | text, disabled | auto | Auto-generated, "Auto-generated" chip |
| Patient Name | text | **yes** | trimmed; error if empty |
| Gender | select | **yes** | Mr./Ms./Mx., default `'Mr.'` |
| Age | number | **yes** | min 0/max 150; UI rejects 0 (schema mismatch — see below) |
| Phone Number | text | no | no format validation at UI level |
| Address | text | no | empty → undefined |
| ICD Codes | `ICDSearchInput` | no | multi-select chips |
| Initial Diagnosis | textarea | no | minRows 3 |
| Lawful Basis | select | **yes, no default** | `CONSENT_BASIS_OPTIONS`; blocks save until chosen (CON-01) |

- On save: `consent_date` = now, `consent_recorded_by` = current auth user id.
- Errors shown as a top danger Alert. Back arrow + Cancel → `/patients`;
  Save → navigates to the new `/patients/:id`.
- **Validation-layer mismatch**: `ValidationService.PatientSchema` (zod)
  enforces a phone regex and requires UUID `id`, `email`, `medical_history`,
  `allergies` — none of which match this form; the schema looks vestigial and
  is not applied to this flow. The port should define ONE source of truth.

### 4.3 Edit Patient — `/patients/:id/edit` (`src/pages/EditPatient.tsx`)

Same shape as Add with these differences: record number disabled
("Cannot modify" chip); **no gender field** (gender becomes immutable after
creation, unexplained in UI); lawful-basis select pre-filled — leaving it
empty leaves the existing basis untouched, never wiped. Loads via
`getPatientById`; missing patient → error view with back button.

### 4.4 Patient Details — `/patients/:id` (`src/pages/PatientDetails.tsx`)

**Purpose:** 360° patient view + launchpad for visits.

**Sections** (vertically stacked cards, not tabs):
1. **General info card**: consent warning banner with "Record basis" button
   when `consent_basis` is missing; name (h2), record-number chip, Edit
   button → `/patients/:id/edit`; age, phone, address, initial diagnosis,
   ICD-11 chips (click → opens `https://icd.who.int/browse/2024-01/mms/en#<code>`
   in a new tab); created-on date. Each view logs an `AUD-01` audit event
   (`patient_viewed`, deduped per record per minute).
2. **Attachments card**: image dropzone — click to browse (jpeg/png/webp),
   drag-and-drop, or **paste from clipboard** (page-level listeners + dimming
   overlay while dragging). Client-side validation + compression to WebP
   ≤1600px; thumbnail generated. Full images stay device-local; thumbnail
   travels with the record; background upload when online with a "pending
   upload" chip otherwise. Rows: 56px thumbnail (click → zoomable preview
   modal, 50–300%), name, size, notes; per-attachment Notes modal and delete
   (confirm).
3. **Appointment history card** (collapsible, collapsed by default): header
   with count; summary chips (last visit, total billed). Mobile: the
   `AppointmentTimeline` component (month-grouped, filters by search/month/
   operator/invoice status, 12-per-page "show more", per-visit vitals with
   **high-BP alert** if systolic ≥140 or diastolic ≥90, invoice status chips).
   Desktop: table (date/time, operator, vitals, treatment count, total chip,
   view icon) with MUI pagination at >10 rows. Empty: dashed "no appointments".
4. **Consent / lawful-basis card**: shows basis label + "recorded on \<date\> ·
   recorded by \<name\>" (name resolved via `useUserDisplayName`, raw id
   fallback), or a warning if never recorded; "Change basis" modal (select +
   Save).

**Header actions:** back arrow → `/patients`; Export (Download icon →
`exportPatientData` → dialog showing saved JSON/PDF paths — GDPR Art. 20);
Erase (DeleteForever, **owner-only**, hidden for non-owners without
explanation) — opens the erasure modal: describes scope (patient +
appointments + invoices + attachments incl. files + AI logs), shows counts
(X appointments · Y invoices · Z stored files · N AI log entries), notes the
backup-file caveat, and requires **typing the patient's full name exactly**
to enable "Erase permanently"; then navigates to `/patients`.

**FAB:** "Create Appointment" → stores `{id, name}` in localStorage
`selectedPatientForAppointment` → `/appointments/new` (pre-selects patient).

**Patient workflow end-to-end:** search (debounced, column picker, 10/25/50
pagination, table/card) → details (demographics, ICD chips, attachments with
drag/drop/paste + zoom preview, collapsible history with billing summary,
consent card) → "Create Appointment" FAB → `/appointments/new`; or Edit
(`/patients/:id/edit`); or Delete (list only, confirm dialog); or Export
(Art. 20) / Erase (Art. 17, typed-name confirmation).

### 4.5 ICDSearchInput (`src/components/ICDSearchInput.tsx`)

ICD-11 diagnosis search + ICF functioning codes; used in Add/Edit Patient and
the appointment flow. Type keywords ("diabetes") or ICF terms ("walking,
mobility"); dropdown of matches; select → chips showing code + title;
removable chips. Search via `ICDService.search(query, linearization)`:
- **Tauri desktop**: bundled **local ICD database** — works offline, always
  enabled ("ICD database missing" warning if the data isn't installed).
- **Web**: WHO ICD API over the internet; disabled + warning when offline or
  when API credentials aren't configured.
- Debounced 400ms, min 2 chars, excludes already-selected codes, relevance %
  per match, HTML stripped from WHO titles, portaled dropdown.

**Port note (offline WinUI):** the ICD search must bundle a local ICD-11
database (the app already does this for desktop) — there is no live WHO API
option offline. ICF codes are also searched locally.

---

## 5. Appointments workflow ("appointments" = visit records)

> Key domain insight: an "appointment" here is **not a scheduled future
> event** — it is a **same-day visit record** created at the time of the
> visit. There is no date/time picker, no calendar view, no check-in, no
> reminders, no duration, no room. The port may either keep this model or add
> real scheduling (new fields required).

### 5.1 Appointments list — `/appointments` (`src/pages/Appointments.tsx`)

**Sections & controls:**
- Debounced search (300ms) over patient name/record/phone.
- Shared `DateRangeDropdown` (today / last 7 / last 30 / custom range);
  **default range = today** — users land on today's visits.
- Column-visibility dropdown (persisted): date (on), patient name (on),
  vital signs (off), treatments (off), total price (on), operator (on).
- Table view: sortable headers (client-side, loaded page only), row
  checkboxes, per-row view (→ details) and delete; patient name deep-links to
  `/patients/:id`. Card view variant.
- Pagination (10/25/50 persisted), first/prev/next/last.
- FAB: "New Appointment" → `/appointments/new`.

**Statuses: none.** The `Appointment` type has no `status` field. What the UI
shows as "status" is the **linked invoice's status** (`paid|unpaid|pending|
void`, or a "no invoice" chip) — see the Timeline below.

**Dialogs:** single/bulk delete → `ConfirmDialog`; afterwards `AlertDialog`.
Deletes are soft-deletes.

**Dead-ends:** leftover `_loading/_hasMore/initialLoad` suggest an unfinished
infinite-scroll path (pagination UI loads whole pages; the append branch is
dead). A commented-out `viewModeStorageKey` is dead code.

### 5.2 New Appointment — `/appointments/new` (`src/pages/NewAppointment.tsx`)

Single-page "create visit" form. **No date/time picker** — `date` is stamped
`new Date().toISOString()` at save. No chief-complaint/reason field (the type
has `notes`/`description` but no input binds them here).

| Section | Fields | Required |
|---|---|---|
| Patient picker | custom search input + portaled dropdown (name, record no., age, phone); record-number deep search (`PT\d+…`, ≥8 chars bypasses pagination); clear ×; pre-selected via localStorage `selectedPatientForAppointment`; empty-registry warning with "Add patient" → `/patients/add` | **yes** |
| Diagnosis codes | `ICDSearchInput` multi-select chips (`{code,title,type:'icd11'\|'icf'}`) | no |
| Operator picker | dropdown (`getOperators`); empty warning with inline "Create one" → `OperatorModal` (name + role, both required), auto-selects new operator | **yes** |
| Vital signs | BP free text ("120/80"), respiration rate (number), heart rate (number), Borg scale 0–10 (number, with helper caption) | no (defaults `""`/0/0/0; **no range validation**) |
| Custom examinations | one free-text input per admin template, labeled `Name (unit)`; stored as `vitalSigns['custom_<id>']` = `{name, unit, value}` snapshot | no |
| Treatments | "Select treatments" modal (search + checkbox list w/ price); inline "Create one" → `TreatmentModal` (name+description required, price > 0), auto-selected; removable chips + live running total | no |
| Discount | hidden behind "+ Add discount": type `none\|percent\|fixed` + value; applied **before tax**; subtotal → discount → payable shown | no |

**Submit:** "Create appointment" (no draft concept, no confirmation) → inline
validation (patient required, operator required; danger Alert at top) →
`saveAppointment` → navigates to `/appointments/:id`.

### 5.3 Appointment Details — `/appointments/:id` (`src/pages/AppointmentDetails.tsx`)

**Purpose:** read-and-enrich view of one visit; treatments/vitals are
snapshotted at creation and **cannot be edited afterwards** — post-creation
changes are limited to treatment notes and attachments (a deliberate-or-gap
decision for the port).

**Sections** (vertically stacked cards, ~600px max width):
1. **Sticky header**: back; title; primary action — **"Generate invoice"**
   (Receipt icon) → becomes **"View invoice"** once an invoice exists
   (`data-onboarding="invoice-action-btn"`).
2. **Info card**: patient, operator, date ("Friday, 7 November 2025 23:21");
   "View patient" → `/patients/:id`; "Delete appointment" (danger; hidden in
   mobile header, full-width below on `xs`).
3. **Vital signs card**: BP (or "Not recorded"), respiration (breaths/min),
   heart rate (bpm), Borg chip (green ≤3 light / yellow ≤6 moderate / red >6
   high / neutral not recorded).
4. **Additional examinations** (only if custom exams recorded): name/value/
   unit boxes, extracted from `vitalSigns.custom_*` snapshots.
5. **Diagnosis codes** (only if present): chips `{code} {title}` — primary for
   ICD-11, green for ICF; **click → opens the WHO ICD browser**
   (`https://icd.who.int/browse/2024-01/mms/en#<code>`) in a new tab.
6. **"Reviewed clinical source"** (only when invoice number starts with
   `MCP-`): `OriginalDictation` audio/transcript + treatment provenance lines.
7. **Treatments card**: name, provenance lines, optional notes, price, per-
   treatment **Notes** button (modal → `updateAppointment`).
8. **Attachments card**: click/drag-drop/**paste** image dropzone (jpeg/png/
   webp), client-compressed WebP ≤1600px, full image device-local, thumbnail
   in record, background upload when online ("pending upload" chip); per
   image: thumbnail preview, name, size, notes, Notes button, Delete
   (confirm); full preview modal with zoom 50–300%.
9. **Total price card**: subtotal, discount line (if any), payable total.
10. **Meta**: "Created on {date}".

**Generate-invoice flow** (lines 638–750): checks for an existing invoice for
the appointment (if found → navigates to it); loads invoice settings profile
(blocks with an error if the settings context exists but settings are missing
— "Save the clinic invoice settings before issuing an invoice"); generates
number `INV-YYYYMM-####` from the max existing sequence (timestamp fallback);
snapshots treatments/vitals/discount into the invoice (immutable),
applies tax per clinic settings, freezes a **document profile**
(`createDocumentProfile` — settings version + hash + patient snapshot);
status `unpaid`; retry up to 3× on 409 duplicate-number; success →
`/invoices/:id` with toast "Invoice created successfully". Deleting the
appointment also soft-deletes its invoices (explicit red warning in the
delete dialog when an invoice exists).

### 5.4 AppointmentTimeline (`src/components/AppointmentTimeline.tsx`)

**Not used by the appointment screens** — it is the **patient profile's visit
history**. Month-grouped sticky headers; filters: text search, month,
operator, invoice status (incl. "no invoice"); 12-per-page "show more"; per-
visit vitals line with **high-BP alert** (systolic ≥140 or diastolic ≥90 in
warning color); treatment count; total; invoice status chip (paid green,
unpaid red, pending yellow, void neutral).

### 5.5 Custom examinations admin — `/settings/custom-examinations`

Admin CRUD for examination field templates. Each template is just
**name + unit** (`CustomExamination {id, name, unit}`) — no field types, no
ranges, no required flags, no defaults. Table/card list (follows global
`viewMode`), add/edit modal (both required, duplicate-name check on add),
delete via `ConfirmDialog`, Science-icon empty state, 3s auto-dismiss success
alerts. Templates render as free-text inputs in NewAppointment; values are
**snapshotted** into the appointment (`custom_<id>: {name, unit, value}`), so
renaming later doesn't rewrite history; they also flow onto invoices and the
appointments list's optional vitals column (built-ins only there).

**Appointment workflow end-to-end:** `/appointments/new` (patient → ICD →
operator → vitals/custom exams → treatments → discount → create) →
`/appointments/:id` (enrich: treatment notes, attach images) →
"Generate invoice" → `/invoices/:id` (money lifecycle). No
scheduling/check-in/complete steps exist; deletion is soft-delete with
confirm.

---

## 6. Dashboard (`src/pages/Dashboard.tsx`)

**Layout:** 2-column grid (1 column on mobile) of 5 cards + fixed "New
Appointment" FAB (repositions above mobile bottom nav). Header: welcome +
overview. Data loaded once on mount (`getPatients`, `getAppointments`,
`getInvoices`, each `limit: 5000`); **all stats computed client-side**; no
manual refresh, no auto-refresh.

**Metric cards** (each with its own inline time-range dropdown — 7 days / 30
days / 3 months, persisted per card in localStorage):
1. **New Patients** — CountUp animated count + area chart (patients/day,
   primary blue); **Add** button → `/patients/add`.
2. **Appointments** — CountUp count + area chart (success green).
3. **Revenue** — CountUp total of **paid invoices only** (unpaid/void
   excluded) + area chart (warning amber); currency-formatted, K/M/B
   shortening above 1000.
4. **Patient Quick Access** — top-10 patients by appointment count in period;
   avatar-initial, name, age • address, visit-count chip. Click → stores
   `{id}` in `selectedPatientForAppointment` → `/appointments/new`.
5. **Recent Invoices** — 10 most recent (any status); patient initial, name,
   invoice number, total, status chip. Click → `/invoices/{id}`.
   (No period filter here by design; no "view all" link.)

**Charts:** `LazyLineChart` wraps recharts in Suspense; Dashboard uses
`variant="area"` (gradient-filled monotone). X labels: day labels ("Mon 24")
for 7/30-day ranges, month abbreviations for 3 months. **Metric cards/charts
are not clickable** — no drill-down from the numbers.

**Onboarding coupling:** on mount, if the `dashboard` tour isn't complete →
`startTour('dashboard')`; otherwise a new user is **force-routed to
`/settings/operators`** first (dashboard-final tour). The offline port should
replicate: first run → operator setup before the dashboard is useful.

**Dead-ends:** `DateRangeDropdown` (the polished shared custom-range picker)
is **not used** by Dashboard or Reports (both use simpler per-card
dropdowns); `totalAllTime` computed but never displayed; dashboard errors only
`log.error` to console (no visible error UI); shimmer skeletons on load.

---

## 7. Reports (`src/pages/Reports.tsx`, exported as `ReportsOptimized`)

Two independent sections; **no PDF, no CSV, no print** anywhere.

### 7.1 Operator Performance Report

- **Filters (always visible):** Month dropdown (Jan–Dec + "All"), **Year
  dropdown (current year back 5; blank = no report generated until selected)**,
  multi-select Operator dropdown, **"Export to XLS"** (disabled until a year is
  chosen).
- **Output:** 3 summary cards (Report Period, Total Appointments, Total
  Revenue), then one row per operator sorted by revenue desc: name + ID,
  appointment count, paid-invoice count, revenue chip. Per row: **"View"**
  (invoices modal) and **"XLS"** (that operator's export).
- **Semantics:** revenue/invoice counts = **paid invoices only**; filtering
  in-memory after fetching all appointments/invoices; results cached by filter
  key in a `useRef` Map.
- **Invoices modal:** Invoice Number / Date (DD/MM/YYYY) / Patient / Amount /
  Status + footer totals; rows are **not** linked to invoice detail.
- **Export:** client-side **ExcelJS** XLSX: whole-period (`handleExportPeriodToXLS`)
  or per-operator (`handleExportToXLS`); filename
  `Invoices_<period>_<operators>.xlsx`; columns No / Invoice Number / Date /
  Patient Name / Operator / Amount / Status. Save path: Tauri desktop → native
  save dialog; Android → app storage with "Open file" action; web → Blob
  download.
- No today's-appointments, no aging, no treatment/diagnosis breakdown.

### 7.2 Patient Analysis (vital-signs trends)

- Debounced (300ms) patient search (name or record number, capped at 50);
  dashed placeholder until a patient is chosen.
- Selected patient: name, record number, appointment count, Clear button;
  then up to **4 recharts line charts** — Blood Pressure (sys/dia two-line),
  Heart Rate, Respiration Rate, Borg Scale — X-axis = visit date (DD/MM/YYYY).
  Only rendered when data exists. Appointments filtered by the same
  year/month filters.

**Offline note:** Dashboard + both report sections are fully offline-capable
(local data → local computation → local charts; ExcelJS export needs no
network — in WinUI, export via a native XLSX library + `FileSavePicker`).

---

## 8. AI Insights (`src/pages/AIInsights.tsx`)

**Structure:** chat-first page. Header (Psychology icon + title + settings
gear → `/settings/ai`). Empty state: "Ask Anything About Your Data" + mode
chips ("Local Only (Deterministic)" when `providerMode === 'none'`; "PHI
Redacted" when redaction on) + capability chips (Revenue Analysis, Patient
Insights, Operator Performance, Data Quality). Messages: user bubbles right,
AI answers as full-width `AIAnswerCard`. Input: sticky sheet with suggested-
prompt chips (from `ai.json` `suggestedQuestions`, e.g. "How many patients
do we have?", "Show me unpaid invoices older than 14 days", "Who is our top
operator?"; chips disappear after being clicked) above a textarea
(Enter-to-send, Shift+Enter newline) + Send.

**How it works:** `AIInsightsService.answerQuestion()` — default
`providerMode: 'none'` gives **deterministic answers computed locally** from
`SimpleDataService.getAIContextSnapshot()`: intent matching against `ai.json`
`intentKeywords` (intents: `kpi_overview`, `patient_timeline`,
`revenue_analysis`, `invoice_followup`, `operator_performance`,
`treatment_trends`, `data_quality`, `unknown`) + summary builders
(`buildRevenueSummary`, `buildOperationalSummary`, `buildDataQualitySummary`,
`getUnpaidInvoiceFollowups`, `buildPatientTimeline`). Answers include numeric
**KPI cards** (auto-currency-format metrics named revenue/amount/paid/billed/
unpaid) and a collapsible **"Data Sources & Evidence (N)"** table (Source /
Record ID / Summary / Count) — the auditability layer. Queries are logged
(`storage.appendAILog`) with settings: PHI redaction (default true),
`syncBeforeAnswering` (default false), `maxEvidenceRows` 10,
`maxResponseLength` 2000, `logRetentionDays` 30.

**Critical gaps:**
- Provider modes `'local'` and `'cloud'` are both labeled **"Coming Soon"**
  in `en/ai.json` and non-functional — only the deterministic mode works.
- **`AIPrivacyNotice` exists but AIInsights.tsx does not render it**; the
  empty state duplicates some of its info via chips.
- **No non-clinical disclaimer anywhere**: no "not medical advice" /
  "analytics only" text in the page, answer cards, or `ai` locale strings.
  The compliance notes say "keep AI Insights non-clinical", but the UI doesn't
  state it. **The port must add an explicit analytics-only/non-clinical
  disclaimer.**

**Offline note:** the deterministic engine is already offline by design —
port the intent keywords + summary builders + snapshot, not just the UI. The
unimplemented cloud mode inherently needs a network LLM backend.

---

## 9. Settings hub (`src/pages/Settings.tsx`)

Card/list hub grouped under 8 `<h2>` section labels; each card = icon tile,
title, one-line description, chevron, keyboard-activatable. The Activity Logs
card carries a live **"N new" unread badge** refreshed every 5s. Hub also
links `/reports` and `/ai-insights` under "Insights" (app pages, mildly
misplaced), and has a "Restart onboarding" action (`resetAllTours()` → `/`).

| Group | Section | Route | What it configures |
|---|---|---|---|
| Clinic | Appearance | `/settings/appearance` | 5 theme presets (Dark/Light/Blue/Yellow/Green), global table/card view mode, **fullscreen** (Tauri desktop only, disabled elsewhere), **UI scale slider 75–175%**, language (7 buttons) |
| Clinic | Operators | `/settings/operators` | operator roster CRUD (name + free-text role label only), debounced search, 10/page pagination; auto-starts `operator-settings` tour then chains to treatments |
| Clinic | Treatments | `/settings/treatments` | treatment catalog CRUD (name/description/price), soft-delete; chains tour → `/` (`dashboard-final`) |
| Clinic | Custom examinations | `/settings/custom-examinations` | exam templates (name + unit only) |
| Clinic | Currency | `/settings/currency` | ~180-currency searchable picker w/ per-currency sample formatting, tax label/type/value + live preview on a 100,000 sample subtotal; editor model w/ draft/conflict UI (cloud) |
| Billing | Receipt | `/settings/receipt` | full invoice-document designer (§10.7); shared with AI/MCP drafts ("Normal invoices and AI drafts use the same clinic settings") |
| Data & Sync | Backup | `/settings/backup` | **manual** JSON backup (version 2.0: operators, treatments, patients, appointments, invoices, customExaminations, settings; **passwords/credentials explicitly excluded**); `EnhancedRestoreDialog` from file; patients/invoices **Excel export**; **Erase all data** (3-stage gauntlet — typed phrases `DELETE ALL WITH CLOUD` / `DELETE ALL KEEP CLOUD` / `DELETE ALL DATA` — requires online workspace validation first). **No scheduled/automatic backup.** Attachment migration to Supabase Storage (cloud-only). |
| Data & Sync | Offline mode | `/settings/offline` | Online/Offline switch (guarded: needs downloaded local mirror to go offline; needs internet to go online; "Sync & Switch" vs "Skip & Switch" dialog), Sync to Cloud (push/pull/conflict counts), Download All Data, local storage inventory (per-entity counts), Clear offline data |
| Data & Sync | Accounts & workspaces | `/settings/cloud-sync` | Google sign-in profile, workspace card (name, OWNER role chip, **TRIAL/✓ PAID plan chip**), invite code (owner: copy/rotate), **team members with Write/Delete permission checkboxes** (the real RBAC: `role: owner\|member` + `canWrite` + `canDelete`), create/join workspace cards, **$20 lifetime upgrade via PayPal** (`createPayPalOrder` → approval URL → capture), trial limited to 10 records/category |
| Security & Access | AI connections (MCP) | `/settings/mcp` | **offline-disabled**; clinical-draft approval queue (field-by-field, "I verified the patient…" checkbox + approve/reject; expired drafts blocked); **connection tokens** (owner: name, scope checkboxes — `analytics:read`, `clinical:read`, `clinical:write`, `documents:read` with dependency auto-adds — expiry default 90d, **plaintext shown once**, Active/Revoked list, revoke) |
| Security & Access | Password & security | `/settings/security` | security overview chips (auto-lock, timeout, lockout, encryption status); change/setup/re-encrypt/**reset** password modals (strength checklist: ≥8, upper, number, symbol; debounced current-password check); auto-lock toggle + timeout (5/15/30/60/120/180 min or **Never**, default 15) + progress bar; failed-attempt lockout toggle; **"Lock app now"**; **TOTP MFA via Supabase** (QR + secret + 6-digit confirm; cloud-only); password-age/staleness warning banners (90-day) |
| Privacy | Privacy notice | `/settings/privacy-notice` | read-only versioned notice **v0.1.0-draft** with **[PLACEHOLDER]** items (lawyer review pending) + permanent DRAFT banner; owner acknowledgment card stored **per-device localStorage** |
| Privacy | Data retention | `/settings/retention` | owner-only per-category (patients, appointments, invoices, attachments, audit_events) retention in days; legal minima **PLACEHOLDERS pending lawyer**; draft values advisory until "lawyer sign-off" checkbox → confirmed minima become blocking; "Run purge now" (hard-erases old soft-deleted records + audit events) |
| Audit | Activity logs | `/settings/logs` | local IndexedDB CRUD feed: operator, action chips (Created/Updated/Deleted/Action), **deep links** to patient/appointment/invoice for non-destructive actions, DD/MM/YYYY HH:mm; **entries >7 days auto-purged on load**; "mark as read", "Clear all" (confirm) |
| Audit | Security audit | `/settings/security-audit` | owner-only read-only GDPR accountability trail (**IDs only, never PHI**); last **500 events on device**; filters (action type, actor user-id substring, date range); **BR-01 incident banner** (lockouts, access-denied spikes, export-volume spikes) with "Mark reviewed" — incidents exempt from retention purge |

**Invoice settings editor** (`useInvoiceSettingsEditor` + `InvoiceSettingsDraftStatus`):
document language (en/id), date format, timezone, decimal places — shared with
the Currency and Receipt screens. Draft status banner: "Unsaved changes" /
"Saved on this device. Waiting to sync." / "Saved on this device." /
"Settings saved." + multi-device conflict resolver (field-by-field diff,
"Keep my changes" / "Use clinic settings"). Owner-only for shared settings.
**Port note:** keep the document-settings content; drop the draft/sync/
conflict machinery for a single local settings store.

**Notable dead-ends in settings:**
- `/settings/ai` (AISettings) is **orphaned** — routed but not in the hub
  menu. Provider select has only "None" selectable (Local/Cloud permanently
  disabled); `syncBeforeAnswering` type field not rendered. Stub only —
  **do not rebuild as-is**; fold the working toggles (enabled, PHI redaction,
  evidence rows, response length, log retention) into one AI section.
- **Tauri security-settings persistence is a TODO** (`loadSettings`/
  `saveSettings` are no-ops on Tauri); Re-encrypt mode disabled ("coming
  soon"); Tauri migrate disabled; Tauri reset-password no-op; encryption
  status check web-only.
- Retention legal minima + privacy notice text are PLACEHOLDERS (not legal
  advice); both English-only (`TODO(i18n)`); retention menu label hardcoded
  English.
- Erase-all-data requires online validation before dialogs open.

**Port mapping:**
- **Keep local, directly portable:** Appearance, Operators, Treatments,
  Custom examinations, Currency + tax, Receipt designer, manual JSON/Excel
  backup + restore, Activity logs (7-day purge adjustable), Security audit
  trail + incident banner, password & security minus MFA (device password,
  auto-lock, 5-failed-attempts→30-min lockout, emergency wipe minus cloud
  sign-out), lock screen (password-only, inactivity triggers), invoice-
  settings editor in local mode.
- **Cloud-only, drop:** Accounts & workspaces (Google sign-in, invites,
  team permissions, PayPal upgrade, trial limits), AuthLogin, WorkspaceSetup,
  MCP settings, TOTP MFA, attachment migration, offline-mode sync/download,
  erase-all-data's cloud-wipe leg, retention's workspace scoping,
  invoice-settings shared-profile publishing + conflict resolution, privacy-
  notice ack syncing.

---

## 10. Invoices workflow

### 10.1 Creation — exactly one path: from an appointment

There is **no** `NewInvoice` page, no `/invoices/new` route, no standalone
create form. The only `saveInvoice` call in UI code is
`src/pages/AppointmentDetails.tsx:712`.

- **Entry**: "Generate Invoice" solid primary button in the appointment
  header (`AppointmentDetails.tsx:1038-1056`); once an invoice exists it
  becomes "View Invoice". No status gating — shows on any appointment.
- **`generateInvoice()` (lines 638-753)** — fully automatic, no creation
  form:
  - **Dedup guard**: checks for an existing invoice with this `appointmentId`
    → navigates to it (one invoice per appointment).
  - **Precondition**: clinic invoice-settings profile must exist
    (`InvoiceSettingsService.getCurrentProfile()`); otherwise an error —
    **invoices cannot be issued before the clinic's invoice settings are
    saved**.
  - Patient derived from the appointment (no selection).
  - **Line items**: `appointment.treatments` snapshotted verbatim. **No
    quantities** (each treatment qty 1; row = `{id, name, price, notes?}`);
    catalog treatments only — **no manual free-text line items**.
  - **Discount**: snapshot from the appointment (`none|percent|fixed`,
    value), applied **before tax**. Discount is set on the *appointment*
    form ("Add Discount" → type select + value, percent capped 0–100), not
    on the invoice.
  - **Tax**: snapshot from invoice-settings currency config
    (`taxLabel` default "VAT", `taxType none|percent|fixed`, `taxValue`,
    computed `taxAmount`) — frozen at creation (set in Currency settings
    with a live preview on a 100,000 sample subtotal).
  - Totals: `computeInvoiceTotals(subtotal, discountType, discountValue,
    currencyConfig)` → `{subtotalAmount, discountAmount, taxAmount, total}`.
  - Status: always created `'unpaid'`. Issue date: creation time. **No due
    date field** — the `Invoice` type has no `dueDate`; no payment terms.
  - **Numbering**: `INV-YYYYMM-####` — zero-padded 4, resets monthly (e.g.
    `INV-202610-0001`); computed by scanning existing numbers with the
    current prefix (server queried, limit 1000); 409
    `DUPLICATE_INVOICE_NUMBER` → retry up to 3×; fallback
    `INV-YYYYMM-<last4 of Date.now()>`. MCP-issued invoices use an `MCP-`
    prefix (server-side only, no create UI).
  - **Document freeze**: `InvoiceDocumentService.captureInvoiceDocument`
    snapshots the approved receipt settings + patient data into
    `documentSnapshot` ("issued_frozen_settings" / "local_issued_settings")
    so the printed/PDF document reflects issue-time settings.
- After save → `/invoices/<id>`; on error → inline
  `errors.generateInvoiceFailed`.

### 10.2 Invoice list — `/invoices` (`src/pages/Invoices.tsx`)

- **Columns** (`LazyInvoiceTable`): invoiceNumber, patientName, operatorName,
  appointmentDate, totalAmount, status (on by default); `date` (issue date,
  off by default). Resizable headers; toggleable via column-visibility
  dropdown (persisted `medrecplus:invoices:columnVisibility`).
- **Filters: no status filter exists.** Debounced search (300ms); shared
  `DateRangeDropdown` **defaulting to today only** (start-of-day →
  end-of-day) — a fresh user sees an empty list until they widen the range.
- **Sorting**: client-side, header click asc/desc on any `keyof Invoice`;
  server query always `sortBy: 'date', sortOrder: 'desc'`.
- **Pagination**: server-side 10/25/50 (persisted `invoices:itemsPerPage`),
  first/prev/next/last + "Showing X–Y of Z".
- Table ⇄ card view (global view mode). Card: patient, status chip, invoice
  #, dates, total, View/Delete buttons.
- **Row actions**: View → `/invoices/:id`; Delete (confirm). Patient name →
  `/patients/:id`; appointment date → `/appointments/:id`.
- **Bulk**: row checkboxes → single bulk Delete; confirm with count; then
  success/warning/error alerts.
- **Empty state (card)**: `list.noInvoices` + `list.makeOne` →
  `/appointments/new` — reinforces that invoices come only from appointments.

### 10.3 Invoice details — `/invoices/:id` (`src/pages/InvoiceDetails.tsx`)

~600px max width. Back + title + **Delete / Download-PDF / Print** actions;
**Status control card** (a plain `<Select>`: unpaid/paid/void — **no
confirmation**; `updateInvoiceStatus` errors silently swallowed); the
WYSIWYG receipt document (`InvoicePdfPreview` on gray paper); footer with
created/updated timestamps + "View Appointment".

- **Mark paid**: just the status select → `updateInvoice`. **No "record
  payment" dialog, no payment methods** (cash/card/transfer — `grep -il
  "payment method|paymentMethod"` finds nothing), no payment history, no
  partial payments. Status is the only money tracking.
- **Void**: same select (`void`).
- **Print**: `handlePrint()` renders the PDF pages at 300 DPI into HTML
  (`@page{size: <paper>; margin:0}`) and prints via `printInvoiceWithDataURL`
  (Tauri/Android native print); audits `patient_printed`; back to `/invoices`
  after 1s.
- **PDF export**: `downloadInvoicePdf()` — Tauri: native save dialog; Android:
  shared Downloads + open/share; browser: blob download; audits
  `patient_exported`.
- **Delete**: animated confirm modal → `deleteInvoice` + toast + back to
  list. (Deleting an **appointment** also soft-deletes its invoices, with an
  explicit warning in the appointment delete dialog.)
- **No send flow**: no email/SMS/WhatsApp despite Download/Print existing.

### 10.4 Invoice statuses

Type: `'paid' | 'unpaid' | 'void' | 'pending'` (`src/types/index.ts:182`).
- Created as `'unpaid'`. The details select offers only `unpaid|paid|void`.
- **`pending` is never assigned anywhere** in UI code — dead enum value
  (only appears as the chip fallback label). There is **no invoice draft
  status** either (despite the filename, `InvoiceSettingsDraftStatus.tsx` is
  about the invoice-*settings document* draft/sync state, not invoices).
- Transitions are free-form: unpaid↔paid↔void in any direction, no guards,
  no confirmation, no UI-side audit requirement. Void can be silently
  un-voided.
- Chip colors: paid=blue(primary), unpaid=red(danger), void=gray(neutral).

### 10.5 Document: InvoiceDocument & InvoicePdfPreview

**`InvoiceDocument.tsx`** — "the single source of truth for invoice layout".
Plain HTML + inline styles (no UI kit) so serialization renders identically
in the print iframe / Android WebView. Sections, all individually toggleable:
1. Header — multi-line clinic text from `receiptConfig.header` (first line
   bold/large)
2. Title — default "TREATMENT RECEIPT" (language-aware)
3. Meta row — *Billed To* (patient name, address, Record No) | *Invoice Info*
   (#number, date, STATUS)
4. Treatments — name + price rows; per-treatment provenance lines
   (`describeTreatmentProvenance`); optional italic treatment notes
5. Totals — SUBTOTAL, discount line, tax line, bold TOTAL (breakdown rows
   only when tax/discount apply and `sections.taxBreakdown` is on)
6. Operator info — dashed box (operator name, created date)
7. Footer — multi-line clinic text; optional dividers between sections

Narrow mode: paper < 100mm stacks meta columns vertically. All geometry from
`resolveReceiptLayout(receiptConfig)`.

**`InvoicePdfPreview.tsx`** — renders the *actual PDF bytes* (jsPDF,
`server/src/mcp/invoicePdf.ts`) into page images (pdf.js,
`invoicePdfPages.ts`) WYSIWYG; the same bytes feed Print and Download. Retry
on render failure; font-substitution notes (system-sans→Helvetica etc.).
**Legacy invoices** without a captured snapshot show a warning ("older
invoice has no captured layout… preview uses current invoice settings").

**Print flow:** `InvoicePdfPreview` → `onReady(pdfBytes)` → Print serializes
pages as images into `@page`-sized HTML → `printInvoiceWithDataURL`. So
**print == PDF == preview**, all from the frozen document snapshot.

### 10.6 Treatment catalog (`TreatmentSettings.tsx` + `TreatmentModal.tsx`)

- **Fields**: `name`, `description`, `price` (must be > 0) + id/created_at.
  **No code/SKU, no category, no duration, no tax class.**
- **TreatmentSettings** (`/settings/treatments`): table (Name/Description/
  Price/Actions) or card view, search, Add/Edit via `TreatmentModal`
  (all fields required; manual validation — "Please fill in all required
  fields."), Delete via ConfirmDialog (soft-delete, sync-aware). Empty
  state + "Add first treatment" CTA. Migrates legacy localStorage treatments
  on load.
- **Picking into appointments**: `NewAppointment` "Select Treatments" modal
  (search + catalog list, click toggles; chips with ×; live total). Empty
  catalog → warning with inline **"Create one"** (saves + auto-selects).
  On an existing appointment, treatments can only gain notes — not
  added/removed.

### 10.7 Receipt settings (`/settings/receipt`, `ReceiptSettings.tsx`)

Via `useInvoiceSettingsEditor` (draft/conflict banner via
`InvoiceSettingsDraftStatus` — cloud machinery to drop in the port):
- Document: language (en/id), date format (DD/MM/YYYY / Localized), timezone
  (free text), decimal places (0/1/2). (Currency/tax live in Currency
  settings.)
- Header/footer: multi-line textareas, **both required** (save blocked if
  empty).
- Paper presets: A4 210×297, Letter 216×279.4, **Thermal 80mm**,
  **Thermal 58mm**, Dot-matrix 120×138 (LX-310), Custom (20–500mm).
  Default/fallback = dotmatrix120.
- Margins top/right/bottom/left mm (0–50, 0.5 step); font (Inter, VT323,
  Courier, system-sans, system-mono — Inter/VT323 embedded as base64
  @font-face); per-section font sizes 6–72px; line-height slider 0.8–3×.
- **11 section toggles**: header, title, billedTo, invoiceInfo, treatments,
  treatmentNotes, totals, taxBreakdown, operatorInfo, footer, dividers.
- Live WYSIWYG sample-invoice preview + Save / Reset-to-default (confirm).

**Receipt vs invoice:** there is **no separate receipt document** — "Receipt
Settings" *is* the invoice layout; the printed invoice is titled
"TREATMENT RECEIPT". One document, one layout, used for preview, print, and
PDF. (Separate paper size config for invoice PDF vs receipt exists in the
code — `ReceiptConfig.paper: 'a4'|'dotmatrix'` — but the settings UI treats
them as one layout.)

**Invoice workflow end-to-end:** configure invoice settings (once, required)
→ appointment → "Generate invoice" → immutable snapshot (`INV-YYYYMM-####`,
status `unpaid`) → details: flip status via select (no payment records) /
void / print / PDF / receipt. No quantities, no manual line items, no due
dates, no payment methods, no partial payments, no send-to-patient.

---

## 11. Auth, lock screen, first-run

### 11.1 Auth (`src/pages/AuthLogin.tsx`, `src/components/AuthGate.tsx`)

- **Google OAuth sign-in** (Supabase) via popup flow (`OAuthWindow`); "use the
  demo of the web app as soon as you log in using your Google account". No
  email/password form in this build.
- `AuthGate` state machine: `checking → login | workspace | ready | offline |
  error`. Offline: a **cached session** from a previous login grants access
  (even if the token is expired; refreshed when connectivity returns); no
  cached session → "offline" state. Missing Supabase config → error state.
- `WorkspaceService`: after login the user must belong to a workspace;
  `WorkspaceSetup` offers **create workspace** (name) or **join via invite
  code** (uppercased) — a cloud multi-tenant concept.
- **Port note:** all of this (OAuth, workspaces, invite codes, cached cloud
  sessions) is cloud-specific. The offline WinUI port needs a **local
  account model**: first-run clinic setup (clinic name, currency, initial
  operator/admin) + local password/PIN lock. The natural offline analogues
  are `WorkspaceSetup`'s "create" path and the existing `UnifiedLockScreen`
  + `PasswordAndSecuritySettings`.

### 11.2 Lock screen (`src/components/UnifiedLockScreen.tsx` + `src/contexts/SecurityContext.tsx`)

**Credential: password only** — zero biometric/passkey/WebAuthn references
anywhere in the codebase. Modes: `loading` → `setup` (first-time encryption
password) / `unlock` (app start, DB encrypted) / `locked` (auto-lock during
use) / `migrate` (unencrypted DB with data — intentionally errors, telling
the user to export and create a new encrypted DB). Web with no encryption
configured skips the lock entirely.

- **Auto-lock triggers**: inactivity timer on keydown/scroll/mousedown/
  click/touchstart (checked every 60s); timeout 5/15/30/60/120/180 min or
  Never (default 15 min; disabled if auto-lock off); `visibilitychange`
  handled; manual **"Lock app now"** button.
- **Failed attempts**: 5 wrong passwords → **30-minute lockout** with live
  countdown (toggleable); recorded as a security incident
  (`BreachDetection.recordLockout`, escalates at 3+ lockouts/24h).
- **Emergency wipe** (forgot-password path): warning dialog → type
  `DELETE ALL DATA` → identity verification (password re-entry; skipped in
  forgot mode) → final irreversible confirm → signs out of cloud, resets DB
  files, clears storage, reloads. Multi-step and deliberate; the offline
  port drops only the cloud sign-out leg.
- Setup shows a password-strength checklist (≥8 chars, uppercase, number,
  special).

### 11.3 Operator model

`OperatorModal.tsx` — just two free-text inputs: **Operator Name\*** and
**Role\*** ("e.g., Doctor, Nurse, Administrator"). No specialty, no
permissions, no credentials, no active/inactive flag. The real RBAC
(`owner|member` + `canWrite`/`canDelete` checkboxes) lives in the cloud
workspace screen. Offline port: operator = name + role label; if multi-user
is needed, design a local role model.

### 11.4 Onboarding tours

12 tours defined in `src/utils/onboardingSteps.ts`, rendered by
`src/components/Onboarding/OnboardingOverlay.tsx` (spotlight + tooltip,
auto-placement with screen-edge protection, mobile-aware):
`dashboard` (welcome → stats → new-appointment FAB), `patients` (intro →
add btn → search → view toggle → columns), `appointments`, `appointment-
details` (invoice action → basic info → vitals → treatments → attachments),
`invoices`, `invoice-details` (print → status → delete → receipt preview →
treatments → total → view appointment), `add-patient` (record no. → name →
save), `new-appointment` (patient → operator → treatments → vitals → save),
`reports` (filters → export → patient analysis), `operator-settings`,
`treatment-settings`, `dashboard-final` (closing message).
- Tours **auto-start on first visit** to each page (`startTour` if
  `!isTourCompleted`); completion is persisted locally and merged with a
  server copy (`user-preferences`).
- **Port note:** the tour system is pure client-side and ports cleanly; the
  server merge should be dropped. Consider whether 12 tours are needed for an
  internal single-user app — a single first-run walkthrough may suffice.

---

## 12. Half-finished / dead-end UI (port candidates to drop or fix)

1. **AI provider modes "Local AI" / "Cloud AI"** (`src/i18n/locales/en/ai.json`):
   selectable in settings, labeled "Coming Soon", non-functional. **Drop**;
   keep the deterministic local engine.
2. **`AIPrivacyNotice`** component exists but is not rendered on the
   AIInsights page. **Wire it in (or an equivalent)** in the port.
3. **Missing non-clinical disclaimer** on AI Insights. **Add** in the port.
4. **`/settings/ai` route not linked** from the Settings hub. Link it or fold
   into the MCP/AI settings section.
5. **`DateRangeDropdown`** (polished custom-range picker) is built but unused
   by Dashboard and Reports. Verify usage elsewhere; likely **drop or adopt**
   consistently.
6. **Appointments list infinite-scroll leftovers** (`_loading/_hasMore/
   initialLoad`, dead append branch). **Drop** in the port.
7. **PatientList sort bug** (`handleSort` forces `'asc'` on the second
   `setSortDirection`); client-side sort of the loaded page only. **Fix**
   properly in the port.
8. **`ValidationService.PatientSchema`** (zod) doesn't match the actual
   patient model (UUID ids, email/medical_history/allergies fields that don't
   exist in the form; phone regex conflicts with the optional-UI). Looks
   vestigial. **Replace** with one validation source of truth in the port.
9. **AppointmentDetails imports `treatmentProvenance` from `server/` source**
   into the client bundle — layering smell; port should keep provenance
   formatting in the client domain layer.
10. **Dashboard `totalAllTime`** computed but never displayed; Recent Invoices
    has no "view all"; Reports invoice modal rows don't drill to invoice
    detail. Minor UX gaps to **close** in the port.
11. **ICD chips open `https://icd.who.int`** in a new tab — requires internet;
    in the offline port these links must degrade gracefully (or open a local
    reference).
12. **Gender immutable after creation** (no field on Edit Patient) with no UI
    explanation. Decide in the port.
13. **No vitals/treatment/operator/discount edit after appointment creation**
    — only notes + attachments. Decide deliberately in the port (add an edit
    flow or keep the snapshot model explicit).
14. **Gender options are `Mr./Ms./Mx.`** (titles, not sexes) — used as the
    gender field throughout. Preserve or normalize in the port.
15. **Comment in Dashboard** admits the Recent Invoices card intentionally
    omits a range filter; keep the decision explicit.
16. **Orphaned `/settings/ai`** — routed but absent from the hub menu;
    provider options Local/Cloud permanently disabled; `syncBeforeAnswering`
    field not rendered. Stub only — fold the working toggles into one AI
    section, don't rebuild as-is.
17. **Tauri security-settings persistence is a TODO** — `loadSettings`/
    `saveSettings` are no-ops on desktop; re-encrypt mode "coming soon";
    Tauri migrate disabled; Tauri reset-password no-op. Port must persist
    these for real (WinUI: DPAPI / app-local settings).
18. **No scheduled backups** — "Backup" is manual export only. The port
    should decide: scheduled local backups would be a genuine improvement
    for a clinic PC.
19. **Erase-all-data requires online workspace validation** before the
    dialogs even open. Offline port: local erase flow only.
20. **Retention legal minima and the privacy-notice text are PLACEHOLDERS**
    (pending lawyer review; English-only, `TODO(i18n)`). Not legal advice —
    the PRD must flag legal review for the clinic's jurisdiction.
21. **Two "roles" concepts**: Operator modal role is free text; the real
    RBAC (owner/member + canWrite/canDelete) lives in the cloud workspace
    screen. Offline port: one operator record; if multi-user is needed,
    design a local role model.
22. **Privacy-notice acknowledgment is per-device localStorage**, not
    synced.

---

## 13. Offline-port cheat sheet

**Cloud/network-dependent — drop or redesign for offline:**
- Google OAuth login (`AuthLogin`), workspaces/invite codes, cached cloud
  sessions, `WorkspaceSetup` create/join (`§11.1`); cloud-sync settings
  (`/settings/cloud-sync`: Google sign-in profile, invite codes, team
  Write/Delete permissions, PayPal $20 lifetime upgrade, trial limits);
  invoice-settings draft/sync/conflict + shared-profile publishing (`§9`);
  MCP server + clinical-draft approvals + connection tokens + "cloud billing";
  AI "Local/Cloud" provider modes (unimplemented anyway); WHO ICD API web
  path (use the bundled local ICD DB — the desktop build already does);
  attachment background upload + signed read URLs (keep device-local
  storage only); update checker (`UpdateBanner` — could be kept as a manual
  "check for updates" if the PC ever gets network); demo record limits
  (`DemoLimitBanner`); onboarding server merge; TOTP MFA (Supabase);
  attachment migration to Supabase Storage; erase-all-data's cloud-wipe leg
  + online validation gate; retention's workspace scoping; privacy-notice
  ack syncing; security-audit's `WorkspaceService` owner gate.
- Replace auth with a **local account model**: device encryption password
  (`UnifiedLockScreen` + `PasswordAndSecuritySettings` minus MFA/cloud) +
  local operator selection. First run = clinic/currency/document settings +
  initial operator (the current app's forced `/settings/operators` redirect
  on first dashboard visit is the model to copy).
- Currency/tax formatting, invoice document settings, receipt layouts,
  retention/erasure (local purge), activity/audit logs, manual JSON/Excel
  backup + restore, PIN/password lock + auto-lock + failed-attempt lockout
  (5 wrong → 30 min, incidents recorded) + emergency wipe — all keepable
  offline.

**Fully offline-capable as-is:** Dashboard stats/charts, Reports (both
sections + XLS export), deterministic AI engine (port the builders +
`getAIContextSnapshot`), all CRUD screens, invoice generation/numbering,
invoice/receipt documents, ICD search via local DB, onboarding tours
(minus server merge), theming + 7-language i18n.

**Canonical end-to-end clinic workflow to preserve:**
1. (First run) Operator setup → clinic/currency/document settings
2. Add patient (`/patients/add`) → explicit lawful-basis choice → detail page
3. "Create Appointment" from patient → `/appointments/new` (vitals + custom
   exams + ICD + treatments + discount) → `/appointments/:id` (notes,
   attachments)
4. "Generate invoice" → immutable invoice snapshot → `/invoices/:id`
   (mark paid / void / print / receipt / PDF)
5. Dashboard (today's activity, revenue), Reports (operator performance XLS,
   patient vital trends), AI Insights (deterministic local Q&A with evidence)
