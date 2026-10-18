// Compiled only into a temporary app copy by Build-NavigationChecks.ps1.
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Text.Json;
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
    private readonly List<string> _navigationChecks = [];
    private void CheckNavigation(bool passed, string label) =>
        _navigationChecks.Add((passed ? "PASS " : "FAIL ") + label);
    private static IEnumerable<DependencyObject> NavigationNodes(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in NavigationNodes(VisualTreeHelper.GetChild(root, i))) yield return node;
    }
    private static bool Shown(FrameworkElement element, DependencyObject root)
    {
        for (DependencyObject? current = element; current != null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement part && (part.Visibility != Visibility.Visible || part.Opacity == 0)) return false;
        return element.ActualWidth > 1 && element.ActualHeight > 1;
    }
    private static bool InsideControlText(DependencyObject element, DependencyObject root)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null && parent != root; parent = VisualTreeHelper.GetParent(parent))
            if (parent is TextBox or PasswordBox or ComboBox or NumberBox or Button) return true;
        return false;
    }
    private void CheckPageBounds(FrameworkElement page, string label)
    {
        var issues = new List<string>();
        var nodes = NavigationNodes(page).OfType<FrameworkElement>().Where(e => Shown(e, page)).ToArray();
        foreach (var element in nodes.Where(e => e is Button or TextBox or PasswordBox or ComboBox or NumberBox
            || e is TextBlock && !InsideControlText(e, page)))
        {
            // Printed pages intentionally keep paper measurements; their preview transform scales them to fit.
            var rect = element.TransformToVisual(page).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (rect.Right < 0 || rect.Bottom < -5000) continue; // off-screen print canvas
            if (rect.Left < -1 || rect.Right > page.ActualWidth + 1)
                issues.Add($"{element.GetType().Name}/{element.Name}/{(element as TextBlock)?.Text}: x={rect.Left:F1}..{rect.Right:F1} of {page.ActualWidth:F1}");
        }
        foreach (var panel in nodes.Where(e => e is AdaptiveGrid or WrapPanel).Cast<Panel>())
        {
            var children = panel.Children.OfType<FrameworkElement>().Where(e => Shown(e, panel)).ToArray();
            for (var i = 0; i < children.Length; i++)
            {
                var a = children[i].TransformToVisual(panel).TransformBounds(new Windows.Foundation.Rect(0, 0, children[i].ActualWidth, children[i].ActualHeight));
                if (a.Right > panel.ActualWidth + 1) issues.Add("child outside responsive panel");
                for (var j = i + 1; j < children.Length; j++)
                {
                    var b = children[j].TransformToVisual(panel).TransformBounds(new Windows.Foundation.Rect(0, 0, children[j].ActualWidth, children[j].ActualHeight));
                    if (Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 1 && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 1)
                        issues.Add("overlapping responsive cells");
                }
            }
        }
        CheckNavigation(issues.Count == 0, label + ": " + (issues.Count == 0 ? "all fields, text and actions fit" : string.Join("; ", issues.Distinct().Take(8))));
    }
    public async void RunNavigationChecks()
    {
        try
        {
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "navigation-sample-data");
            AppData.EnsureCreated();
            var db = new Database(Path.Combine(AppData.BaseDir, Guid.NewGuid() + ".db"), new string('c', 64));
            db.Initialize();
            AppSession.Start(db, "navigation checks");
            var patientId = AppSession.Patients.Create(new Patient
            {
                Name = "Alexandria Sample Patient With A Long Family Name", RecordNumber = "NAV-2026-00000001", Age = 23,
                PhoneNumber = "0123456789012345", Address = new string('A', 110), InitialDiagnosis = "Long initial diagnosis with follow-up instructions and additional clinical context.",
                ConsentBasis = "public_health", ConsentRecordedBy = "Sample clinician", ConsentDate = DateTime.UtcNow.ToString("o"),
                IcdCodesJson = JsonSerializer.Serialize(new[] { new IcdCode { Code = "AA00.01", Title = "Sample diagnosis" }, new IcdCode { Code = "BB00.02", Title = "Another diagnosis" } }),
            });
            var operatorId = AppSession.Operators.Create("Sample clinician with a long display name", "Physiotherapist");
            AppSession.Treatments.Create("Long treatment name for consultation and follow-up therapy", "", 12345678900, null);
            AppSession.CustomExams.Create("Long custom examination name", "%");
            AppSession.Settings.Set(SettingKeys.ClinicProfile, new ClinicProfile { Name = "Sample clinic with a long display name", HeaderText = "Sample clinic", FooterText = "Thank you for visiting." });
            var visitId = AppSession.Visits.CreateVisit(patientId, operatorId,
                new Dictionary<string, object?> { ["bloodPressure"] = "120/80", ["heartRate"] = 70 },
                [new TreatmentItem { Name = "Long treatment name for consultation and follow-up therapy", PriceCents = 12345678900 }],
                DiscountTypes.None, 0, "Long visit notes for checking text wrapping and responsive layouts.", null, null, AppSession.Actor);
            var invoice = AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
            AppSession.Invoices.RecordPayment(invoice.Id, invoice.TotalCents, PaymentMethods.Cash, "Sample payment", AppSession.Actor);
            var attachmentPath = Path.Combine(AppData.BaseDir, "long-sample-attachment-name-for-responsive-layout-checks.png");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"), attachmentPath, true);
            AppSession.Attachments.Add("patient", patientId, attachmentPath, null, null, AppSession.Actor);
            EnterShell();
            ShellRoot.RequestedTheme = ElementTheme.Dark;
            // RenderTargetBitmap cannot capture the system Mica backdrop.
            ShellRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
            NavigateTo(typeof(PatientsPage));
            await Task.Delay(200);
            var destinations = _pages.Values.Select(type => (Type: type, Parameter: (object?)null)).Concat(new (Type Type, object? Parameter)[]
            {
                (typeof(PatientDetailPage), (object?)patientId), (typeof(VisitDetailPage), (object?)visitId),
                (typeof(InvoiceDetailPage), (object?)invoice.Id), (typeof(SearchResultsPage), (object?)"Alexandria"),
            }).ToArray();
            foreach (var width in new[] { 640, 900, 1280 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * ShellRoot.XamlRoot.RasterizationScale), 800));
                await Task.Delay(150);
                foreach (var open in new[] { true, false })
                {
                    if (NavView.IsPaneOpen != open) SidebarToggle_Click(SidebarToggleButton, new RoutedEventArgs());
                    await Task.Delay(400);
                    var split = NavigationNodes(NavView).OfType<SplitView>().Single();
                    var pane = (FrameworkElement)split.Pane;
                    var paneX = pane.TransformToVisual(ShellRoot).TransformPoint(new Windows.Foundation.Point()).X;
                    var contentX = ContentFrame.TransformToVisual(ShellRoot).TransformPoint(new Windows.Foundation.Point()).X;
                    CheckNavigation(split.DisplayMode is SplitViewDisplayMode.Inline or SplitViewDisplayMode.CompactInline
                        && contentX >= (open ? paneX + pane.ActualWidth : NavView.CompactPaneLength) - 1,
                        $"{width}/{open}: {split.DisplayMode} reserves sidebar space (content x={contentX:F1})");
                    foreach (var destination in destinations)
                    {
                        NavigateTo(destination.Type, destination.Parameter);
                        await Task.Delay(320);
                        var page = (FrameworkElement)ContentFrame.Content;
                        if (page is AiInsightsPage)
                        {
                            typeof(AiInsightsPage).GetMethod("Ask", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, ["Give me a clinic overview"]);
                            foreach (var expander in NavigationNodes(page).OfType<Expander>()) expander.IsExpanded = true;
                            await Task.Delay(80);
                        }
                        CheckNavigation(NavView.IsPaneOpen == open, $"{destination.Type.Name}: navigation preserves sidebar state");
                        CheckPageBounds(page, $"{destination.Type.Name}/{width}/{open}");
                        if (width == 640 && open && page is PatientsPage or PatientDetailPage or DashboardPage or SettingsPage)
                            await SaveNavigationVisual($"responsive-{destination.Type.Name}-{width}.png");
                    }
                }
            }
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                ShellRoot.RequestedTheme = theme; NavView.IsPaneOpen = true;
                ShellRoot.Background = new SolidColorBrush(theme == ElementTheme.Light ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(640 * ShellRoot.XamlRoot.RasterizationScale), 600));
                NavigateTo(typeof(PatientDetailPage), patientId); await Task.Delay(150);
                CheckPageBounds((FrameworkElement)ContentFrame.Content, $"minimum/{theme}");
                await SaveNavigationVisual($"navigation-{theme}-640.png");
            }
            CheckNavigation(Title == "MedRecPlus" && AppTitleBar.Title == "MedRecPlus", "window and title-bar branding use MedRecPlus");
            var icon = (Image)SidebarToggleButton.Content;
            CheckNavigation(icon.Source is BitmapImage bitmap && bitmap.PixelWidth > 0 && AppTitleBar.IsPaneToggleButtonVisible == false,
                "the app icon is loaded on the sidebar button and the hamburger is hidden");
            var scale = ShellRoot.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(640 * scale), (int)(480 * scale)));
            await Task.Delay(200);
            NavView.Visibility = Visibility.Collapsed;
            AuthFrame.Visibility = Visibility.Visible;
            foreach (var type in new[] { typeof(SetupPage), typeof(UnlockPage) })
            {
                AuthFrame.Navigate(type); await Task.Delay(150);
                var page = (Page)AuthFrame.Content;
                var method = type == typeof(SetupPage) ? "Setup_Click" : "Unlock_Click";
                type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, new RoutedEventArgs()]);
                await Task.Delay(100);
                CheckPageBounds(page, $"{type.Name}/minimum with validation messages");
            }
            AuthFrame.Visibility = Visibility.Collapsed;
            NavView.Visibility = Visibility.Visible;
            foreach (var dialog in new ContentDialog[]
            {
                new PatientDialog(), new VisitDialog(), new TreatmentDialog(),
                new PaymentDialog(invoice), new ErasePatientDialog(AppSession.Patients.GetById(patientId)!),
            })
            {
                dialog.XamlRoot = ShellRoot.XamlRoot;
                var showing = dialog.ShowAsync(); await Task.Delay(180);
                CheckPageBounds((FrameworkElement)dialog.Content, $"{dialog.GetType().Name}/minimum");
                var card = NavigationNodes(dialog).OfType<FrameworkElement>().First(element => element.Name == "BackgroundElement");
                var bounds = card.TransformToVisual(ShellRoot).TransformBounds(new Windows.Foundation.Rect(0, 0, card.ActualWidth, card.ActualHeight));
                CheckNavigation(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= ShellRoot.ActualWidth + 1 && bounds.Bottom <= ShellRoot.ActualHeight + 1,
                    $"{dialog.GetType().Name}: dialog card and footer stay within the minimum window");
                dialog.Hide(); await showing;
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(200, 200)); await Task.Delay(100);
            CheckNavigation(AppWindow.Size.Width >= 640 * scale - 1 && AppWindow.Size.Height >= 480 * scale - 1,
                "minimum window dimensions remain usable at the current display scale");
        }
        catch (Exception ex) { _navigationChecks.Add("FAIL " + ex); }
        finally
        {
            _navigationChecks.Add(_navigationChecks.Any(line => line.StartsWith("FAIL")) ? "NAVIGATION_RESULT=FAIL" : "NAVIGATION_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "navigation-check-results.txt"), _navigationChecks);
            Application.Current.Exit();
        }
    }
    private async Task SaveNavigationVisual(string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(ShellRoot);
        var pixels = await bitmap.GetPixelsAsync();
        var path = Path.Combine(AppContext.BaseDirectory, name); File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}
