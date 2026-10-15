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
            EnterShell();
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
