using Dapper;
using MedRecPlus.Core.Services;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

internal static class RegressionChecks
{
    public static void RunSearch(string directory, Action<bool, string> check)
    {
        var database = new Database(Path.Combine(directory, "global-search.db"), new string('c', 64));
        database.Initialize();
        var patients = new PatientRepository(database);
        var appointments = new AppointmentRepository(database);
        var invoices = new InvoiceRepository(database);
        var search = new GlobalSearchService(database);
        var operatorId = new OperatorRepository(database).Create("Dr. Search", "Doctor");
        var johnId = patients.Create(new Patient { Name = "John Smith", RecordNumber = "PT-JOHN-01", PhoneNumber = "08123456789" });
        var otherJohnId = patients.Create(new Patient { Name = "John Stone", RecordNumber = "PT-STONE-02" });
        patients.Create(new Patient { Name = @"O'Neil 100% a_b a\b", RecordNumber = "PT-LITERAL" });
        var visitIds = new List<long>();
        var invoiceIds = new List<long>();
        for (var index = 0; index < 7; index++)
        {
            var patientId = index == 6 ? otherJohnId : johnId;
            var name = index == 6 ? "John Stone" : "John Smith";
            // Older records must be searchable regardless of list-page date filters.
            var visitId = appointments.Create(new Appointment
            {
                PatientId = patientId, PatientName = name, OperatorId = operatorId,
                OperatorName = "Dr. Search", Date = "2001-01-01T12:00:00Z", Description = "Follow-up",
            });
            visitIds.Add(visitId);
            invoiceIds.Add(invoices.Create(new Invoice
            {
                InvoiceNumber = $"INV-SEARCH-{index:D4}", AppointmentId = visitId,
                PatientId = patientId, PatientName = name, OperatorId = operatorId,
                OperatorName = "Dr. Search", Date = "2001-01-02T12:00:00Z", AppointmentDate = "2001-01-01T12:00:00Z",
            }));
        }

        var groups = search.Search("  jOhN  ");
        check(groups.Select(group => group.Category).SequenceEqual(Enum.GetValues<SearchCategory>()),
            "global search categorizes patients, appointments, and invoices");
        check(groups[0].Total == 2 && groups[1].Total == 7 && groups[2].Total == 7,
            "patient-name search finds related records across all dates");
        check(groups[1].Entries.Count == 5 && groups[2].Entries.Count == 5,
            "suggestions are limited independently in each category with full totals");
        check(groups[0].Entries.Select(entry => entry.Id).ToHashSet().SetEquals(new[] { johnId, otherJohnId }),
            "same-name patients retain distinct navigation identities");
        check(groups[1].Entries.All(entry => entry.Category == SearchCategory.Appointments && appointments.GetById(entry.Id) != null)
            && groups[2].Entries.All(entry => entry.Category == SearchCategory.Invoices && invoices.GetById(entry.Id) != null),
            "search entries carry the category and actual detail-page record ID");
        var rest = search.SearchCategoryPage("John", SearchCategory.Appointments, 5, 5);
        check(rest.Entries.Count == 2 && !rest.Entries.Select(entry => entry.Id).Intersect(groups[1].Entries.Select(entry => entry.Id)).Any(),
            "load more exposes remaining results without duplicate rows on equal dates");
        check(!search.SearchCategoryPage("John", SearchCategory.Invoices, 5, 10).Entries.Any(),
            "search page beyond the end is empty");
        check(search.Search("Smith John")[0].Entries.Single().Id == johnId,
            "multiword search matches every term regardless of word order");
        check(search.Search("08123456789").Select(group => group.Total).SequenceEqual(new[] { 1, 6, 6 }),
            "phone search finds the patient and related records");
        check(search.Search("PT-STONE-02").Select(group => group.Total).SequenceEqual(new[] { 1, 1, 1 }),
            "record-number search follows the correct patient relationship");
        check(search.Search("INV-SEARCH-0002")[2].Entries.Single().Id == invoiceIds[2],
            "invoice-number search opens the matching invoice identity");
        check(search.Search("unpaid")[2].Total == 7 && search.Search("Follow-up")[1].Total == 7,
            "search includes invoice status and appointment description");
        foreach (var literal in new[] { "100%", "a_b", @"a\b", "O'Neil" })
            check(search.Search(literal)[0].Entries.Single().Title.StartsWith("O'Neil"),
                $"search safely treats '{literal}' as literal text");
        check(search.Search("' OR 1=1 --").All(group => group.Total == 0), "search input cannot expand SQL filters");
        check(search.Search("missing-person").All(group => group.Total == 0), "no-match search reports zero in each category");
        check(search.Search(" \t ").Count == 0, "blank search does not enumerate patient records");

        var john = patients.GetById(johnId)!;
        john.Name = "Renamed Patient";
        patients.Update(john);
        check(search.Search("Renamed").Select(group => group.Total).SequenceEqual(new[] { 1, 6, 6 }),
            "renamed patient's current name finds old appointments and invoices");
        check(search.Search("Smith").Select(group => group.Total).SequenceEqual(new[] { 0, 6, 6 }),
            "saved patient-name snapshots remain searchable");
        appointments.SoftDelete(visitIds[0]);
        invoices.SoftDelete(invoiceIds[0]);
        check(search.Search("Smith").Select(group => group.Total).SequenceEqual(new[] { 0, 5, 5 }),
            "deleted appointments and invoices are excluded");
        patients.SoftDelete(otherJohnId);
        check(search.Search("Stone").All(group => group.Total == 0),
            "deleted patients and their related records are excluded");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            search.Search("John", cancellationToken: cancelled.Token);
            check(false, "cancelled search stops before reading results");
        }
        catch (OperationCanceledException) { check(true, "cancelled search stops before reading results"); }
    }

    public static void RunIcd(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "icd-fixture.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            connection.Execute("""
                CREATE TABLE icd_entities (
                    code TEXT, title TEXT, linearization TEXT, chapter TEXT,
                    is_leaf INTEGER, entity_uri TEXT, foundation_uri TEXT);
                INSERT INTO icd_entities VALUES
                    ('AB12', '<b>Stroke</b>', 'mms', '01', 1, NULL, NULL),
                    ('CD34', 'Stroke follow-up', 'mms', '01', 1, NULL, NULL),
                    ('EF56', 'Late stroke', 'mms', '01', 1, NULL, NULL),
                    ('GH78', 'Literal 10% value', 'mms', '01', 1, NULL, NULL),
                    ('IJ90', 'Literal a_b value', 'mms', '01', 1, NULL, NULL),
                    ('KL12', 'Literal a\b value', 'mms', '01', 1, NULL, NULL),
                    ('ICF1', 'Stroke functioning', 'icf', '01', 1, NULL, NULL);
                """);
        }

        var service = new IcdService(path);
        try
        {
            var results = service.Search("stroke").ToList();
            check(results.Count == 3, "ICD search returns matching diagnoses without throwing");
            check(results[0].Code == "CD34", "ICD title prefix ranks before substring matches");
            check(results.Any(code => code.Title == "Stroke"), "ICD search strips HTML titles");
            check(service.Search("AB12").First().Code == "AB12", "ICD exact code search");
            check(service.Search("10%").Single().Code == "GH78", "ICD percent is treated literally");
            check(service.Search("a_b").Single().Code == "IJ90", "ICD underscore is treated literally");
            check(service.Search(@"a\b").Single().Code == "KL12", "ICD backslash is treated literally");
            check(service.Search("stroke", "icf").Single().Type == "icf", "ICD search respects linearization");
            check(service.Search("stroke", limit: 1).Count() == 1, "ICD result limit");
            check(!service.Search("s").Any(), "ICD short queries do not search");
            check(!service.Search("no such diagnosis").Any(), "ICD no-match search is empty");
            check(service.Lookup("AB12")?.Title == "Stroke", "ICD lookup still works");
            var bundled = new IcdService(Path.Combine(AppContext.BaseDirectory, "icd11.db"));
            check(bundled.IsAvailable, "bundled ICD dictionary is present");
            check(bundled.Search("stroke").Any(), "bundled ICD stroke search");
            check(bundled.Search("diabetes").Any(), "bundled ICD diabetes search");
        }
        catch (Exception exception)
        {
            check(false, $"ICD search: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void RunRupiah(string directory, Action<bool, string> check)
    {
        foreach (var input in new[] { "300000", "300.000", "300,000", "Rp 300.000", "rp 300000", " 300000 " })
            check(RupiahAmount.TryParseCents(input, out var cents) && cents == 30_000_000,
                $"rupiah input '{input}' converts exactly to stored cents");
        check(RupiahAmount.TryParseCents("1", out var minimum) && minimum == 100, "minimum Rp 1 stores 100 cents");
        foreach (var input in new[] { "", "0", "-100", "3000.00", "3,00", "300.000,00", "1e3", "abc", long.MaxValue.ToString() })
            check(!RupiahAmount.TryParseCents(input, out _), $"invalid or overflowing rupiah input '{input}' is rejected");

        var database = new MedRecPlus.Core.Data.Database(Path.Combine(directory, "rupiah-test.db"), new string('b', 64));
        database.Initialize();
        var treatments = new MedRecPlus.Core.Data.TreatmentRepository(database);
        RupiahAmount.TryParseCents("300000", out var priceCents);
        var id = treatments.Create("Price regression", null, priceCents, null);
        var saved = treatments.GetAll().Single(treatment => treatment.Id == id);
        check(saved.PriceCents == 30_000_000 && saved.PriceCents / 100m == 300000m,
            "treatment price survives parse, database save, and rupiah conversion");
    }
}
