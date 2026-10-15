// Compiled only into the temporary app copy by Build-SearchChecks.ps1.
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using MedRecPlus.App.Views;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private readonly List<string> _searchChecks = [];
    private void CheckSearch(bool ok, string name) => _searchChecks.Add((ok ? "PASS " : "FAIL ") + name);
    private SearchItem[] Suggestions => ShellSearchBox.ItemsSource is IEnumerable<SearchItem> items ? items.ToArray() : [];

    private static async Task WaitForSearch(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Search UI did not settle.");
    }

    private void TypeSearch(string query)
    {
        ShellSearchBox.Text = query;
        ShellSearchBox_TextChanged(ShellSearchBox,
            new AutoSuggestBoxTextChangedEventArgs { Reason = AutoSuggestionBoxTextChangeReason.UserInput });
    }

    private static IEnumerable<TextBlock> SearchTexts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in SearchTexts(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    public async void RunSearchChecks()
    {
        try
        {
            Title = "Search checks (sample data)";
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "sample-search-data");
            AppData.EnsureCreated();
            var database = new Database(Path.Combine(AppData.BaseDir, Guid.NewGuid() + ".db"), new string('d', 64));
            database.Initialize();
            AppSession.Start(database, "search checks");
            var patientId = AppSession.Patients.Create(new Patient { RecordNumber = "TEST-JOHN", Name = "John Smith", PhoneNumber = "08123456789" });
            AppSession.Patients.Create(new Patient { RecordNumber = "TEST-JANE", Name = "Jane Doe" });
            var operatorId = AppSession.Operators.Create("Sample doctor", "Doctor");
            for (var index = 0; index < 27; index++)
            {
                var visitId = AppSession.Visits.CreateVisit(patientId, operatorId, [],
                    [new TreatmentItem { Name = "Consultation", PriceCents = 100_000 }],
                    DiscountTypes.None, 0, null, null, null, AppSession.Actor);
                AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
            }
            EnterShell();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 900));
            await Task.Delay(200);
            ShellSearchBox.Focus(FocusState.Programmatic);
            TypeSearch("John");
            await WaitForSearch(() => Suggestions.Any(item => item.Query == "John"));
            CheckSearch(Suggestions.Where(item => item.Heading.Length > 0).Select(item => item.Heading)
                .SequenceEqual(new[] { "Patients (1)", "Appointments (27)", "Invoices (27)" }),
                "native search suggestions have three category headings with full counts");
            CheckSearch(Suggestions.Count(item => item.Category == SearchCategory.Patients) == 1
                && Suggestions.Count(item => item.Category == SearchCategory.Appointments) == 5
                && Suggestions.Count(item => item.Category == SearchCategory.Invoices) == 5,
                "dropdown keeps each record category bounded");
            ShellSearchBox.IsSuggestionListOpen = true;
            await Task.Delay(200);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(ShellRoot.XamlRoot)
                .FirstOrDefault(candidate => candidate.Child != null && SearchTexts(candidate.Child).Any(text => text.Text == "Patients (1)"));
            CheckSearch(popup != null, "category headings render in the native suggestion popup");
            if (popup?.Child is FrameworkElement dropdown) await SaveSearchVisual(dropdown, "search-suggestions.png");

            foreach (var category in Enum.GetValues<SearchCategory>())
            {
                var item = Suggestions.First(item => item.Category == category);
                SubmitSearch(ShellSearchBox, "John", item);
                CheckSearch(ContentFrame.CurrentSourcePageType == (category switch
                {
                    SearchCategory.Patients => typeof(PatientDetailPage),
                    SearchCategory.Appointments => typeof(VisitDetailPage),
                    _ => typeof(InvoiceDetailPage),
                }) && Equals(_currentParameter, item.Id), $"{category} suggestion opens its actual detail page and ID");
                TypeSearch("John");
                await WaitForSearch(() => Suggestions.Any(result => result.Query == "John"));
            }

            SubmitSearch(ShellSearchBox, "John");
            await WaitForSearch(() => ContentFrame.Content is SearchResultsPage page && SearchTexts(page).Any(text => text.Text == "55 results for “John”"));
            var results = (SearchResultsPage)ContentFrame.Content;
            CheckSearch(((AutoSuggestBox)results.FindName("ResultsSearchBox")).ActualWidth >= 280,
                "full results search field has a usable width");
            var groups = (ObservableCollection<SearchResultsPage.ResultGroup>)((ItemsControl)results.FindName("ResultGroups")).ItemsSource;
            CheckSearch(groups.Count == 3 && groups[1].Items.Count == 25 && groups[1].MoreVisibility == Visibility.Visible,
                "Enter opens grouped full results with load-more controls");
            var loadMore = typeof(SearchResultsPage).GetMethod("LoadMore_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            loadMore.Invoke(results, [new Button { Tag = groups[1] }, new RoutedEventArgs()]);
            await WaitForSearch(() => groups[1].Items.Count == 27);
            CheckSearch(groups[1].Items.Select(item => item.Id).Distinct().Count() == 27
                && groups[1].MoreVisibility == Visibility.Collapsed, "native Load more reveals every remaining appointment once");
            await Task.Delay(300);
            CheckSearch(SearchTexts(results).Any(text => text.Text == "Patients (1)")
                && SearchTexts(results).Any(text => text.Text == "Invoices (27)"), "full results render visible category headings");
            await SaveSearchVisual(ShellRoot, "search-results.png");

            var resultsBox = (AutoSuggestBox)results.FindName("ResultsSearchBox");
            resultsBox.Text = "Jane";
            typeof(SearchResultsPage).GetMethod("ResultsSearchBox_QuerySubmitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(results, [resultsBox, new AutoSuggestBoxQuerySubmittedEventArgs()]);
            await WaitForSearch(() => Equals(_currentParameter, "Jane"));
            CheckSearch(Equals(_currentParameter, "Jane"), "refining a search keeps the navigation query in sync");
            TypeSearch("John");
            TypeSearch("Jane");
            await WaitForSearch(() => Suggestions.Any(item => item.Query == "Jane"));
            await Task.Delay(250);
            CheckSearch(Suggestions.Any(item => item.Title == "Jane Doe") && !Suggestions.Any(item => item.Title == "John Smith"),
                "rapid typing never replaces newer suggestions with old results");
            TypeSearch("John");
            SubmitSearch(ShellSearchBox, "John");
            await Task.Delay(500);
            CheckSearch(ShellSearchBox.Text == "" && ShellSearchBox.ItemsSource == null && !ShellSearchBox.IsSuggestionListOpen,
                $"submitting during debounce leaves the dropdown closed and cleared (text='{ShellSearchBox.Text}', items={Suggestions.Length}, open={ShellSearchBox.IsSuggestionListOpen})");
            TypeSearch("no-such-person");
            await WaitForSearch(() => Suggestions.Any(item => item.Title == "No matches found"));
            CheckSearch(Suggestions.Single().Title == "No matches found", "native search displays an explicit no-match state");
            TypeSearch("Settings");
            await WaitForSearch(() => Suggestions.Any(item => item.SectionTag == "Settings"));
            SubmitSearch(ShellSearchBox, "Settings");
            CheckSearch(ContentFrame.Content is SettingsPage, "section search and exact-title Enter navigation still work");
        }
        catch (Exception exception)
        {
            CheckSearch(false, exception.ToString());
            _searchChecks.Add($"TRACE final text={ShellSearchBox.Text}; suggestions=" + string.Join(" | ", Suggestions.Select(item => $"{item.Title} [{item.Query}]")));
        }
        finally
        {
            _searchChecks.Add(_searchChecks.Any(line => line.StartsWith("FAIL ")) ? "SEARCH_UI_RESULT=FAIL" : "SEARCH_UI_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "search-check-results.txt"), _searchChecks);
            Close();
        }
    }

    private static async Task SaveSearchVisual(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var path = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}
