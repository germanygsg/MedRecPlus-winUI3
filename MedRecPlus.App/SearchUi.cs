using MedRecPlus.App.Views;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;

namespace MedRecPlus.App;

public sealed record SearchItem(string Title, string Detail = "", SearchCategory? Category = null,
    long Id = 0, string? SectionTag = null, string? Query = null, string Heading = "")
{
    public Visibility HeadingVisibility => Heading.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public override string ToString() => Title;
}

internal static class SearchUi
{
    internal sealed record SectionDestination(string Tag, string Title, string Keywords);

    internal static readonly SectionDestination[] Sections =
    [
        new("Dashboard", "Dashboard", "home overview clinic revenue recent"),
        new("Patients", "Patients", "patient records people contacts"),
        new("Appointments", "Appointments", "visits appointment calendar schedule"),
        new("Invoices", "Invoices", "invoice billing receipt payments unpaid"),
        new("Reports", "Reports", "report performance operator history"),
        new("AiInsights", "AI Insights", "analytics insights questions revenue"),
        new("Settings", "Settings", "clinic profile tax operators treatments examinations backup password"),
    ];

    internal static IEnumerable<SearchItem> FindSections(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Sections.Where(section => words.All(word =>
                $"{section.Title} {section.Keywords}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(section => section.Title.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(section => new SearchItem(section.Title, "Open section", SectionTag: section.Tag));
    }

    internal static SearchItem FromEntry(SearchEntry entry, string heading = "") =>
        new(entry.Title, string.Join(" · ", new[]
        {
            entry.Date == null ? "" : UiHelpers.Date(entry.Date), entry.Description,
        }.Where(part => !string.IsNullOrWhiteSpace(part))), entry.Category, entry.Id, Heading: heading);

    internal static void Open(SearchItem item)
    {
        if (item.Query != null)
            App.Shell?.NavigateTo(typeof(SearchResultsPage), item.Query);
        else if (item.SectionTag != null)
            App.Shell?.SelectNav(item.SectionTag);
        else if (item.Category is { } category)
            App.Shell?.NavigateTo(category switch
            {
                SearchCategory.Patients => typeof(PatientDetailPage),
                SearchCategory.Appointments => typeof(VisitDetailPage),
                SearchCategory.Invoices => typeof(InvoiceDetailPage),
                _ => throw new ArgumentOutOfRangeException(nameof(item)),
            }, item.Id);
    }
}
