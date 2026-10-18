# Native app checks

## Column visibility and resizing

Run `./verification/Build-ColumnChecks.ps1` from `src` on Windows. It uses a
temporary app copy and a separate sample database to check each table's checkbox
picker, header/row collapse and restoration, selection preservation, pagination,
recycled rows, independent page preferences, and recovery from malformed settings.
It also checks resize handles, column width changes, minimum widths, horizontal
scrolling, and width preservation when hiding columns or paging. It launches
twice to verify saved visibility and widths after a full app restart. Results and
light/dark screenshots are written beside the copied executable.

The initial run sends Windows mouse input to each page's divider and verifies
that the header and rows follow the actual drag distance. The sample app briefly
comes forward for these checks and then restores the pointer and previous window.
Run `./verification/Build-ColumnChecks.ps1 -PointerOnly` for just this regression.

## Sidebar resizing

Run `./verification/Build-NavigationChecks.ps1` from `src` on Windows. It uses
a temporary app and long sample values to check every page at minimum (640),
medium (900) and wide (1280) window widths, with the sidebar open and closed.
Checks require an inline sidebar, preserved sidebar state, content within page
bounds, and responsive rows without overlapping cells. They also verify light/dark
mode, the app-icon toggle, and MedRecPlus branding. The copied executable's folder
contains `navigation-check-results.txt` and screenshots.

## Field validation

Run `./verification/Build-ValidationChecks.ps1` from `src` on Windows. It builds
a temporary app copy with a separate sample database. Checks submit the native
forms with missing or invalid values, correct individual fields, and verify
patient, treatment and payment saves. They also cover setup, unlock, visits,
password settings, operator and examination dialogs, plus light/dark screenshots
of patient messages beside their inputs. Results are in
`validation-check-results.txt` beside the copied executable. Real clinic data is
untouched.

## Global search

Run `./verification/Build-SearchChecks.ps1` from `src` on Windows. It builds a
temporary app copy and uses a separate sample database. It verifies categorized
suggestions, record detail navigation, full grouped results, loading additional
results, query changes, rapid typing, empty results, and section navigation.
The copied executable's folder contains `search-check-results.txt`,
`search-suggestions.png`, and `search-results.png`. The real app's data is untouched.

Core search regression checks also run with the smoke-test project, including
current/saved patient names, related records, deletion, literal search terms,
and pagination.

## Invoice printing

Run `./verification/Build-InvoiceChecks.ps1` from `src` on Windows. The script
builds a temporary copy of the app, starts it with a separate sample database,
and exits after writing `invoice-check-results.txt` and first/last page PNGs
beside the copied executable. `invoice-screen-preview.png` captures the paper
preview displayed on the invoice screen for a short sample invoice. The real
app's database and startup are untouched.

Checks exercise the native WinUI receipt layout at exactly **120 mm wide x
138 mm high**, including A4/landscape driver defaults and media-size mismatch
validation; every treatment must appear exactly once and fit within the
printable area. They also cover frozen clinic settings, monetary
totals, partial payment, short invoices, long footers, returning to an invoice,
legacy receipts and damaged profiles. Pagination must not create a print audit
event. Verify the system print dialog and physical output manually with a printer.

Screen checks compare every text block's content, wrapping width and position
with the printed pages, including larger driver margins and a narrow window.
The screen scales whole sheets to fit without reflowing their contents.

Receipt setup checks also edit the actual settings controls and verify immediate
sample preview changes, millimetre margins, all six font sizes, bundled VT323,
line spacing, ten section switches and five independent divider switches. They
save and reopen settings through a fresh encrypted database connection, reject
blank measurements, exercise reset/default recovery, and verify that new invoices
retain their issue-time layout after settings change. Invoices issued before
the designer retain their original renderer. Treatment notes are checked across
multiple pages. `receipt-setup-controls.png` and `receipt-setup-preview.png`
capture the designer and its sample receipt.

Set the printer's custom paper to **width 120 mm, height 138 mm**, portrait.
The app exposes paper selection and flags a different driver media size in the
print dialog. WinUI's standard print options obtain custom dimensions from the
printer driver, so the printer must provide this custom paper format.
