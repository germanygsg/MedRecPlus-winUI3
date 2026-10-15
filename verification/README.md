# Native app checks

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

Set the printer's custom paper to **width 120 mm, height 138 mm**, portrait.
The app exposes paper selection and flags a different driver media size in the
print dialog. WinUI's standard print options obtain custom dimensions from the
printer driver, so the printer must provide this custom paper format.
