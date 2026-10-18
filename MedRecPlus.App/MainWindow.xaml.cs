using MedRecPlus.App.Views;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace MedRecPlus.App;

public sealed partial class MainWindow : Window
{
    private bool _syncingSelection;
    private object? _currentParameter;
    private CancellationTokenSource? _searchCancellation;

    private readonly Dictionary<string, Type> _pages = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Patients"] = typeof(PatientsPage),
        ["Appointments"] = typeof(AppointmentsPage),
        ["Invoices"] = typeof(InvoicesPage),
        ["Reports"] = typeof(ReportsPage),
        ["AiInsights"] = typeof(AiInsightsPage),
        ["Settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        this.InitializeComponent();
        Title = "MedRecPlus";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        NavView.PaneOpened += (_, _) => UpdateSidebarToggle();
        NavView.PaneClosed += (_, _) => UpdateSidebarToggle();
        ShellRoot.Loaded += (_, _) =>
        {
            UpdateMinimumSize();
            ShellRoot.XamlRoot.Changed += (_, _) => UpdateMinimumSize();
        };
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        // Title-bar / taskbar icon (unpackaged app).
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow?.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, @"Assets\appicon.ico"));
        }
        catch { }
    }

    /// <summary>Called by App after single-instance check: routes to setup,
    /// unlock, or straight into the shell.</summary>
    public void Start()
    {
        if (AppData.IsFirstRun)
            AuthFrame.Navigate(typeof(SetupPage));
        else
            AuthFrame.Navigate(typeof(UnlockPage));
    }

    /// <summary>Called by SetupPage/UnlockPage on success.</summary>
    public void EnterShell()
    {
        AuthFrame.Visibility = Visibility.Collapsed;
        NavView.Visibility = Visibility.Visible;
        AppTitleBar.IsBackButtonVisible = false;
        SidebarToggleButton.Visibility = Visibility.Visible;
        ShellSearchHost.Visibility = Visibility.Visible;
        ShellSearchBox.Visibility = Visibility.Visible;
        NavigateTo(typeof(DashboardPage));
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args) => GoBack();

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => ToggleSidebar();
    private void SidebarToggle_Click(object sender, RoutedEventArgs args) => ToggleSidebar();
    private void ToggleSidebar()
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        UpdateSidebarToggle();
    }
    private void UpdateSidebarToggle()
    {
        var label = NavView.IsPaneOpen ? "Collapse sidebar" : "Expand sidebar";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SidebarToggleButton, label);
        ToolTipService.SetToolTip(SidebarToggleButton, label);
    }
    private void UpdateMinimumSize()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            var scale = ShellRoot.XamlRoot?.RasterizationScale ?? 1;
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(640 * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(480 * scale);
        }
    }

    private void GoBack()
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_syncingSelection && args.SelectedItemContainer is NavigationViewItem item
            && item.Tag is string tag
            && _pages.TryGetValue(tag, out var pageType))
        {
            NavigateTo(pageType);
        }
    }

    /// <summary>Navigate the content frame to a page from anywhere (e.g. drill-down).</summary>
    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (ContentFrame.CurrentSourcePageType == pageType && Equals(_currentParameter, parameter))
            return;

        ContentFrame.Navigate(pageType, parameter);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _currentParameter = e.Parameter;
        _searchCancellation?.Cancel();
        ShellSearchBox.Text = e.SourcePageType == typeof(SearchResultsPage)
            ? (e.Parameter as string ?? "").Trim() : "";
        ShellSearchBox.ItemsSource = null;
        ShellSearchBox.IsSuggestionListOpen = false;
        AppTitleBar.IsBackButtonVisible = e.SourcePageType != typeof(DashboardPage);
        AppTitleBar.IsBackButtonEnabled = ContentFrame.CanGoBack;
        var tag = e.SourcePageType == typeof(PatientDetailPage) ? "Patients"
            : e.SourcePageType == typeof(VisitDetailPage) ? "Appointments"
            : e.SourcePageType == typeof(InvoiceDetailPage) ? "Invoices"
            : _pages.FirstOrDefault(entry => entry.Value == e.SourcePageType).Key;

        // Sidebar pages have no in-page title (the sidebar labels them); a nulled header
        // removes the whole header band. Drill-down pages show their contextual title.
        var isSidebarPage = _pages.ContainsValue(e.SourcePageType);
        SectionTitle.Text = e.SourcePageType == typeof(PatientDetailPage) ? "Patient details"
            : e.SourcePageType == typeof(VisitDetailPage) ? "Visit details"
            : e.SourcePageType == typeof(InvoiceDetailPage) ? "Invoice details"
            : e.SourcePageType == typeof(SearchResultsPage) ? "Search results"
            : SearchUi.Sections.FirstOrDefault(section => section.Tag == tag)?.Title ?? "MedRecPlus";
        NavView.Header = isSidebarPage ? null : SectionTitle;

        _syncingSelection = true;
        try
        {
            NavView.SelectedItem = NavView.MenuItems.Concat(NavView.FooterMenuItems)
                .OfType<NavigationViewItem>().FirstOrDefault(item => (item.Tag as string) == tag);
        }
        finally { _syncingSelection = false; }
    }

    private async void ShellSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen)
                _searchCancellation?.Cancel();
            return;
        }
        _searchCancellation?.Cancel();

        var query = sender.Text.Trim();
        var sections = SearchUi.FindSections(query).ToArray();
        if (query.Length == 0)
        {
            sender.ItemsSource = null;
            sender.IsSuggestionListOpen = false;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var token = cancellation.Token;
        sender.ItemsSource = new[] { new SearchItem("Searching…") };
        try
        {
            // Wait for a pause in typing, then query a separate connection off the UI thread.
            await Task.Delay(200, token);
            var search = AppSession.Search;
            var groups = await Task.Run(() => search.Search(query, cancellationToken: token), token);
            if (token.IsCancellationRequested) return;

            var items = new List<SearchItem>();
            foreach (var group in groups.Where(group => group.Total > 0))
            {
                items.AddRange(group.Entries.Select((entry, index) => SearchUi.FromEntry(entry,
                    index == 0 ? $"{group.Category} ({group.Total})" : "")));
            }
            if (sections.Length > 0)
            {
                sections[0] = sections[0] with { Heading = "Sections" };
                items.AddRange(sections);
            }
            items.Add(new SearchItem(items.Count == 0 ? "No matches found" : "View all results",
                items.Count == 0 ? "Try a name, record number, phone, or invoice number" : $"All matches for “{query}”",
                Query: query));
            sender.ItemsSource = items;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                sender.ItemsSource = sections.Append(new SearchItem("Search unavailable",
                    "Press Enter to open search results and retry", Query: query)).ToArray();
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null;
        }
    }

    private void ShellSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        SubmitSearch(sender, args.QueryText, args.ChosenSuggestion as SearchItem);

    private void ShellSearchSubmit_Click(object sender, RoutedEventArgs args) =>
        SubmitSearch(ShellSearchBox, ShellSearchBox.Text);

    private void SubmitSearch(AutoSuggestBox sender, string queryText, SearchItem? destination = null)
    {
        _searchCancellation?.Cancel();
        if (destination == null)
        {
            var query = queryText.Trim();
            if (query.Length == 0) return;
            destination = SearchUi.FindSections(query).FirstOrDefault(section =>
                section.Title.Equals(query, StringComparison.OrdinalIgnoreCase))
                ?? new SearchItem("View all results", Query: query);
        }
        if (destination.Category == null && destination.SectionTag == null && destination.Query == null) return;

        if (ContentFrame.Content is SearchResultsPage results && destination.Query == results.Query)
            results.RefreshSearch();
        else
            SearchUi.Open(destination);
        sender.Text = (ContentFrame.Content as SearchResultsPage)?.Query ?? "";
        sender.ItemsSource = null;
        sender.IsSuggestionListOpen = false;
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (NavView.Visibility != Visibility.Visible)
            return;
        ShellSearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        GoBack();
        args.Handled = true;
    }

    public void SelectNav(string tag)
    {
        if (_pages.TryGetValue(tag, out var pageType))
            NavigateTo(pageType);
    }
}
