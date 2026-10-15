using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Printing;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Graphics.Printing.OptionDetails;

namespace MedRecPlus.App;

/// <summary>Owns the print registration for one invoice page; dispose when leaving it.</summary>
internal sealed class InvoicePrinter : IDisposable
{
    internal static Size PaperSize => InvoicePaperLayout.PaperSize;
    private const string PaperOptionId = "invoicePaper";
    private readonly nint _windowHandle;
    private readonly Canvas _canvas;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<PrintTaskCompletion> _completed;
    private readonly Action<PrintPageDescription>? _previewChanged;
    private readonly PrintManager _manager;
    private readonly PrintDocument _document = new();
    private readonly IPrintDocumentSource _source;
    private IReadOnlyList<FrameworkElement> _pages = [];
    private InvoiceReceipt? _receipt;
    private PrintTask? _task;
    private bool _disposed;

    public InvoicePrinter(Window window, Canvas canvas, Action<PrintTaskCompletion> completed,
        Action<PrintPageDescription>? previewChanged = null)
    {
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _canvas = canvas;
        _dispatcher = canvas.DispatcherQueue;
        _completed = completed;
        _previewChanged = previewChanged;
        _source = _document.DocumentSource;
        _document.Paginate += Paginate;
        _document.GetPreviewPage += GetPreviewPage;
        _document.AddPages += AddPages;
        _manager = PrintManagerInterop.GetForWindow(_windowHandle);
        _manager.PrintTaskRequested += PrintTaskRequested;
    }

    public async Task<bool> ShowAsync(InvoiceReceipt receipt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _receipt = receipt;
        return await PrintManagerInterop.ShowPrintUIForWindowAsync(_windowHandle);
    }

    private void PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        if (_disposed || _receipt == null) return;
        _task = args.Request.CreatePrintTask($"Invoice {_receipt.Number}", source =>
            source.SetSource(_source));
        _task.Options.Orientation = PrintOrientation.Portrait;
        var details = PrintTaskOptionDetails.GetFromPrintTaskOptions(_task.Options);
        var paper = details.CreateItemListOption(PaperOptionId, "Invoice paper");
        paper.AddItem("120x138", "12 cm wide × 13.8 cm high");
        paper.TrySetValue("120x138");
        details.DisplayedOptions.Clear();
        details.DisplayedOptions.Add(PaperOptionId);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.MediaSize);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.Copies);
        _task.Completed += PrintTaskCompleted;
    }

    private void PrintTaskCompleted(PrintTask sender, PrintTaskCompletedEventArgs args)
    {
        sender.Completed -= PrintTaskCompleted;
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            _task = null;
            _completed(args.Completion);
        });
    }

    private void Paginate(object sender, PaginateEventArgs args)
    {
        var options = (PrintTaskOptions)args.PrintTaskOptions;
        var description = options.GetPageDescription(0);
        var details = PrintTaskOptionDetails.GetFromPrintTaskOptions(options);
        details.Options[PaperOptionId].ErrorText = MatchesInvoicePaper(description.PageSize)
            ? ""
            : "Select custom paper 120 mm wide × 138 mm high in your printer settings.";
        BuildPages(_receipt!, description);
        _previewChanged?.Invoke(description);
        _document.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
    }

    internal static bool MatchesInvoicePaper(Size size) =>
        Math.Abs(size.Width - PaperSize.Width) <= 0.5 * 96 / 25.4 &&
        Math.Abs(size.Height - PaperSize.Height) <= 0.5 * 96 / 25.4;

    internal IReadOnlyList<FrameworkElement> BuildPages(InvoiceReceipt receipt, PrintPageDescription description)
    {
        _pages = InvoicePaperLayout.BuildPages(receipt, _canvas, description);
        return _pages;
    }

    private void GetPreviewPage(object sender, GetPreviewPageEventArgs args) =>
        _document.SetPreviewPage(args.PageNumber, _pages[args.PageNumber - 1]);

    private void AddPages(object sender, AddPagesEventArgs args)
    {
        foreach (var page in _pages) _document.AddPage(page);
        _document.AddPagesComplete();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.PrintTaskRequested -= PrintTaskRequested;
        if (_task != null) _task.Completed -= PrintTaskCompleted;
        _document.Paginate -= Paginate;
        _document.GetPreviewPage -= GetPreviewPage;
        _document.AddPages -= AddPages;
        _pages = [];
        _canvas.Children.Clear();
    }
}
