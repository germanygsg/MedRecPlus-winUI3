using System.Collections.ObjectModel;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App.Views;

public sealed partial class SearchResultsPage : Page
{
    private const int PageSize = 25;
    private readonly ObservableCollection<ResultGroup> _groups = [];
    private CancellationTokenSource? _cancellation;
    private string _query = "";
    internal string Query => _query;

    public sealed record ResultGroup(string Name, int Total, IReadOnlyList<SearchItem> Items,
        SearchCategory? Category = null)
    {
        public string Heading => $"{Name} ({Total})";
        public Visibility MoreVisibility => Category != null && Items.Count < Total
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public SearchResultsPage()
    {
        InitializeComponent();
        ResultGroups.ItemsSource = _groups;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _query = (e.Parameter as string ?? "").Trim();
        _ = SearchAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task SearchAsync()
    {
        _cancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var token = cancellation.Token;
        var query = _query;
        _groups.Clear();
        RetryButton.Visibility = Visibility.Collapsed;
        if (query.Length == 0)
        {
            SummaryText.Text = "Enter a name, record number, phone, or invoice number.";
            SetBusy(false);
            _cancellation = null;
            return;
        }

        SetBusy(true);
        SummaryText.Text = $"Searching for “{query}”…";
        try
        {
            var search = AppSession.Search;
            var groups = await Task.Run(() => search.Search(query, PageSize, token), token);
            if (token.IsCancellationRequested) return;
            foreach (var group in groups.Where(group => group.Total > 0))
                _groups.Add(new ResultGroup(group.Category.ToString(), group.Total,
                    group.Entries.Select(entry => SearchUi.FromEntry(entry)).ToArray(), group.Category));
            var sections = SearchUi.FindSections(query).ToArray();
            if (sections.Length > 0) _groups.Add(new ResultGroup("Sections", sections.Length, sections));
            var count = groups.Sum(group => group.Total) + sections.Length;
            SummaryText.Text = count == 0 ? $"No matches for “{query}”. Try another name or number."
                : $"{count} result{(count == 1 ? "" : "s")} for “{query}”";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                SummaryText.Text = "Search is unavailable. Please try again.";
                RetryButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        SearchProgress.IsActive = busy;
        SearchProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void RefreshSearch() => _ = SearchAsync();

    private async void Retry_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void Result_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchItem item) SearchUi.Open(item);
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ResultGroup group || group.Category is not { } category) return;
        button.IsEnabled = false;
        var query = _query;
        try
        {
            var search = AppSession.Search;
            var page = await Task.Run(() => search.SearchCategoryPage(query, category, PageSize, group.Items.Count));
            // A fresh search replaces the groups, so a stale page cannot append to it.
            var index = _groups.IndexOf(group);
            if (_query != query || index < 0 || !ReferenceEquals(Frame?.Content, this)) return;
            _groups[index] = group with
            {
                Total = page.Total,
                Items = group.Items.Concat(page.Entries.Select(entry => SearchUi.FromEntry(entry))).ToArray(),
            };
        }
        catch (Exception)
        {
            if (_query == query && _groups.Contains(group) && ReferenceEquals(Frame?.Content, this))
                SummaryText.Text = "Could not load more results. Please try again.";
        }
        finally { button.IsEnabled = true; }
    }
}
