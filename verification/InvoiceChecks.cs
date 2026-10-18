// Compiled only into the temporary app copy by Build-InvoiceChecks.ps1.
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using MedRecPlus.App.Views;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Graphics.Printing;
using Windows.Storage;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private readonly List<string> _invoiceChecks = [];
    private void CheckInvoice(bool ok, string name) => _invoiceChecks.Add((ok ? "PASS " : "FAIL ") + name);

    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Texts(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    public async void RunInvoiceChecks()
    {
        try
        {
            Title = "Invoice checks (sample data)";
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "sample-data");
            AppData.EnsureCreated();
            var database = new Database(Path.Combine(AppData.BaseDir, Guid.NewGuid() + ".db"), new string('a', 64));
            database.Initialize();
            AppSession.Start(database, "invoice checks");
            var patientId = AppSession.Patients.Create(new Patient { RecordNumber = "TEST0001", Name = "Sample patient" });
            var opId = AppSession.Operators.Create("Sample doctor", "Doctor");
            AppSession.Settings.Set(SettingKeys.ClinicProfile, new ClinicProfile
            {
                Name = "Original clinic", HeaderText = "Original clinic header", FooterText = "Original clinic footer",
            });
            AppSession.Settings.Set(SettingKeys.Currency, new CurrencyConfig { TaxType = TaxTypes.Percent, TaxValue = 11, TaxLabel = "PPN" });
            var visitId = AppSession.Visits.CreateVisit(patientId, opId, [],
                Enumerable.Range(1, 100).Select(i => new TreatmentItem
                {
                    Name = $"Treatment {i:D3}: consultation and follow-up care", PriceCents = 100_000,
                }).ToList(), DiscountTypes.Percent, 10, null, null, null, AppSession.Actor);
            var invoice = AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
            AppSession.Invoices.RecordPayment(invoice.Id, 100_000, PaymentMethods.Cash, null, AppSession.Actor);
            AppSession.Settings.Set(SettingKeys.ClinicProfile, new ClinicProfile { Name = "Changed clinic", FooterText = "Changed footer" });
            // Normal startup waits at the unlock screen. Let the initial window
            // attach and acquire the system theme before constructing shell pages.
            await Task.Delay(200);
            EnterShell();
            NavigateTo(typeof(SettingsPage));
            var initialSettings = (SettingsPage)ContentFrame.Content;
            ((Expander)initialSettings.FindName("ReceiptExpander")).IsExpanded = true;
            await Task.Delay(250);
            var initialDesigner = (ReceiptSetupControl)initialSettings.FindName("ReceiptDesigner");
            var initialLabel = ReceiptNodes<TextBlock>(initialDesigner).Single(t => t.Text == "Margins");
            var initialCard = ReceiptNodes<Border>(initialDesigner).First(b => ReceiptNodes<TextBlock>(b).Contains(initialLabel));
            var initialText = ((SolidColorBrush)initialLabel.Foreground).Color;
            var initialBackground = ((SolidColorBrush)initialCard.Background).Color;
            // Fluent card fills are translucent white in dark mode. Compare
            // their composited color, not the unblended RGB channels. Captures
            // also need a backdrop because RenderTargetBitmap omits window Mica.
            var initialBase = initialDesigner.ActualTheme == ElementTheme.Dark ? 28d : 245d;
            ShellRoot.Background = new SolidColorBrush(initialDesigner.ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 28, 28, 28) : Microsoft.UI.Colors.WhiteSmoke);
            var cardIntensity = initialBackground.R * initialBackground.A / 255d + initialBase * (255 - initialBackground.A) / 255d;
            var textIntensity = initialText.R * initialText.A / 255d + cardIntensity * (255 - initialText.A) / 255d;
            CheckInvoice(Math.Abs(textIntensity - cardIntensity) > 100,
                $"receipt designer has readable text in the default {initialDesigner.ActualTheme} system theme (card alpha {initialBackground.A})");
            await SaveVisual(ShellRoot, "receipt-setup-system-theme.png");
            NavigateTo(typeof(InvoiceDetailPage), invoice.Id);
            await Task.Delay(200);
            var view = (InvoiceDetailPage)ContentFrame.Content;
            var receipt = (InvoiceReceipt)typeof(InvoiceDetailPage).GetField("_receipt", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            CheckInvoice(receipt.Header == "Original clinic header" && receipt.Footer == "Original clinic footer", "saved clinic profile survives settings changes in preview and print");
            CheckInvoice(receipt.Discount == "Discount: −Rp 10.000" && receipt.Tax == "PPN: Rp 9.900" && receipt.Total == "TOTAL: Rp 99.900", "frozen discount, tax and total formatting");
            CheckInvoice(receipt.Paid == "Paid: Rp 1.000 · Outstanding: Rp 98.900", "partial payment and outstanding balance");
            CheckInvoice(((Button)view.FindName("PrintButton")).IsEnabled, "invoice has an enabled print button");
            CheckInvoice(!Texts(view).Any(t => t.Text.Contains("Mark void") || t.Text.Contains("Mark as void")), "void action removed from invoice UI");
            var canvas = (Canvas)view.FindName("PrintCanvas");
            using (var printer = new InvoicePrinter(this, canvas, _ => { }))
            {
                var defaultDescription = InvoicePaperLayout.DefaultPageDescription;
                var defaultPages = printer.BuildPages(receipt, defaultDescription).ToArray();
                CheckPreviewMatches(view, defaultPages, "initial preview");
                var a4 = new PrintPageDescription { PageSize = new Size(793.7, 1122.5), ImageableRect = new Rect(20, 30, 743.7, 1052.5) };
                var pages = printer.BuildPages(receipt, a4).ToArray();
                CheckInvoice(pages.All(p => Math.Abs(p.Width * 25.4 / 96 - 120) < 0.001 && Math.Abs(p.Height * 25.4 / 96 - 138) < 0.001), "receipt stays exactly 120 mm wide x 138 mm high even when driver defaults to A4");
                CheckInvoice(!InvoicePrinter.MatchesInvoicePaper(a4.PageSize), "A4 driver media is flagged as a mismatch");
                var small = new PrintPageDescription { PageSize = InvoicePrinter.PaperSize, ImageableRect = new Rect(12, 12, InvoicePrinter.PaperSize.Width - 24, InvoicePrinter.PaperSize.Height - 24) };
                var smallPages = printer.BuildPages(receipt, small).ToArray();
                CheckInvoice(smallPages.Length > 1, $"100 treatments paginate: {smallPages.Length} receipt pages");
                CheckInvoice(InvoicePrinter.MatchesInvoicePaper(small.PageSize), "120 x 138 mm driver media accepted");
                CheckInvoice(InvoicePrinter.MatchesInvoicePaper(new Size(453.5, 521.6)), "driver dimension rounding accepted");
                CheckInvoice(!InvoicePrinter.MatchesInvoicePaper(new Size(small.PageSize.Height, small.PageSize.Width)), "138 mm wide x 120 mm high is rejected");
                CheckPages(smallPages, receipt, small, "120 x 138 mm paper");
                view.RenderReceiptPreview(small);
                CheckPreviewMatches(view, smallPages, "selected printer margins");
                var inset = new PrintPageDescription
                {
                    PageSize = InvoicePrinter.PaperSize,
                    ImageableRect = new Rect(30, 35, InvoicePrinter.PaperSize.Width - 65, InvoicePrinter.PaperSize.Height - 75),
                };
                var insetPages = printer.BuildPages(receipt, inset).ToArray();
                view.RenderReceiptPreview(inset);
                CheckPreviewMatches(view, insetPages, "larger printer margins");
                AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 700));
                await Task.Delay(150);
                CheckPreviewMatches(view, insetPages, "narrow window keeps print page breaks");
                var previewHost = (StackPanel)view.FindName("ReceiptPanel");
                CheckInvoice(previewHost.Children.Cast<Viewbox>().All(sheet => sheet.ActualWidth <= previewHost.ActualWidth + 0.1), "narrow preview sheets fit the available width");
                // Rebuild after margin checks so captured print images use the small paper profile.
                smallPages = printer.BuildPages(receipt, small).ToArray();
                view.RenderReceiptPreview(small);
                await SavePage(smallPages[0], canvas, "invoice-first-page.png");
                await SavePage(smallPages[^1], canvas, "invoice-last-page.png");
                var landscape = new PrintPageDescription { PageSize = new Size(1122.5, 793.7), ImageableRect = new Rect(20, 20, 1082.5, 753.7) };
                CheckInvoice(printer.BuildPages(receipt, landscape).All(p => p.Width < p.Height && Math.Abs(p.Width - small.PageSize.Width) < 0.001), "landscape driver defaults do not rotate or enlarge receipt pages");
                var shortReceipt = receipt with { Lines = receipt.Lines.Take(1).ToArray() };
                CheckInvoice(printer.BuildPages(shortReceipt, small).Count == 1, "short invoice prints on one 120 x 138 mm page");
                var tallReceipt = shortReceipt with { Footer = string.Join('\n', Enumerable.Repeat("Long footer text", 150)) };
                CheckInvoice(printer.BuildPages(tallReceipt, small).SelectMany(Texts).Any(t => t.Text == tallReceipt.Footer), "oversized block retained in full on small paper");
            }
            NavigateTo(typeof(InvoicesPage));
            NavigateTo(typeof(InvoiceDetailPage), invoice.Id);
            await Task.Delay(100);
            view = (InvoiceDetailPage)ContentFrame.Content;
            using (var reopened = new InvoicePrinter(this, (Canvas)view.FindName("PrintCanvas"), _ => { }))
                CheckInvoice(true, "print registration can be disposed and recreated after navigating back");
            var shortVisitId = AppSession.Visits.CreateVisit(patientId, opId, [],
                [new TreatmentItem { Name = "Consultation", PriceCents = 25_000_000 }],
                DiscountTypes.None, 0, null, null, null, AppSession.Actor);
            var shortInvoice = AppSession.Invoices.GenerateFromAppointment(shortVisitId, AppSession.Actor);
            NavigateTo(typeof(InvoiceDetailPage), shortInvoice.Id);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 1000));
            // Let the frame's navigation transition finish before capturing
            // the current invoice, rather than the outgoing page animation.
            await Task.Delay(700);
            view = (InvoiceDetailPage)ContentFrame.Content;
            var shortPreview = (StackPanel)view.FindName("ReceiptPanel");
            CheckInvoice(shortPreview.Children.Count == 1 && Texts(shortPreview).Any(text => text.Text == "Consultation"),
                "short invoice screen shows one complete paper sheet with current invoice content");
            await SaveVisual((FrameworkElement)shortPreview.Children[0], "invoice-screen-preview.png");
            var legacy = new Invoice { InvoiceNumber = "LEGACY" };
            CheckInvoice(InvoiceReceipt.Create(legacy, null, new ClinicProfile { Name = "Legacy clinic" }).Header == "Legacy clinic", "legacy invoice without snapshot remains printable");
            legacy.DocumentProfileJson = "{invalid";
            try { InvoiceReceipt.Create(legacy, null, new ClinicProfile()); CheckInvoice(false, "invalid snapshot rejected"); }
            catch (JsonException) { CheckInvoice(true, "invalid saved snapshot rejected without substituting current settings"); }
            await CheckReceiptSetup(patientId, opId);
            CheckInvoice(!AppSession.Audit.Query(AuditEvents.InvoicePrinted).Any(), "preview and pagination do not record a submitted print job");
        }
        catch (Exception exception) { CheckInvoice(false, exception.ToString()); }
        finally
        {
            _invoiceChecks.Add(_invoiceChecks.Any(s => s.StartsWith("FAIL ")) ? "INVOICE_RESULT=FAIL" : "INVOICE_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "invoice-check-results.txt"), _invoiceChecks);
            Close();
        }
    }

    private static IEnumerable<T> ReceiptNodes<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ReceiptNodes<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private async Task CheckReceiptSetup(long patientId, long opId)
    {
        _ = new ReceiptSetupControl();
        NavigateTo(typeof(SettingsPage));
        await Task.Delay(150);
        var settings = (SettingsPage)ContentFrame.Content;
        ShellRoot.RequestedTheme = ElementTheme.Light;
        ShellRoot.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
        ((Expander)settings.FindName("ReceiptExpander")).IsExpanded = true;
        await Task.Delay(200);
        var designer = (ReceiptSetupControl)settings.FindName("ReceiptDesigner");
        CheckInvoice(designer.GetLayout() == new ReceiptLayout(), "receipt designer loads existing defaults");
        ((TextBox)settings.FindName("ClinicNameBox")).Text = "BSP CENTER PHYSIOTHERAPY CLINIC";
        ((TextBox)settings.FindName("HeaderBox")).Text = "Ruko Rose Garden 7 no.11, Jakasetia, Bekasi Selatan";
        ((TextBox)settings.FindName("FooterBox")).Text = "Thank you for your visit!";
        foreach (var (name, value) in new[] { ("TopBox", 1d), ("RightBox", 2d), ("BottomBox", 3d), ("LeftBox", 4d),
            ("ClinicSizeBox", 20d), ("TitleSizeBox", 18d), ("InfoSizeBox", 14d), ("TreatmentSizeBox", 14d),
            ("TotalSizeBox", 17d), ("FooterSizeBox", 13d) })
            ((NumberBox)designer.FindName(name)).Value = value;
        ((ComboBox)designer.FindName("FontBox")).SelectedIndex = 1;
        ((Slider)designer.FindName("SpacingSlider")).Value = 0.8;
        ((ToggleSwitch)designer.FindName("TitleSwitch")).IsOn = false;
        ((ToggleSwitch)designer.FindName("HeaderDividerSwitch")).IsOn = true;
        ((ToggleSwitch)designer.FindName("FooterDividerSwitch")).IsOn = true;
        await Task.Delay(150);
        var preview = (StackPanel)designer.FindName("PreviewPanel");
        var sampleTexts = Texts(preview).ToArray();
        CheckInvoice(sampleTexts.Any(t => t.Text == "BSP CENTER PHYSIOTHERAPY CLINIC" && t.FontSize == 20)
            && !sampleTexts.Any(t => t.Text == "TREATMENT RECEIPT"), "clinic text and section changes update live sample without saving");
        CheckInvoice(sampleTexts.All(t => t.FontFamily.Source.Contains("VT323-Regular.ttf#VT323"))
            && sampleTexts.Any(t => t.Text.StartsWith("Notes: ") && Math.Abs(t.LineHeight - t.FontSize * 0.8) < 0.01),
            "live preview applies bundled dot-matrix font, notes and 0.80 line spacing");
        CheckInvoice(((TextBlock)settings.FindName("ReceiptSaveStatus")).Text.Contains("Unsaved"), "receipt editor reports unsaved changes");
        var wanted = designer.GetLayout();
        typeof(SettingsPage).GetMethod("SaveClinic_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(settings, new object[] { settings, new RoutedEventArgs() });
        CheckInvoice(ReceiptLayout.Load(AppSession.Settings) == wanted
            && AppSession.Settings.Get<ClinicProfile>(SettingKeys.ClinicProfile)!.Name == "BSP CENTER PHYSIOTHERAPY CLINIC",
            "save persists clinic text and all receipt appearance settings together");
        // Reopen the encrypted file using a fresh settings service, not an in-memory copy.
        var dbPath = AppSession.Db.DbPath;
        var reopenedSettings = new SettingsService(new Database(dbPath, new string('a', 64)));
        CheckInvoice(ReceiptLayout.Load(reopenedSettings) == wanted, "receipt settings survive a fresh database connection");
        ((NumberBox)designer.FindName("LeftBox")).Text = "";
        CheckInvoice(!designer.Validate(), "blank receipt measurements are rejected beside the input");
        ((NumberBox)designer.FindName("LeftBox")).Value = 4;
        ((NumberBox)designer.FindName("LeftBox")).Text = "4";
        CheckInvoice(designer.Validate(), "corrected receipt measurement clears validation");
        // The actual text editor must update the preview even before NumberBox commits Value.
        var margin = (NumberBox)designer.FindName("TopBox");
        ReceiptNodes<TextBox>(margin).First().Text = "5";
        await Task.Delay(100);
        CheckInvoice(designer.GetLayout().MarginTopMm == 5, "receipt preview reads uncommitted numeric edits");
        designer.SetLayout(wanted);
        CheckInvoice(designer.GetLayout().MarginTopMm == wanted.MarginTopMm,
            "restoring layout replaces uncommitted numeric text even when its previous Value matches");
        ((NumberBox)designer.FindName("ClinicSizeBox")).StartBringIntoView();
        await Task.Delay(200);
        await SaveVisual(ShellRoot, "receipt-setup-controls.png");
        preview.StartBringIntoView();
        await Task.Delay(200);
        await SaveVisual((FrameworkElement)preview.Children[0], "receipt-setup-preview.png");
        await SaveVisual(ShellRoot, "receipt-setup-preview-context.png");
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            ShellRoot.RequestedTheme = theme;
            ShellRoot.Background = new SolidColorBrush(theme == ElementTheme.Light ? Microsoft.UI.Colors.WhiteSmoke : Windows.UI.Color.FromArgb(255, 28, 28, 28));
            foreach (var width in new[] { 700, 1200 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 1000));
                await Task.Delay(150);
                ((ToggleSwitch)designer.FindName("ClinicSwitch")).StartBringIntoView();
                await Task.Delay(150);
                var section = ReceiptNodes<TextBlock>(designer).Single(t => t.Text == "Sections");
                var labelColor = ((SolidColorBrush)section.Foreground).Color;
                CheckInvoice(theme == ElementTheme.Light ? labelColor.R < 128 : labelColor.R > 128,
                    $"{theme} {width}px: designer section labels follow the page theme");
                var controls = ReceiptNodes<Control>(designer).Where(c => c is NumberBox or ComboBox or ToggleSwitch).ToArray();
                CheckInvoice(controls.All(c => c.ActualWidth <= designer.ActualWidth + 0.1),
                    $"{theme} {width}px: designer controls fit the available width");
                CheckInvoice(preview.Children.Cast<Viewbox>().All(v => v.ActualWidth <= preview.ActualWidth + 0.1),
                    $"{theme} {width}px: live preview fits without changing paper geometry");
                await SaveVisual(ShellRoot, $"receipt-setup-{theme}-{width}.png");
            }
        }
        ShellRoot.RequestedTheme = ElementTheme.Light;
        ShellRoot.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);

        var visitId = AppSession.Visits.CreateVisit(patientId, opId, [],
            [new TreatmentItem { Name = "Custom treatment", PriceCents = 25_000_000, Notes = "Custom treatment note" }],
            DiscountTypes.None, 0, null, null, null, AppSession.Actor);
        var invoice = AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
        AppSession.Settings.SaveReceiptSetup(new ClinicProfile { Name = "Later clinic" }, new ReceiptLayout());
        NavigateTo(typeof(InvoiceDetailPage), invoice.Id);
        await Task.Delay(150);
        var view = (InvoiceDetailPage)ContentFrame.Content;
        var receipt = (InvoiceReceipt)typeof(InvoiceDetailPage).GetField("_receipt", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        CheckInvoice(receipt.Layout == wanted && receipt.ClinicName == "BSP CENTER PHYSIOTHERAPY CLINIC",
            "new invoice freezes the saved receipt layout and clinic text at issue time");
        var canvas = (Canvas)view.FindName("PrintCanvas");
        using (var printer = new InvoicePrinter(this, canvas, _ => { }))
        {
            var pages = printer.BuildPages(receipt, InvoicePaperLayout.DefaultPageDescription).ToArray();
            CheckPreviewMatches(view, pages, "customized receipt");
            var body = ((Grid)pages[0]).Children.OfType<StackPanel>().Single();
            CheckInvoice(body.Margin.Left == Math.Ceiling(4d * 96 / 25.4) && body.Margin.Top == Math.Ceiling(1d * 96 / 25.4)
                && body.Width == Math.Floor(InvoicePaperLayout.PaperSize.Width - Math.Ceiling(4d * 96 / 25.4) - Math.Ceiling(2d * 96 / 25.4)),
                "custom margins use millimetres and preserve the 120 mm sheet width");
            var blocks = pages.SelectMany(Texts).ToArray();
            CheckInvoice(blocks.Single(t => t.Text == receipt.Total).FontSize == 17 && blocks.Single(t => t.Text == receipt.Footer).FontSize == 13
                && blocks.Single(t => t.Text == receipt.ClinicName).FontSize == 20, "per-section font sizes reach the printer");
            var baseLayout = wanted with { ShowReceiptTitle = true, HeaderDivider = false, FooterDivider = false };
            var hidden = new[] {
                (baseLayout with { ShowClinicHeader = false }, receipt.ClinicName),
                (baseLayout with { ShowReceiptTitle = false }, "TREATMENT RECEIPT"),
                (baseLayout with { ShowPatient = false }, receipt.BillTo),
                (baseLayout with { ShowInvoiceInfo = false }, receipt.Info),
                (baseLayout with { ShowTreatments = false }, receipt.Lines[0].Name),
                (baseLayout with { ShowTreatmentNotes = false }, "Notes: Custom treatment note"),
                (baseLayout with { ShowTotals = false }, receipt.Total),
                (baseLayout with { ShowTax = false }, receipt.Tax!),
                (baseLayout with { ShowOperator = false }, receipt.Operator),
                (baseLayout with { ShowFooter = false }, receipt.Footer),
            };
            foreach (var (layout, text) in hidden)
            {
                var output = printer.BuildPages(receipt with { Layout = layout }, InvoicePaperLayout.DefaultPageDescription);
                CheckInvoice(!output.SelectMany(Texts).Any(t => t.Text == text), $"section toggle hides {text.Split('\n')[0]}");
            }
            foreach (var (layout, name) in new[] {
                (baseLayout with { HeaderDivider = true }, "HeaderDivider"),
                (baseLayout with { DetailsDivider = true }, "DetailsDivider"),
                (baseLayout with { TreatmentsDivider = true }, "TreatmentsDivider"),
                (baseLayout with { TotalDivider = true }, "TotalDivider"),
                (baseLayout with { FooterDivider = true }, "FooterDivider"),
            })
            {
                var output = printer.BuildPages(receipt with { Layout = layout }, InvoicePaperLayout.DefaultPageDescription);
                var dividers = output.SelectMany(p => ReceiptNodes<Border>(p)).Where(b => b.Name.EndsWith("Divider")).ToArray();
                CheckInvoice(dividers.Length == 1 && dividers[0].Name == name, $"{name} is independently controlled");
            }
            var blank = baseLayout with { ShowClinicHeader = false, ShowReceiptTitle = false, ShowPatient = false,
                ShowInvoiceInfo = false, ShowTreatments = false, ShowTotals = false, ShowTax = false, ShowOperator = false, ShowFooter = false };
            CheckInvoice(printer.BuildPages(receipt with { Layout = blank }, InvoicePaperLayout.DefaultPageDescription).Count == 1,
                "all sections hidden still produces a valid printable sheet");
            var many = receipt with { Lines = Enumerable.Range(1, 80).Select(i => new ReceiptLine($"Custom row {i}", "Rp 250.000", "A wrapped treatment note for this row")).ToArray() };
            var manyPages = printer.BuildPages(many, InvoicePaperLayout.DefaultPageDescription).ToArray();
            CheckPages(manyPages, many, InvoicePaperLayout.DefaultPageDescription, "custom layout pagination with treatment notes");
            CheckInvoice(manyPages.SelectMany(Texts).Count(t => t.Text == "Notes: A wrapped treatment note for this row") == 80,
                "every treatment note survives custom pagination");
        }
        NavigateTo(typeof(SettingsPage));
        await Task.Delay(100);
        settings = (SettingsPage)ContentFrame.Content;
        designer = (ReceiptSetupControl)settings.FindName("ReceiptDesigner");
        designer.SetLayout(wanted);
        typeof(SettingsPage).GetMethod("ResetReceipt_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(settings, new object[] { settings, new RoutedEventArgs() });
        CheckInvoice(designer.GetLayout() == new ReceiptLayout(), "reset restores every layout control to defaults");
        AppSession.Settings.Set(SettingKeys.ReceiptDocument, "malformed shape");
        CheckInvoice(ReceiptLayout.Load(AppSession.Settings) == new ReceiptLayout(), "damaged local appearance settings fall back to defaults");
        var bounded = new ReceiptLayout { MarginLeftMm = -10, MarginRightMm = 999, TotalSize = double.NaN, LineSpacing = 9, Font = "missing" }.Normalize();
        CheckInvoice(bounded.MarginLeftMm == 0 && bounded.MarginRightMm == 25 && bounded.TotalSize == 16 && bounded.LineSpacing == 2 && bounded.Font == "inter",
            "invalid imported measurements, sizes and font are bounded before layout");
        // A saved pre-designer profile must not pick up current layout controls.
        var old = new Invoice { InvoiceNumber = "OLD", DocumentProfileJson = """{"clinic":{"Name":"Old clinic","HeaderText":"Old header"},"settings_version":1}""" };
        CheckInvoice(InvoiceReceipt.Create(old, null, new ClinicProfile()).UseLegacyAppearance,
            "pre-designer issued invoices retain the original renderer");
        AppSession.Settings.Set(SettingKeys.ReceiptDocument, new ReceiptLayout());
    }

    private void CheckPreviewMatches(InvoiceDetailPage view, FrameworkElement[] printed, string label)
    {
        var sheets = ((StackPanel)view.FindName("ReceiptPanel")).Children.Cast<Viewbox>()
            .Select(box => (FrameworkElement)((Border)box.Child).Child).ToArray();
        CheckInvoice(sheets.Length == printed.Length && sheets.All(sheet =>
            Math.Abs(sheet.Width * 25.4 / 96 - 120) < 0.001 && Math.Abs(sheet.Height * 25.4 / 96 - 138) < 0.001),
            $"{label}: screen and printer have identical 120 x 138 mm sheets");
        CheckInvoice(sheets.Length == printed.Length && sheets.Zip(printed).All(pair =>
        {
            var screen = Texts(pair.First).ToArray();
            var paper = Texts(pair.Second).ToArray();
            return screen.Length == paper.Length && screen.Zip(paper).All(textPair =>
            {
                var a = textPair.First.TransformToVisual(pair.First).TransformPoint(new Point(0, 0));
                var b = textPair.Second.TransformToVisual(pair.Second).TransformPoint(new Point(0, 0));
                return textPair.First.Text == textPair.Second.Text && Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1
                    && Math.Abs(textPair.First.ActualWidth - textPair.Second.ActualWidth) < 0.1;
            });
        }), $"{label}: text, wrapping and placement match printed pages");
    }

    private void CheckPages(FrameworkElement[] pages, InvoiceReceipt receipt, PrintPageDescription description, string label)
    {
        var texts = pages.SelectMany(Texts).ToArray();
        CheckInvoice(receipt.Lines.All(line => texts.Count(t => t.Text == line.Name) == 1), $"{label}: every treatment appears exactly once");
        CheckInvoice(texts.Count(t => t.Text == receipt.Total) == 1 && texts.Count(t => t.Text == receipt.Footer) == 1, $"{label}: totals and clinic footer preserved");
        CheckInvoice(pages.All(page => ((Grid)page).Children.OfType<StackPanel>().Single().Children.Cast<FrameworkElement>().All(block =>
        {
            var position = block.TransformToVisual(page).TransformPoint(new Point(0, 0));
            return position.X >= description.ImageableRect.Left - 0.1 && position.Y >= description.ImageableRect.Top - 0.1
                && position.X + block.ActualWidth <= description.ImageableRect.Right + 0.1
                && position.Y + block.ActualHeight <= description.ImageableRect.Bottom + 0.1;
        })), $"{label}: receipt blocks fit printer's printable area");
    }

    private async Task SavePage(FrameworkElement page, Canvas canvas, string name)
    {
        canvas.Children.Remove(page);
        var prior = ContentFrame.Content;
        ContentFrame.Content = page;
        await Task.Delay(100);
        await SaveVisual(page, name);
        ContentFrame.Content = prior;
        canvas.Children.Add(page);
    }

    private async Task SaveVisual(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var file = await StorageFile.GetFileFromPathAsync(CreateOutput(name));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private static string CreateOutput(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
