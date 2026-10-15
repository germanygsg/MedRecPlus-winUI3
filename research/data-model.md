# MedRecPlus — Data Model & Data Flow Research

Source repo: `~/workspace/medrecplus` (branch `online`, commit `3ff650d`, 2026-10-13).
Read-only analysis for the WinUI3 offline port. All file paths are repo-relative.

---

## 1. Architecture at a glance

MedRecPlus is a **local-first, cloud-syncing** clinic app (React + Vite + Tauri/Web). The client can run fully offline against a local mirror; sync pushes/pulls through an Express API (`server/`) and Supabase (Postgres + Auth + Storage).

**Service layering (client)** — `src/services/`:

| Layer | Files | Job |
|---|---|---|
| Mode router | `DataService.tsx`, `SimpleDataService.tsx` | `DataService` routes every call to `ApiClient` (online) or `OfflineDataService` (offline) based on `ConnectivityContext` app mode. `SimpleDataService` wraps it with caching + offline fallback on API failure. |
| Offline CRUD | `OfflineDataService.ts` | Local-first CRUD over `UnifiedStorage`; every mutation also appends to `OfflineChangeLog`. |
| Storage backend | `UnifiedStorage.ts` | Singleton; prefers **IndexedDB**, falls back to localStorage. `mutate()` gives transactional read-modify-write. Money columns are normalized to numbers on write (`normalizeStoredRows`). |
| Settings in SQLite | `DbSettings.ts` | Under Tauri, critical settings (onboarding completion etc.) go to an **encrypted SQLite DB via Tauri commands** `get_setting`/`set_setting` (key-value `kv_settings` table in `src-tauri/src/lib.rs`). Falls back to `UnifiedStorage` on web. |
| Mirror | `LocalMirror.ts` | Merges online reads into the local mirror (never replaces; preserves attachment fields the API list payloads omit). |
| Sync | `SyncService.ts`, `syncMerge.ts`, `syncPayload.ts`, `syncReadModel.ts`, `OfflineChangeLog.ts`, `SyncConflictLog.ts` | Push/pull pipeline + pure merge logic. |
| Attachments | `AttachmentUploadService.ts` | Retry queue for pending photo uploads to Supabase Storage. |
| Audit | `AuditService.ts`, `SecurityService.ts` | Typed, fire-and-forget PHI access events; batched upload to server. |
| Erasure | `ErasureService.ts`, `ErasureSync.ts` | GDPR Art. 17 hard-delete flow (owner-gated). |

**Server** — `server/src/`: Express routes per entity (`routes/*.ts`), Drizzle schema (`db/schema.ts`, 21 migrations), workspace-scoped auth middleware (`middleware/auth.ts`, `middleware/workspaceContext.ts`).

**What lives where (current cloud version):**

| Data | Location |
|---|---|
| Working clinical data (patients, appointments, invoices, operators, treatments, exams) | Local: IndexedDB (web) — entity keys `patients`, `appointments`, `invoices`, `operators`, `treatments`, `custom_examinations`; Cloud: Postgres (Supabase `medrecplus-demo`) |
| Critical settings | Tauri: encrypted SQLite `kv_settings` via `get_setting`/`set_setting`; web: UnifiedStorage settings |
| Audit events (device copy) | IndexedDB audit store (via `SecurityService`); server: `audit_events` table (append-only) |
| Attachment full images | Local-only (base64 `data` field in the record); thumbnails travel with the record; full bytes in Supabase Storage (`storagePath`) |
| Changelog / sync state | localStorage (`medrecplus_offline_changelog`), pull watermarks, run markers |
| AI query logs (`ai_logs`) | Local-only IndexedDB store |
| `ValidationService.ts` | NOTE: appears **legacy/unused** — its zod schemas describe UUID-string ids and fields (email, allergies, appointment `status`) that do not match the real numeric-id model. Real validation lives in UI pages, `computeInvoiceTotals` (`src/utils/currencyUtils.tsx`), server route zod checks, and DB constraints. Do NOT port it verbatim. |

For the WinUI3 **offline-only** port, the sync/mirror/upload layers, audit upload, workspace, subscription, and MCP tables can be dropped or simplified; the entity tables, numbering, math, immutability, soft-delete, erasure and audit-logging semantics are what must be carried over.

---

## 2. Entities (canonical: `server/src/db/schema.ts`)

Every clinical table: integer serial `id` PK, `workspace_id` FK → `workspaces.id` (cascade), `created_at`/`updated_at` (DB default now), `deleted_at` (soft delete), `sync_id` (UUID text, unique, default `gen_random_uuid()`), plus `rev` (integer, default 1, DB-trigger-incremented on every update — conflict-resolution counter, migration `0006`).

### workspaces
| Field | Type | Notes |
|---|---|---|
| `id` | serial PK | |
| `name` | text | Clinic name |
| `invite_code` | varchar(64), unique | Invite others to the workspace |
| `owner_user_id` | text | Auth user id of owner |
| `plan` | `trial` \| `paid`, default `trial` | |
| `created_at`, `updated_at` | timestamp | |

### workspace_members
| Field | Type | Notes |
|---|---|---|
| `id` | serial PK | |
| `workspace_id` | FK → workspaces (cascade) | |
| `user_id` | text | Auth user id; unique per workspace AND unique per user (`workspace_single_membership_idx` → one workspace per user) |
| `role` | `owner` \| `member`, default `member` | Owners always get full write/delete (resolved in `workspaceContext.ts`) |
| `can_write`, `can_delete` | bool, default true | Per-member permission flags |

### patients
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | as above |
| `record_number` | varchar(255), NOT NULL | **Format `PT{YYYY}{6-digit seq}`**, e.g. `PT2026000142`. Unique per workspace (`patients_workspace_record_number_unique_idx`). Never reused; soft-deleted rows still occupy the number (uniqueness counts them). |
| `name` | text NOT NULL | |
| `age` | integer NOT NULL | |
| `address` | text nullable | |
| `phone_number` | varchar(50) NOT NULL | Validated: `^[+]?[\d\s-()]{7,25}$` (client `ValidationService.validatePhoneNumber`) |
| `initial_diagnosis` | text nullable | |
| `icd_codes` | json | Array of `{code, title, linearizationUri?, foundationUri?, type: 'icd11'\|'icf'}` |
| `gender` | `Mr.` \| `Ms.` \| `Mx.` nullable | Title-style enum (not M/F) |
| `attachments` | json | `AttachmentItem[]` (see §5) |
| `consent_basis` | `explicit_consent` \| `contract` \| `legal_obligation` \| `vital_interests` \| `public_health`, nullable | GDPR lawful basis (migration `0008`). NULL = not yet recorded; UI forces explicit choice on create |
| `consent_date` | timestamptz nullable | |
| `consent_recorded_by` | text nullable | Auth user id who recorded it |

### operators (staff who perform treatments)
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | |
| `name` | text NOT NULL | |
| `role` | text NOT NULL | Free-text job role (NOT the workspace RBAC role) |

### treatments (price catalog)
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | |
| `name` | text NOT NULL | |
| `description` | text nullable | |
| `price` | decimal(10,2) NOT NULL | Arrives as string from Postgres; normalized to number locally |
| `notes` | text nullable | |

### custom_examinations (user-defined vital-sign measurements)
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | |
| `name` | text NOT NULL | e.g. "Oxygen saturation" |
| `unit` | text NOT NULL | e.g. "%" |

Each custom examination becomes a dynamic vital-sign field keyed `custom_{name}` on appointment `vitalSigns` (string|number; see §5). `src/utils/customExaminationSnapshots.ts` extracts these.

### appointments (visits / examination records)
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | |
| `patientId` (`patient_id`) | int NOT NULL, FK → patients (cascade) | |
| `patientName` (`patient_name`) | text NOT NULL | **Denormalized snapshot** of patient name at visit time |
| `operatorId` (`operator_id`) | int NOT NULL, FK → operators | No cascade (restrict implied) |
| `operatorName` (`operator_name`) | text NOT NULL | Denormalized |
| `date` | timestamp NOT NULL | Visit datetime |
| `vitalSigns` (`vital_signs`) | json NOT NULL | `{bloodPressure: string, respirationRate: number, heartRate: number, borgScale: number, custom_<key>?: string\|number}`. Borg scale 0 = not recorded; 1–3 light, 4–6 moderate, 7+ high exertion (per `AppointmentDetails.tsx`) |
| `treatments` | json NOT NULL | `TreatmentItem[]`: `{id, name, description?, price, notes?}` — a **snapshot copy** of the treatment catalog rows at the time of the visit |
| `totalPrice` (`total_price`) | decimal(10,2) NOT NULL | Sum of treatment prices (pre-discount; discount stored separately) |
| `discountType` (`discount_type`) | `none` \| `percent` \| `fixed`, default `none` | Per-appointment discount (migration `0011`), applied before tax |
| `discountValue` (`discount_value`) | decimal(10,2), default 0 | % (0–100) or fixed amount |
| `notes` | text nullable | |
| `description` | text nullable | |
| `diagnosis_codes` | json | ICD code array like patients |
| `attachments` | json | `AttachmentItem[]` |

### invoices (bills — **immutable once issued**)
| Field | Type | Notes |
|---|---|---|
| `id`, `workspace_id`, `sync_id`, timestamps, `deleted_at` | | |
| `invoiceNumber` (`invoice_number`) | varchar(100) NOT NULL | **Format `INV-{YYYYMM}-{4-digit seq}`**, e.g. `INV-202610-0042`; generated client-side as max+1 within the month prefix, unique per workspace. Fallback `INV-YYYYMM-<timestamp tail>` on error. |
| `appointmentId` (`appointment_id`) | int NOT NULL, FK → appointments | One invoice per appointment (UI checks for an existing invoice before issuing) |
| `patientId`, `patientName`, `operatorId`, `operatorName` | | Denormalized snapshots (same as appointments) |
| `date` | timestamp NOT NULL | Issue date |
| `appointmentDate` (`appointment_date`) | timestamp NOT NULL | Snapshot of the visit date |
| `vitalSigns`, `treatments` | json NOT NULL | Snapshots carried over from the appointment |
| `totalAmount` (`total_amount`) | decimal(10,2) NOT NULL | Final charged amount |
| `subtotalAmount` (`subtotal_amount`) | decimal(10,2) nullable | Treatment subtotal before tax; equals totalAmount when no tax (migration `0009`) |
| `taxLabel` (`tax_label`) | varchar(50) nullable | Snapshot of configured tax label, e.g. "VAT", "PPN", "GST" |
| `taxType` (`tax_type`) | `none` \| `percent` \| `fixed` nullable | |
| `taxValue` (`tax_value`) | decimal(10,2) nullable | % (0–100) or fixed amount |
| `taxAmount` (`tax_amount`) | decimal(10,2) nullable | Tax actually charged |
| `discountType`, `discountValue`, `discountAmount` | nullable | Discount snapshot frozen at issuance (from the appointment) |
| `documentProfile` (`document_profile`) | jsonb nullable | Immutable `InvoiceDocumentProfile`: `{version: 1, settings_version, settings_hash, patient?: {id, record_number, age, gender, phoneNumber, address}}` — pins the invoice document settings version in force at issuance (migration `0019`) |
| `hasDocumentSnapshot` (`has_document_snapshot`) | bool default false | Server-owned marker of an approved PDF/document snapshot, independent of the invoice number |
| `status` | `paid` \| `unpaid` \| `void` \| `pending`, default `unpaid` | |

**Frozen-invoice rule** (`OfflineDataService.updateInvoice`): once an invoice has a document snapshot/profile, only `status`, `invoiceNumber`, `deleted_at` may change; any other edit throws *"Issued invoice details are frozen. Create a new invoice for a correction."*

### receipt_config
| Field | Type | Notes |
|---|---|---|
| `id` | serial PK | |
| `workspace_id` | FK → workspaces, unique | One row per workspace |
| `header`, `footer` | text NOT NULL | Receipt/invoice header & footer text (defaults: `MAPLEWOOD FAMILY CLINIC…` header, thank-you footer) |
| `updated_at` | timestamp | |

### subscription_payments (SaaS billing — NOT needed for offline port)
`id`, `workspace_id` FK, `paypal_order_id` (unique), `paypal_capture_id`, `amount_usd` decimal(10,2), `status`: `pending`|`completed`|`failed`, `created_at`, `completed_at`.

### audit_events (AUD-02, server-side accountability log)
| Field | Type | Notes |
|---|---|---|
| `id` | text PK | Client-generated event id |
| `workspace_id` | FK → workspaces (cascade) | |
| `actor_id` | text NOT NULL | Auth user UUID — **stamped from the session server-side, never trusted from the client** |
| `event` | text NOT NULL | Typed event name (see §9) |
| `entity_type` | text nullable | `patient` \| `appointment` \| `invoice` \| `attachment` \| `workspace` \| `operator` |
| `entity_sync_id` | text nullable | Record identity **by ID only — never names/PHI** |
| `at` | timestamptz NOT NULL | When the action happened |
| `meta` | jsonb NOT NULL default `{}` | Scalar-only bag (DB trigger enforces scalars; strings/numbers/booleans only) |
| `chain_hash` | text NOT NULL | Per-workspace SHA-256 hash chain stamped by a `BEFORE INSERT` trigger — tamper-evident; no UPDATE/DELETE policies exist, so history cannot be rewritten |
| `created_at` | timestamptz | |

### api_tokens (MCP credentials — not needed for offline port)
`id` (uuid text PK), `workspace_id` FK, `name`, `token_hash` unique, `scopes` text[] (default `['analytics:read']`), `expires_at` (default now+90d), `last_used_at`, `created_by`, `created_at`, `revoked_at`.

### mcp_workspace_settings / mcp_clinical_drafts / invoice_settings_versions
MCP/AI infrastructure — not needed for the offline port. `invoice_settings_versions(workspace_id, version, configuration jsonb, settings_hash)` is the server side of the frozen invoice-document-settings mechanism (migration `0019`); the offline port should keep the *concept* (settings version hash pinned on each invoice) even without the server.

### user_preferences
`id`, `user_id` (auth UUID as text), `key`, `value` jsonb, `updated_at`; unique `(user_id, key)`. Used for onboarding completion etc. — for offline port, a per-machine local settings table covers this.

### Local-only stores (not in Postgres)
- `ai_logs` (IndexedDB): `AILogEntry {id, timestamp, userId?, question, intent, toolsUsed[], rowCounts, modelUsed?, fallbackReason?, durationMs}` — AI Insights query history; never synced.
- Activity/security logs (IndexedDB via `SecurityService`): legacy events + AUD-01 typed events waiting for upload.
- Settings store (IndexedDB `settings` / SQLite `kv_settings`): currency config (`{code, symbol, decimalPlaces, thousandSeparator, decimalSeparator, taxLabel, taxType, taxValue}`), receipt config, invoice settings drafts, etc.
- Backup format (`src/utils/backupFormat.ts`): JSON `{version: '1.0'|'2.0', backupDate, operators, treatments, patients, appointments, invoices, customExaminations?, settings?}` — the portable export/import schema.

---

## 3. Relationship graph

```
workspaces 1───* workspace_members        (user_id per workspace unique; a user belongs to ONE workspace)
workspaces 1───* patients ──* appointments ──* invoices
                        │          │               │
                        │          *── operatorId ──┘
                        │          *── patientId ───┘
                        *──* appointments (patientId, cascade)
                        *──* invoices     (patientId)
operators 1───* appointments (operatorId), 1───* invoices (operatorId)
treatments (catalog, no FKs — snapshots copied into appointments/invoices.treatments)
custom_examinations (catalog, no FKs — extends vitalSigns via custom_<key>)
```

Notable denormalization: `patientName`/`operatorName` are copied onto every appointment and invoice at creation; `invoice.patient` profile snapshot is frozen into `documentProfile`. Deleting an operator does NOT cascade (appointments keep their snapshot); deleting a patient cascades to its appointments (FK cascade).

---

## 4. Number formats & identity

| Identifier | Format | Allocation |
|---|---|---|
| Patient `record_number` | `PT{YYYY}{seq:06d}` — e.g. `PT2026000142` | Server: `patient_record_number_seq` Postgres sequence (migration `0005`), race-free via `nextval()`, endpoint `GET /api/patients/record-number/generate`. Offline: `OfflineDataService.generateRecordNumber()` scans local max with same prefix and adds 1. **Port needs a local counter**: one incrementing counter per year (or a table `sequence_counters(name, year, next_value)`), seeded above the max existing suffix. Generated-but-unused numbers are lost (gaps acceptable). Collisions surface as HTTP 409 in the cloud version; locally, enforce the per-clinic unique index. |
| Invoice `invoiceNumber` | `INV-{YYYYMM}-{seq:04d}` — e.g. `INV-202610-0042` | Client-side: max existing number with the same `INV-YYYYMM-` prefix + 1 (needs all invoices visible). Enforced unique per workspace (`invoices_workspace_invoice_number_unique_idx`). Fallback on error: `INV-YYYYMM-<last-4-of-timestamp>`. |
| Local offline record ids | **Negative integers** (`-1, -2, …`), floor persisted in `medrecplus_local_id_floor` | Server ids are positive serials; offline-created rows get negative ids and are **remapped to real ids after sync** (children's FKs rewritten). Offline-only port: plain autoincrement works, but keeping a distinct negative-id scheme is unnecessary — a single SQLite autoincrement is enough. |
| `sync_id` | UUID v4 per record, unique across the table | Used as the stable cross-device identity for sync, erasure markers, audit references. Offline port can keep it as the stable GUID for export/import deduplication. |
| `rev` | Server-stamped integer, increments on every write | Conflict-resolution counter (immune to clock skew). Offline-only: not needed unless multi-device sync is ever added; keep `updated_at` as the audit field. |

---

## 5. Composite/JSON structures

**VitalSigns** (`appointment.vital_signs`, `invoice.vital_signs`):
```ts
{ bloodPressure: string, respirationRate: number, heartRate: number,
  borgScale: number /* 0 = not recorded */,
  custom_<name>: string | number }  // one per custom_examinations row
```

**TreatmentItem** (snapshot inside appointment/invoice `treatments`):
```ts
{ id: number, name: string, description?: string, price: number, notes?: string }
```

**AppointmentAttachment** (inside `patients.attachments`, `appointments.attachments`):
```ts
{ id: number, sync_id?: string, name: string, mimeType: string, size: number,
  data?: string        /* base64 full image — LOCAL ONLY, stripped at API boundary */,
  thumbnail?: string   /* small preview (~256px) — travels with the record */,
  storagePath?: string /* Supabase Storage key once uploaded */,
  uploadStatus?: 'pending' | 'uploaded',
  description?: string, created_at: string }
```
Flow: photo added locally with `data` + `thumbnail` (`uploadStatus='pending'`); `AttachmentUploadService` scans the local mirror, gets a signed URL, uploads bytes, then stores `storagePath`/`uploaded`. Server records only ever carry thumbnails; list payloads omit attachments entirely and the mirror preserves the local copy (`LocalMirror` `PARTIAL_PAYLOAD_KEYS`).

**InvoiceDocumentProfile** (frozen on invoice): `{version: 1, settings_version, settings_hash, patient?: {id, record_number, age, gender, phoneNumber, address}}`. A device-local `documentSnapshot` (full PDF snapshot) is cached on the device only, never authoritative.

**ICDCode**: `{code, title, linearizationUri?, foundationUri?, type: 'icd11'|'icf'}`.

---

## 6. Write path

**Online:** UI → `DataService` (mode=online) → `ApiClient` (JWT bearer from Supabase auth) → Express route → `requireSupabaseAuth` + workspace-context middleware (`requireCanWrite`/`requireCanDelete`/`requireOwner`) → DB write with `updated_at`/`rev` stamped by DB triggers → response mirrored into local store via `LocalMirror.mirrorOne`.

**Offline:** UI → `DataService` (mode=offline) → `OfflineDataService` → `storage.mutate(entity, fn)` (single-transaction read-modify-write on IndexedDB; `updated_at` set on every mutation) → `OfflineChangeLog.append(entityType, op, entityId, sync_id)` (collapsed per record, capped at 500 entries, localStorage).

**Delete = soft delete everywhere.** Every entity delete sets `deleted_at` (and bumps `updated_at`); reads filter `deleted_at`. Erasure (GDPR Art. 17) is a separate, owner-gated **hard delete** path (§8).

---

## 7. Read path

**Online:** `DataService.getX()` → `ApiClient.getX()` (paginated, server-side search, 50/page) → `LocalMirror.mirror(table, rows, {throttled: true})` — merges into the local mirror (never replaces; preserves attachments; throttled to 2s per table so paging doesn't rewrite everything). Detail reads mirror immediately. Online reads thus keep the offline store warm without an explicit download.

**Offline:** `OfflineDataService` getters over IndexedDB, soft-deletes filtered, with in-memory search/filter + pagination (50/page) since there's no server.

**Fallback:** `SimpleDataService` wraps `DataService` with per-entity caches and transparent offline fallback on API error, plus `getEmptyPaginatedResult` shapes.

---

## 8. Sync pipeline (cloud version — reference for understanding, mostly not needed offline)

`SyncService.syncAllTables`: per-workspace table order **patients → operators → treatments → custom_examinations → appointments → invoices** (parents before children, enforced). Per table (`syncOneTable`):
1. Load local (including soft-deleted).
2. Heal FKs from a persisted id-map of an interrupted run; `partitionPushable` holds back children whose parent has no server id.
3. Pull remote (delta since a DB-stamped `sync_updated_at` watermark, full pull periodically; never push if pull failed).
4. `mergeTable` — merge local vs remote with documented precedence:
   - **Higher `rev` wins** (server-stamped; immune to clock skew); timestamps only tiebreak legacy rows.
   - Later intent (tombstone `deleted_at`, else `updated_at`) wins; **tombstone wins exact ties**; edit-after-delete resurrects (conflict logged).
   - Business-key matching (`record_number`, `invoiceNumber`) and server-id fallback adopt remote `sync_id`s to avoid duplicates.
   - Records the merge marks `localWins` + changelog-dirty + never-pushed → dirty set.
5. Push dirty records via an explicit field allowlist per table (`syncPayload.ts` — camelCase client fields mapped to snake_case columns; `id`/`sync_id`/`workspace_id`/server columns can never leak into an upsert). Parent id-map collected during push; children FKs remapped (`remapForeignKeys`); tombstones for never-pushed rows are dropped as phantoms.
6. Drain the changelog per record as each table's push succeeds (per-table drain, not wholesale).
7. Conflicts recorded in `SyncConflictLog` and reported.

Delta cursor rule: `maxSyncCursor` only ever uses DB-stamped `sync_updated_at` — client clocks can't skip rows.

---

## 9. Validation rules & business logic

- **Record numbers**: `PT{YYYY}{06d}`; offline generator scans local max with the same year prefix (including soft-deleted rows) and pads to 6; digits beyond 999999 just grow longer; numbers never reused.
- **Invoice numbers**: `INV-{YYYYMM}-{04d}`, max+1 within month prefix; unique per workspace; one invoice per appointment (UI redirects to the existing invoice instead of double-issuing).
- **Invoice totals math** (`computeInvoiceTotals`, `src/utils/currencyUtils.tsx` — canonical): subtotal = Σ treatment prices; discount **applied first** (percent: integer cents/basis points, clamped 0–100; fixed: `min(value, subtotal)` — never exceeds subtotal); then **tax computed on the discounted subtotal**; `total = subtotal − discount + tax`. Whole breakdown frozen onto the invoice at creation; later settings changes never rewrite it.
- **Issued invoices are frozen**: only `status`, `invoiceNumber`, `deleted_at` may change after issuance; edits to anything else throw (client + should be enforced in DB/app layer in the port).
- **Invoice status**: `paid | unpaid | pending | void`; default `unpaid`. Dashboard revenue = Σ `totalAmount` where `status='paid'`.
- **Appointment has NO status field** in the real model (the `ValidationService` zod schemas mentioning statuses/UUIDs are legacy and unused).
- **Phone**: `^[+]?[\d\s-()]{7,25}$`. **Password policy** (used for local app lock): ≥8 chars, upper, lower, digit, special. Input sanitization strips `javascript:`/`data:`/`vbscript:` protocols and `on*=` handlers.
- **Consent**: `consent_basis` must be one of the 5 lawful bases, chosen explicitly on patient create (never defaulted/invented); changes emit a `consent_updated` audit event.
- **Soft deletes**: all entities use `deleted_at`; unique constraints (record/invoice numbers) count soft-deleted rows.
- **Erasure (GDPR Art. 17)** — `ErasureService`:
  - Owner-only (`ErasureRefusedError` otherwise); operator must type the patient's exact name to confirm (interactive), retention guard `canErasePatient` must allow.
  - **Scope**: patient row + their appointments + their invoices + AI-log entries referencing the record number (best-effort). Shared catalogs (treatments, operators), other patients: untouched.
  - Local purge is a **hard delete** (rows removed, not tombstoned); changelog entries drained by `sync_id` so sync can never resurrect them; server leg via owner-gated `POST /api/patients/:id/erase` or queued in localStorage when offline (`ErasureSync`); storage objects deleted; propagation to other devices via a `patient_erased` audit event.
  - **Retention guard** (`src/utils/retentionGuard.ts`): erasure refused while `now − recordDate < minimumDays` under a *confirmed* (lawyer-signed) retention rule; unknown law = allowed, draft rules advisory-only.
- **Attachment limits**: migration `0012` sets bucket limits; client validates real decoded byte size (not base64 length) before signing an upload.

---

## 10. Auth / RBAC / workspace model (cloud version)

- **Auth**: Supabase Auth (email/password + Google OAuth in `SupabaseService`; sessions via `@supabase/supabase-js`). Client also has a **local app lock/PIN** (`SecurityContext`, bcrypt in `SecurityService`, `EncryptionSetup` page; Tauri encrypts the local SQLite DB).
- **Workspace tenancy**: every clinical row carries `workspace_id`; all queries are workspace-scoped (`withUserContext` sets `app.current_user_id`; RLS policies key off it). One user belongs to at most one workspace (unique index on `user_id`).
- **Roles**: `owner` | `member`. The workspace creator is resolved as `owner` regardless of the membership row. Owners always have write+delete; members' `can_write`/`can_delete` flags gate them. Middleware: `requireSupabaseAuth` → attach workspace context (fail-closed 503 if the DB lookup fails) → `requireCanWrite` / `requireCanDelete` / `requireOwner`.
- **Owner-gated actions** (client + server): patient erasure, workspace settings publication, invoice-settings publish, API token management, retention purge, subscription flows. **For the single-clinic offline port: the local operator is effectively the owner** — keep a role field for future multi-user but the RBAC server layer collapses away.
- **Demo/trial limits**: `DEMO_MODE` + `DEMO_RECORD_LIMIT=10` enforced DB-side (migration `0013`) and in routes — SaaS concern, not needed offline. `plan` (`trial`|`paid`) likewise.

---

## 11. Audit trail

- **AUD-01 (device)**: typed events written to local IndexedDB via `SecurityService.logAuditEvent`; fired fire-and-forget from mutation paths and views (`AuditService.logDataEvent`). Taxonomy (`src/services/SecurityService.ts` `DATA_AUDIT_EVENTS`): `patient_viewed` (deduped 60s per actor+record), `patient_created/updated/deleted/erased`, `patient_exported/printed`, `consent_updated`, `record_created/updated/deleted/searched` (searches throttled 60s), `attachment_uploaded/downloaded`, `user_invited`, `role_changed`, `security_incident` (permanent legal record, exempt from retention purge), plus legacy `app_locked/unlock_*` events.
- **PHI rule**: events carry **IDs only** (`actorId` = auth UUID, `entitySyncId` = record sync_id); `meta` is restricted to `string|number|boolean` scalars — no names, contact data, or clinical content. Enforced by types client-side and by a DB trigger server-side.
- **AUD-02 (server)**: batched upload (`POST /api/audit-events/batch`, ≤200/batch, idempotent; marked synced only on 2xx, retried every 5 min); rows get a per-workspace SHA-256 hash chain from a `BEFORE INSERT` trigger; no UPDATE/DELETE policies — append-only.
- `patient_erased` doubles as the permanent erasure record and the cross-device purge marker.

**Offline port guidance**: keep the device-side audit log (SQLite table `audit_events` with the same taxonomy and no-PHI rule); the hash chain can be computed locally per session/device; no upload leg.

---

## 12. Implications for the WinUI3 offline port

1. **Local DB**: SQLite replaces IndexedDB; one table per entity above, same columns (minus `workspace_id`/`sync_id`/`rev` unless export-dedup needs them — keep `sync_id` GUID as stable export identity). Keep `deleted_at` soft deletes, `created_at`/`updated_at`, unique indexes on patient `record_number` and invoice `invoiceNumber`.
2. **Numbering**: local per-year counter for `PT{YYYY}{06d}` (seed above existing max); `INV-{YYYYMM}-{04d}` max+1 within month; one invoice per appointment; freeze issued invoices (only status/number/deletion editable).
3. **Math**: port `computeInvoiceTotals` exactly (discount first, then tax on discounted subtotal; percent 0–100 clamp; fixed ≤ subtotal).
4. **Denormalization**: copy `patientName`/`operatorName` + treatment snapshots + vital signs onto appointments/invoices at creation; pin a settings-version hash (`InvoiceDocumentProfile`) on each invoice.
5. **Erasure**: owner-confirmed hard delete of patient + appointments + invoices (+ their attachments), with the retention-guard hook and typed-name confirmation; audit the erasure.
6. **Audit**: local `audit_events` table with the same event taxonomy and ID-only/PHI-free rule.
7. **Settings**: local key-value settings (currency, tax label/type/value, receipt header/footer, clinic invoice settings with versioning + hash for document profiles).
8. **Drop**: `workspaces`/`workspace_members` (single clinic), `subscription_payments`, `api_tokens`, MCP tables, sync pipeline, attachment upload queue (keep `data`+`thumbnail` fields local-only; full bytes stay on disk), AI logs are optional.
9. **Auth**: no cloud auth needed; keep a local operator lock (PIN/password, bcrypt-style hashing) mirroring `SecurityContext` if the clinic wants workstation protection.
10. **Backup**: implement the portable JSON export/import format (`backupFormat.ts`: versioned, entity arrays + settings) — it's the offline equivalent of the sync pipeline and the natural migration path from the existing app.
