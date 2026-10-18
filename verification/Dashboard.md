Run `./verification/Build-DashboardChecks.ps1` from `src` on Windows.

The script builds an isolated app copy with encrypted sample databases and
exits after saving `dashboard-check-results.txt` and dashboard PNGs beside
the copied executable. It never opens the clinic database.

Checks cover zero-filled daily series, local date boundaries, 7/30/90-day
filters, more than 1,000 records, soft deletions, paid invoice revenue,
partial-payment balances, patient quick access, creation dialogs, and native
light/dark layouts at 640, 900 and 1280 pixels. Inspect the generated images
for chart readability and card spacing.

Dashboard revenue follows Reports: sum of `paid_cents` on fully paid,
non-deleted invoices, grouped by invoice issue date. It is not a payment-date
cash-flow report. Periods include today and use the PC's local calendar days.
Quick access shows up to four patients registered or seen in the chosen
period; its visit badges count all their non-deleted visits.
