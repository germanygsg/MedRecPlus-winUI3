# Security and Excel update verification

Implemented and verified on H410M on 2026-10-18. All automated scenarios use
temporary encrypted databases and a separate Credential Locker resource based
on the sample data path. The clinic database, preferences and vault key are
not used by the harness.

## Commands (from `src`)

```powershell
dotnet build MedRecPlus.sln -c Debug -p:Platform=x64 -v:quiet
dotnet run --project MedRecPlus.SmokeTest -c Debug -p:Platform=x64
./verification/Build-UpdateChecks.ps1
./verification/Build-UpdateChecks.ps1 -FullIdleWait
./verification/Build-UpdateChecks.ps1 -Preview
./verification/Build-SearchChecks.ps1
./verification/Build-ValidationChecks.ps1
./verification/Build-InvoiceChecks.ps1
```

`-FullIdleWait` uses the production five-minute setting, hides the unattended
sample window, and waits 330 seconds without input. Results, screenshots,
sample workbooks and start/end UTC timestamps are saved beside the copied
executable. There is no test-only UI or runtime hook in the shipped app.
`-Preview` runs the same short checks and leaves the isolated sample app open
on Reports for manual save-picker/layout checks. Close it after inspection.

## Results

- Normal solution build: 0 warnings/errors.
- Core/Windows smoke runner: 126 PASS assertions, `SMOKE_RESULT=PASS`.
- Native update checks: `UPDATE_RESULT=PASS`, including real PasswordVault
  writes, wrong-password rejection, toggle OFF/ON, direct startup routing,
  timer reset/Stop/disabled behavior, lock audit, all shared references cleared,
  revoked DB access, abandoned dialog removal and password unlock/restart.
- Five-minute production idle test: PASS. UTC timestamps
  `2026-10-18T12:34:30.2886974+00:00` to
  `2026-10-18T12:40:00.3114225+00:00` (19:34:30–19:40:00 Asia/Jakarta).
- Native search, form validation and invoice suites:
  `SEARCH_UI_RESULT=PASS`, `VALIDATION_RESULT=PASS`, `INVOICE_RESULT=PASS`.
  Validation helpers now select the actual error style rather than any nearby
  TextBlock, wait for a XamlRoot, and expand lazy settings content before
  inspecting field parents.
- Both sample workbooks opened in ONLYOFFICE. Data, payment and annual summary
  views show proper dates, Unicode text, numeric rupiah, leading-zero phone,
  per-sheet colors, filters and totals. Filter-arrow padding is included in
  fitted column widths. ClosedXML read-back assertions cover all four sheet
  names, headers, counts, typed numbers/dates/text, formula-like patient names
  retained as literal text, ranges, void/deleted records, annual totals and
  colliding/invalid operator names.
- Excel/LibreOffice application-specific checks remain pending because those
  applications are not installed. The app requires neither application.
- Final native Reports layout and the real Save As picker were inspected.
  The picker displayed `MedRecPlus_Data_2026-10-18.xlsx` with XLSX type. The
  computer-use helper could capture its PickerHost child window but refused
  input because it belonged to a different process and exposed no targetable
  window (`point ... is over PickerHost.exe, not target window`). The final
  Save click therefore remains a manual acceptance step; generated workbook
  bytes and direct file writes were exercised by the isolated harness.

## Export definitions and schema adaptations

Visits are stored in `appointments`. Patients have a recorded Age and no DOB:
DateOfBirth is blank, Age is the recorded value. Payment operator attribution
uses the invoice's operator because payments have no separate operator field.

Full data export includes all surviving records, including void invoices marked
VOID; patient financial aggregates exclude void invoices. Deleted patients or
visits also exclude their invoices/payments. Soft-deleted operator profiles
retain historical attribution in surviving visits/invoices.

Optional data ranges use local calendar days, inclusive on both ends, applied
independently to patient creation, visit date, invoice issue date and payment
date. Patient aggregate counts/totals use those respective ranged records;
Visits/Invoices/Payments sheets can include an older patient's name/record
number even if that patient was created outside the selected range.

Annual performance counts all visits in the selected year. Issued count/gross
exclude void invoices and use those visits. Collections use payment dates in
the year, including earlier-year invoices. Carry-in detail rows show zero
annual TotalRp and BalanceRp and label the receipt as a carry-in payment, so
detail columns reconcile to summary gross/collections. Outstanding for the
year's invoices is max(total minus payments in the year, 0), per invoice,
summed. Later-year payments do not reduce this annual figure. The TOTAL row
sums numeric columns, including AvgInvoiceRp, as the plan explicitly requests.
Summary omits inactive operators; selecting an inactive operator still creates
its empty detail sheet. Dates and rupiah are native Excel cell types; money
keeps the cents/100 numeric value and uses `#,##0` display formatting.

Existing on-screen Reports/dashboard revenue is unchanged: fully paid invoices
grouped by issue date. It measures a different quantity than annual collections.

## Security findings

The new two-key regression (`--key-isolation` on the smoke runner) demonstrated
an existing live-session password-proof bug: a keyed SQLite handle pooled only
by database path ignored a subsequent PRAGMA key. Disabling pooling makes each
open validate its own key and releases key-bearing handles when closed.
The regression fails before the fix and passes after it.

WinUI ContentDialog popups survive hiding pages and even replacing the content
root. The native lock test failed with both approaches. The production lock
transition now constructs an unlock window and closes the old native window,
which destroys the old popup layer. Bounds and minimized/maximized state are
preserved. Dialog workflows are abandoned, not resumed after unlocking.
The shared dialog helper also tracks handled input on popup routes and returns
None for a dialog whose session ended.

Lock revokes the old Database wrapper (including key-copy via ForPath), clears
all AppSession service properties, and clears SQLite pools. New encrypted
connections do not pool. A background read already in progress may complete;
the export handler checks the original session before writing bytes or showing
completion UI. Native file-picker windows are external Windows UI, so input
there is not counted as in-app activity.

Core has both portable net10.0 and Windows targets. PasswordVault is compiled
only into the Windows target; portable callers safely report unavailable.
Only the two non-secret flags go in app.json, outside encrypted SettingKeys.

API references used during implementation:
[Microsoft SQLite connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings),
[Microsoft Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker),
[ClosedXML documentation](https://docs.closedxml.io/en/latest/).
