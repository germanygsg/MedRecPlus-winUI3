using System.Globalization;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App;

public sealed partial class ReceiptSetupControl : UserControl
{
    private bool _loading;
    private bool _previewQueued;
    private ClinicProfile _clinic = new();
    private readonly FormValidation _validation = new();
    private readonly Dictionary<NumberBox, double> _numbers = [];
    private readonly HashSet<TextBox> _subscribedEditors = [];
    public event EventHandler? Changed;

    public ReceiptSetupControl()
    {
        InitializeComponent();
        foreach (var box in new[] { TopBox, RightBox, BottomBox, LeftBox,
            ClinicSizeBox, TitleSizeBox, InfoSizeBox, TreatmentSizeBox, TotalSizeBox, FooterSizeBox })
        {
            var error = (TextBlock)FindName(box.Name + "Error");
            _validation.Add(box, error, () =>
            {
                var value = FormValidation.Number(box);
                return double.IsFinite(value) && value >= box.Minimum && value <= box.Maximum
                    ? null : $"Enter a value from {box.Minimum} to {box.Maximum}.";
            });
            box.ValueChanged += (_, _) => InputChanged();
            // Update while typing, including when NumberBox.Value still holds
            // its previous committed value. Read the editor through FormValidation.
            box.Loaded += (_, _) =>
            {
                var editor = Descendants(box).OfType<TextBox>().FirstOrDefault();
                if (editor != null && _subscribedEditors.Add(editor)) editor.TextChanged += (_, _) => InputChanged();
            };
        }
        foreach (var toggle in new[] { ClinicSwitch, TitleSwitch, PatientSwitch, InfoSwitch,
            TreatmentsSwitch, NotesSwitch, TotalsSwitch, TaxSwitch, OperatorSwitch, FooterSwitch,
            HeaderDividerSwitch, DetailsDividerSwitch, TreatmentsDividerSwitch, TotalDividerSwitch, FooterDividerSwitch })
            toggle.Toggled += (_, _) => InputChanged();
        FontBox.SelectionChanged += (_, _) => InputChanged();
        SpacingSlider.ValueChanged += (_, _) => InputChanged();
        Loaded += (_, _) => QueuePreview();
        SetLayout(new ReceiptLayout());
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public void SetClinic(ClinicProfile clinic)
    {
        _clinic = clinic;
        QueuePreview();
    }

    public void SetLayout(ReceiptLayout layout)
    {
        _loading = true;
        var value = layout.Normalize();
        Set(TopBox, value.MarginTopMm); Set(RightBox, value.MarginRightMm);
        Set(BottomBox, value.MarginBottomMm); Set(LeftBox, value.MarginLeftMm);
        Set(ClinicSizeBox, value.ClinicHeaderSize); Set(TitleSizeBox, value.ReceiptTitleSize);
        Set(InfoSizeBox, value.InfoSize); Set(TreatmentSizeBox, value.TreatmentSize);
        Set(TotalSizeBox, value.TotalSize); Set(FooterSizeBox, value.FooterSize);
        FontBox.SelectedIndex = value.Font switch { "vt323" => 1, "courier" => 2, _ => 0 };
        SpacingSlider.Value = value.LineSpacing;
        ClinicSwitch.IsOn = value.ShowClinicHeader; TitleSwitch.IsOn = value.ShowReceiptTitle;
        PatientSwitch.IsOn = value.ShowPatient; InfoSwitch.IsOn = value.ShowInvoiceInfo;
        TreatmentsSwitch.IsOn = value.ShowTreatments; NotesSwitch.IsOn = value.ShowTreatmentNotes;
        TotalsSwitch.IsOn = value.ShowTotals; TaxSwitch.IsOn = value.ShowTax;
        OperatorSwitch.IsOn = value.ShowOperator; FooterSwitch.IsOn = value.ShowFooter;
        HeaderDividerSwitch.IsOn = value.HeaderDivider; DetailsDividerSwitch.IsOn = value.DetailsDivider;
        TreatmentsDividerSwitch.IsOn = value.TreatmentsDivider; TotalDividerSwitch.IsOn = value.TotalDivider;
        FooterDividerSwitch.IsOn = value.FooterDivider;
        _validation.Reset();
        _loading = false;
        QueuePreview();
    }

    private void Set(NumberBox box, double value)
    {
        box.Value = value;
        // Value may already equal the target while its editor contains a
        // different, uncommitted number. Reset must replace that text too.
        box.Text = value.ToString(CultureInfo.CurrentCulture);
        if (Descendants(box).OfType<TextBox>().FirstOrDefault() is { } editor)
            editor.Text = box.Text;
        _numbers[box] = value;
    }

    private double Read(NumberBox box)
    {
        var value = FormValidation.Number(box);
        if (double.IsFinite(value) && value >= box.Minimum && value <= box.Maximum) _numbers[box] = value;
        return _numbers[box];
    }

    public ReceiptLayout GetLayout() => new ReceiptLayout
    {
        MarginTopMm = Read(TopBox), MarginRightMm = Read(RightBox),
        MarginBottomMm = Read(BottomBox), MarginLeftMm = Read(LeftBox),
        Font = (FontBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "inter",
        ClinicHeaderSize = Read(ClinicSizeBox), ReceiptTitleSize = Read(TitleSizeBox),
        InfoSize = Read(InfoSizeBox), TreatmentSize = Read(TreatmentSizeBox),
        TotalSize = Read(TotalSizeBox), FooterSize = Read(FooterSizeBox),
        LineSpacing = SpacingSlider.Value,
        ShowClinicHeader = ClinicSwitch.IsOn, ShowReceiptTitle = TitleSwitch.IsOn,
        ShowPatient = PatientSwitch.IsOn, ShowInvoiceInfo = InfoSwitch.IsOn,
        ShowTreatments = TreatmentsSwitch.IsOn, ShowTreatmentNotes = NotesSwitch.IsOn,
        ShowTotals = TotalsSwitch.IsOn, ShowTax = TaxSwitch.IsOn,
        ShowOperator = OperatorSwitch.IsOn, ShowFooter = FooterSwitch.IsOn,
        HeaderDivider = HeaderDividerSwitch.IsOn, DetailsDivider = DetailsDividerSwitch.IsOn,
        TreatmentsDivider = TreatmentsDividerSwitch.IsOn, TotalDivider = TotalDividerSwitch.IsOn,
        FooterDivider = FooterDividerSwitch.IsOn,
    };

    public bool Validate() => _validation.Validate();

    private void InputChanged()
    {
        if (_loading) return;
        Changed?.Invoke(this, EventArgs.Empty);
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (_loading || _previewQueued) return;
        _previewQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _previewQueued = false;
            if (!IsLoaded) return;
            RenderPreview();
        });
    }

    internal void RenderPreview()
    {
        var layout = GetLayout();
        SpacingValue.Text = layout.LineSpacing.ToString("0.00", CultureInfo.CurrentCulture) + "×";
        FontHint.Text = layout.Font switch
        {
            "vt323" => "Dot-matrix look, bundled for offline use.",
            "courier" => "A classic fixed-width receipt font.",
            _ => "A clean sans serif font, bundled for offline use.",
        };
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-202610-0001", PatientName = "John Doe", OperatorName = "Dr. Smith",
            Date = "2026-10-18T06:17:00Z", AppointmentDate = "2026-10-18T06:00:00Z", Status = InvoiceStatuses.Paid,
            TreatmentsJson = """[{"Name":"Physiotherapy Session","PriceCents":25000000,"Notes":"Lower back treatment"},{"Name":"Exercise Therapy","PriceCents":10000000}]""",
            SubtotalCents = 35_000_000, TaxLabel = "PPN", TaxCents = 3_850_000,
            TotalCents = 38_850_000, PaidCents = 38_850_000,
        };
        var clinic = new ClinicProfile
        {
            Name = string.IsNullOrWhiteSpace(_clinic.Name) ? "Your clinic" : _clinic.Name,
            HeaderText = _clinic.HeaderText, FooterText = _clinic.FooterText,
        };
        var receipt = InvoiceReceipt.Create(invoice, "PT3473085", clinic) with
        {
            Layout = layout, ClinicName = clinic.Name, UseLegacyAppearance = false,
        };
        try
        {
            InvoicePaperLayout.ShowPreview(receipt, PreviewPanel, InvoicePaperLayout.DefaultPageDescription);
            PreviewError.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException)
        {
            PreviewPanel.Children.Clear();
            PreviewError.Text = "These margins leave too little space for the receipt. Reduce the margins.";
            PreviewError.Visibility = Visibility.Visible;
        }
    }
}
