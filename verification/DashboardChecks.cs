// Runs only in an isolated app copy with an encrypted synthetic database.
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Dapper;
using MedRecPlus.App.Views;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private readonly List<string> _dashboardChecks = [];
    private void DashboardCheck(bool pass, string label) => _dashboardChecks.Add((pass ? "PASS " : "FAIL ") + label);
    private static string Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("o");

    private void CheckDashboardData(string directory)
    {
        var db = new Database(Path.Combine(directory, "aggregates.db"), new string('d', 64));
        db.Initialize();
        var repository = new DashboardRepository(db);
        DashboardCheck(repository.Trend(DashboardMetric.Revenue, 7).All(d => d.Value == 0), "empty revenue returns seven zero days");
        DashboardCheck(repository.RecentPatients(7).Count == 0 && repository.Outstanding() == (0, 0L), "empty quick access and balance");
        var today = DateTime.Today;
        using var conn = db.Open();
        using (var tx = conn.BeginTransaction())
        {
            for (var i = 0; i < 1005; i++)
                conn.Execute("INSERT INTO patients(record_number, name, age, created_at, updated_at) VALUES(@number, 'Sample patient', 30, @date, @date)",
                    new { number = $"P{i}", date = Utc(today.AddDays(-3).AddHours(12)) }, tx);
            foreach (var item in new[]
            {
                ("START", today.AddDays(-6), (string?)null),
                ("BEFORE", today.AddDays(-6).AddSeconds(-1), (string?)null),
                ("FUTURE", today.AddDays(1), (string?)null),
                ("DELETED", today, (string?)Utc(today)),
            })
                conn.Execute("INSERT INTO patients(record_number, name, age, created_at, updated_at, deleted_at) VALUES(@number, 'Boundary sample', 30, @date, @date, @deleted)",
                    new { number = item.Item1, date = Utc(item.Item2), deleted = item.Item3 }, tx);
            tx.Commit();
        }
        var patients = repository.Trend(DashboardMetric.Patients, 7);
        DashboardCheck(patients.Count == 7 && patients.Sum(p => p.Value) == 1006 && patients[0].Value == 1 && patients[3].Value == 1005,
            "patient totals exceed 1000 and honor inclusive local start, exclusive end, soft deletion and zero days");
        DashboardCheck(repository.Trend(DashboardMetric.Patients, 30).Sum(d => d.Value) == 1007 && repository.Trend(DashboardMetric.Patients, 90).Count == 90,
            "30/90-day ranges include earlier activity without future records");
        var quick = repository.RecentPatients(7);
        DashboardCheck(quick.Count == 4 && quick.Select(p => p.Id).Distinct().Count() == 4 && quick.All(p => p.RecordNumber.StartsWith("P")),
            "quick access is bounded, distinct, recent and excludes deleted/future patients");
        var operators = new OperatorRepository(db);
        var operatorId = operators.Create("Sample clinician", "Doctor");
        var visits = new AppointmentRepository(db);
        var invoices = new InvoiceRepository(db);
        foreach (var item in new[] { ("paid", 20000L, false), ("unpaid", 5000L, false), ("void", 20000L, false), ("paid", 20000L, true) })
        {
            var date = Utc(today.AddDays(-2).AddHours(1));
            var visit = visits.Create(new Appointment { PatientId = 1, PatientName = "Sample patient", OperatorId = operatorId, OperatorName = "Sample clinician", Date = date });
            var id = invoices.Create(new Invoice
            {
                InvoiceNumber = $"TEST-{visit}", AppointmentId = visit, PatientId = 1, PatientName = "Sample patient",
                OperatorId = operatorId, OperatorName = "Sample clinician", Date = date, AppointmentDate = date,
                TotalCents = 20000, PaidCents = item.Item2, Status = item.Item1,
            });
            if (item.Item3) { invoices.SoftDelete(id); visits.SoftDelete(visit); }
        }
        DashboardCheck(repository.Trend(DashboardMetric.Revenue, 7).Sum(d => d.Value) == 20000,
            "revenue counts fully paid invoices, excluding partial payments, voids and deleted invoices");
        DashboardCheck(repository.Trend(DashboardMetric.Appointments, 7).Sum(d => d.Value) == 3,
            "appointment trend excludes deleted visits");
        DashboardCheck(repository.Outstanding() == (1, 15000L), "outstanding balance subtracts partial payments");
        DashboardCheck(repository.RecentPatients(7).Single(p => p.Id == 1).Visits == 3, "quick access counts surviving visits without duplicating patients");
    }

    public async void RunDashboardChecks()
    {
        try
        {
            Title = "Dashboard checks — sample data";
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "dashboard-sample-data", Guid.NewGuid().ToString("N"));
            AppData.EnsureCreated();
            CheckDashboardData(AppData.BaseDir);
            var db = new Database(Path.Combine(AppData.BaseDir, "ui.db"), new string('e', 64));
            db.Initialize();
            AppSession.Start(db, "dashboard checks");
            AppSession.Settings.Set(SettingKeys.ClinicProfile, new ClinicProfile { Name = "MedRecPlus" });
            var op = AppSession.Operators.Create("Sample clinician", "Doctor");
            var patientIds = new List<long>();
            for (var i = 0; i < 4; i++)
            {
                var patient = AppSession.Patients.Create(new Patient { RecordNumber = $"PT2026{i + 1:D6}", Name = new[] { "Maya Putri", "Rina Wijaya", "Alexandria Sample Patient With A Long Family Name", "Budi Santoso" }[i], Age = 28 + i * 7 });
                patientIds.Add(patient);
                using var conn = db.Open();
                conn.Execute("UPDATE patients SET created_at = @date WHERE id = @patient", new { patient, date = Utc(DateTime.Today.AddDays(-i)) });
            }
            var daily = new[] { 2, 4, 3, 6, 4, 8, 5 };
            for (var day = 0; day < 7; day++)
                for (var i = 0; i < daily[day]; i++)
                {
                    var visit = AppSession.Visits.CreateVisit(patientIds[i % 4], op, [], [new TreatmentItem { Name = "Consultation", PriceCents = (i + 1) * 7500000 }], DiscountTypes.None, 0, null, null, null, AppSession.Actor);
                    var invoice = AppSession.Invoices.GenerateFromAppointment(visit, AppSession.Actor);
                    if (i != 0) AppSession.Invoices.RecordPayment(invoice.Id, invoice.TotalCents, PaymentMethods.Cash, null, AppSession.Actor);
                    using var conn = db.Open();
                    var date = Utc(DateTime.Today.AddDays(day - 6).AddHours(10));
                    conn.Execute("UPDATE appointments SET date = @date WHERE id = @visit", new { date, visit });
                    conn.Execute("UPDATE invoices SET date = @date WHERE id = @id", new { date, id = invoice.Id });
                }
            EnterShell();
            await Task.Delay(400);
            var page = (DashboardPage)ContentFrame.Content;
            DashboardCheck(((TextBlock)page.FindName("PatientsCount")).Text == "4" && ((TextBlock)page.FindName("VisitsToday")).Text == "32", "native cards show real aggregate totals");
            foreach (var name in new[] { "PatientRange", "AppointmentRange", "RevenueRange", "QuickRange" })
            {
                var range = (ComboBox)page.FindName(name);
                range.SelectedIndex = 2;
                await Task.Delay(60);
                range.SelectedIndex = 1;
                await Task.Delay(60);
                range.SelectedIndex = 0;
            }
            DashboardCheck(((ComboBox)page.FindName("RevenueRange")).SelectedIndex == 0, "all four native date filters update through 7, 30 and 90 days");
            var rangeA = (ComboBox)page.FindName("PatientRange");
            rangeA.SelectedIndex = 2;
            DashboardCheck(((ComboBox)page.FindName("AppointmentRange")).SelectedIndex == 0, "card date filters are independent");
            rangeA.SelectedIndex = 0;
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                ShellRoot.RequestedTheme = theme;
                ShellRoot.Background = new SolidColorBrush(theme == ElementTheme.Light ? Microsoft.UI.Colors.WhiteSmoke : Windows.UI.Color.FromArgb(255, 28, 28, 28));
                foreach (var width in new[] { 640, 900, 1280 })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * ShellRoot.XamlRoot.RasterizationScale), (int)(1050 * ShellRoot.XamlRoot.RasterizationScale)));
                    NavView.IsPaneOpen = width == 900;
                    await Task.Delay(300);
                    page.UpdateLayout();
                    var cards = ((AdaptiveGrid)page.FindName("BentoGrid")).Children.Cast<FrameworkElement>().ToArray();
                    var rects = cards.Select(card => card.TransformToVisual(page).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight))).ToArray();
                    var fit = rects.All(r => r.X >= 0 && r.Right <= page.ActualWidth + 1);
                    var overlap = rects.SelectMany((a, i) => rects.Skip(i + 1).Select(b => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 1 && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 1)).Any(b => b);
                    DashboardCheck(fit && !overlap, $"{theme} {width}px: bento cards fit without overlap");
                    var chart = (DashboardChart)page.FindName("PatientChart");
                    DashboardCheck(chart.ActualWidth > 100 && Descendants(chart).OfType<Microsoft.UI.Xaml.Shapes.Path>().Count() == 2, $"{theme} {width}px: native area and trend render");
                    var axisColor = ((SolidColorBrush)Descendants(chart).OfType<TextBlock>().First().Foreground).Color;
                    DashboardCheck(theme == ElementTheme.Light ? axisColor.R < 150 : axisColor.R > 150, $"{theme} {width}px: chart axes follow the page theme");
                    var outside = Descendants(page).OfType<ComboBox>().Any(c => c.TransformToVisual(page).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight)).Right > page.ActualWidth + 1);
                    DashboardCheck(!outside, $"{theme} {width}px: date pickers stay inside the viewport");
                    if (width is 640 or 1280) await SaveDashboardVisual($"dashboard-{theme}-{width}.png");
                }
            }
            ((ComboBox)page.FindName("AppointmentRange")).SelectedIndex = 2;
            await Task.Delay(150);
            DashboardCheck(Descendants((DashboardChart)page.FindName("AppointmentChart")).OfType<TextBlock>().Count() < 15, "90-day charts thin date labels to fit available width");
            await SaveDashboardVisual("dashboard-90-days.png");
            var list = (ListView)page.FindName("QuickPatients");
            list.UpdateLayout();
            var item = (ListViewItem)list.ContainerFromIndex(0);
            var itemPeer = new ListViewItemAutomationPeer(item);
            if (itemPeer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
            await Task.Delay(150);
            DashboardCheck(ContentFrame.Content is PatientDetailPage, "quick access opens the patient detail page");
            NavigateTo(typeof(DashboardPage));
            await Task.Delay(150);
            page = (DashboardPage)ContentFrame.Content;
            var method = typeof(DashboardPage).GetMethod("AddPatient_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(page, [page, new RoutedEventArgs()]);
            await Task.Delay(150);
            var dialogs = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(ShellRoot.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<PatientDialog>().ToArray();
            DashboardCheck(dialogs.Length > 0, "Add opens the patient creation dialog directly");
            foreach (var dialog in dialogs) dialog.Hide();
            await Task.Delay(100);
            typeof(DashboardPage).GetMethod("NewVisit_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [page, new RoutedEventArgs()]);
            await Task.Delay(150);
            var visitDialogs = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(ShellRoot.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<VisitDialog>().ToArray();
            DashboardCheck(visitDialogs.Length > 0, "New visit opens the visit creation dialog directly");
            foreach (var dialog in visitDialogs) dialog.Hide();
            await Task.Delay(100);
            // Check empty UI using another isolated database.
            var empty = new Database(Path.Combine(AppData.BaseDir, "empty.db"), new string('f', 64));
            empty.Initialize(); AppSession.Start(empty, "empty checks");
            NavigateTo(typeof(PatientsPage)); NavigateTo(typeof(DashboardPage));
            await Task.Delay(200);
            page = (DashboardPage)ContentFrame.Content;
            DashboardCheck(((FrameworkElement)page.FindName("QuickEmpty")).Visibility == Visibility.Visible && ((TextBlock)page.FindName("RevenueMonth")).Text == "Rp 0", "empty dashboard shows honest zero charts and patient guidance");
            await SaveDashboardVisual("dashboard-empty.png");
        }
        catch (Exception ex) { _dashboardChecks.Add("FAIL " + ex); }
        finally
        {
            _dashboardChecks.Add(_dashboardChecks.Any(r => r.StartsWith("FAIL")) ? "DASHBOARD_RESULT=FAIL" : "DASHBOARD_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "dashboard-check-results.txt"), _dashboardChecks);
            Close(); Application.Current.Exit();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private async Task SaveDashboardVisual(string name)
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
