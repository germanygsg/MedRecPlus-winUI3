using Dapper;
using MedRecPlus.Core.Services;
using Microsoft.Data.Sqlite;

internal static class RegressionChecks
{
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
