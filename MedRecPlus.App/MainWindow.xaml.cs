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

    public sealed record SectionDestination(string Tag, string Title, string Keywords);

    private readonly SectionDestination[] _destinations =
    [
        new("Dashboard", "Dashboard", "home overview clinic revenue recent"),
        new("Patients", "Patients", "patient records people contacts"),
        new("Appointments", "Appointments", "visits appointment calendar schedule"),
        new("Invoices", "Invoices", "invoice billing receipt payments unpaid"),
        new("Reports", "Reports", "report performance operator history"),
        new("AiInsights", "AI Insights", "analytics insights questions revenue"),
        new("Settings", "Settings", "clinic profile tax operators treatments examinations backup password"),
    ];

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
        Title = "MedRecPlus Offline";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
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
        AppTitleBar.IsBackButtonVisible = true;
        AppTitleBar.IsPaneToggleButtonVisible = true;
        ShellSearchBox.Visibility = Visibility.Visible;
        NavigateTo(typeof(DashboardPage));
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args) => GoBack();

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

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
        if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
            NavView.IsPaneOpen = false;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _currentParameter = e.Parameter;
        AppTitleBar.IsBackButtonEnabled = ContentFrame.CanGoBack;
        var tag = e.SourcePageType == typeof(PatientDetailPage) ? "Patients"
            : e.SourcePageType == typeof(VisitDetailPage) ? "Appointments"
            : e.SourcePageType == typeof(InvoiceDetailPage) ? "Invoices"
            : _pages.FirstOrDefault(entry => entry.Value == e.SourcePageType).Key;

        SectionTitle.Text = e.SourcePageType == typeof(PatientDetailPage) ? "Patient details"
            : e.SourcePageType == typeof(VisitDetailPage) ? "Visit details"
            : e.SourcePageType == typeof(InvoiceDetailPage) ? "Invoice details"
            : _destinations.FirstOrDefault(section => section.Tag == tag)?.Title ?? "MedRecPlus";

        _syncingSelection = true;
        try
        {
            NavView.SelectedItem = NavView.MenuItems.Concat(NavView.FooterMenuItems)
                .OfType<NavigationViewItem>().FirstOrDefault(item => (item.Tag as string) == tag);
        }
        finally { _syncingSelection = false; }
    }

    private IEnumerable<SectionDestination> FindSections(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _destinations.Where(section => words.All(word =>
            $"{section.Title} {section.Keywords}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(section => section.Title.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void ShellSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = FindSections(sender.Text).ToArray();
    }

    private void ShellSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var destination = args.ChosenSuggestion as SectionDestination;
        if (destination == null && !string.IsNullOrWhiteSpace(args.QueryText))
            destination = FindSections(args.QueryText).FirstOrDefault();
        if (destination == null)
            return;

        NavigateTo(_pages[destination.Tag]);
        sender.Text = "";
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
