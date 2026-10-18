using System.Collections.ObjectModel;
using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class VisitDialog : ContentDialog
{
    private Patient? _patient;
    private readonly FormValidation _validation = new();
    private readonly FormValidation _treatmentValidation = new();
    private readonly ObservableCollection<Patient> _patientResults = new();
    private readonly ObservableCollection<IcdCode> _icdResults = new();
    private readonly ObservableCollection<IcdRow> _icdSelected = new();
    private readonly ObservableCollection<TreatmentRow> _treatments = new();
    private readonly List<TextBox> _customExamBoxes = new();
    private XamlRoot? _xamlRoot;

    public VisitDialog(long? preselectPatientId = null)
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _validation.Add(PatientSearchBox, PatientSearchBoxError, () => _patient == null ? "Select a patient." : null);
        _validation.Add(OperatorBox, OperatorBoxError, () => OperatorBox.SelectedItem == null ? "Select an operator." : null);
        _treatmentValidation.Add(NewTreatNameBox, NewTreatNameBoxError, () => FormValidation.Required(NewTreatNameBox.Text, "Treatment name"));
        _treatmentValidation.Add(NewTreatPriceBox, NewTreatPriceBoxError, () => MedRecPlus.Core.Services.RupiahAmount.TryParseCents(NewTreatPriceBox.Text, out _)
            ? null : "Enter a whole-rupiah price of at least Rp 1 (e.g. 300000).");
        PatientResults.ItemsSource = _patientResults;
        IcdResults.ItemsSource = _icdResults;
        IcdChips.ItemsSource = _icdSelected;
        TreatmentList.ItemsSource = _treatments;

        foreach (var op in AppSession.Operators.GetAll())
            OperatorBox.Items.Add(new ComboBoxItem { Content = $"{op.Name} ({op.Role})", Tag = op.Id });
        if (OperatorBox.Items.Count > 0) OperatorBox.SelectedIndex = 0;

        foreach (var t in AppSession.Treatments.GetAll())
            _treatments.Add(new TreatmentRow { Id = t.Id, Name = t.Name, Price = UiHelpers.Money(t.PriceCents), PriceCents = t.PriceCents });
        TreatmentList.SelectionChanged += (_, _) => UpdateTotal();

        foreach (var ce in AppSession.CustomExams.GetAll())
        {
            var tb = new TextBox { Header = $"{ce.Name} ({ce.Unit})", Tag = ce.Id };
            _customExamBoxes.Add(tb);
            CustomExamsPanel.Children.Add(tb);
        }

        if (preselectPatientId is long pid)
        {
            _patient = AppSession.Patients.GetById(pid);
        }
        UpdatePatientSelection();

        PrimaryButtonClick += OnPrimary;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_xamlRoot != null) return;
        _xamlRoot = XamlRoot;
        if (_xamlRoot is null) return;
        UpdateScrollerHeight();
        _xamlRoot.Changed += OnXamlRootChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_xamlRoot is null) return;
        _xamlRoot.Changed -= OnXamlRootChanged;
        _xamlRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateScrollerHeight();

    private void UpdateScrollerHeight()
    {
        var height = _xamlRoot?.Size.Height ?? 0;
        if (height > 0) ContentScroller.MaxHeight = Math.Max(120, height - 150);
    }

    private void UpdatePatientSelection()
    {
        var hasPatient = _patient != null;
        PatientSelectedText.Text = hasPatient
            ? $"Selected Patient: {_patient!.Name} ({_patient.RecordNumber})" : "";
        SelectedPatientRow.Visibility = hasPatient ? Visibility.Visible : Visibility.Collapsed;
        PatientSearchBox.Visibility = hasPatient ? Visibility.Collapsed : Visibility.Visible;
        _patientResults.Clear();
        PatientResults.Visibility = Visibility.Collapsed;
        PatientSearchBox.Text = "";
        _validation.Recheck();
    }

    private void ClearPatient_Click(object sender, RoutedEventArgs e)
    {
        _patient = null;
        UpdatePatientSelection();
        PatientSearchBox.Focus(FocusState.Programmatic);
    }

    private void PatientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _patientResults.Clear();
        var q = PatientSearchBox.Text.Trim();
        if (_patient != null || q.Length < 2) { PatientResults.Visibility = Visibility.Collapsed; return; }
        foreach (var p in AppSession.Patients.Search(q, 10))
            _patientResults.Add(p);
        PatientResults.Visibility = _patientResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PatientResult_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Patient p)
        {
            _patient = p;
            UpdatePatientSelection();
        }
    }

    private void IcdSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _icdResults.Clear();
        IcdSearchStatus.Visibility = Visibility.Collapsed;
        var q = IcdSearchBox.Text.Trim();
        if (q.Length < 2) { IcdResults.Visibility = Visibility.Collapsed; return; }
        var results = UiHelpers.SearchIcd(q, 12, out var searchError);
        IcdSearchStatus.Text = searchError ?? "";
        IcdSearchStatus.Visibility = searchError == null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var c in results)
            if (!_icdSelected.Any(s => s.Code == c.Code))
                _icdResults.Add(c);
        IcdResults.Visibility = _icdResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IcdResult_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IcdCode c)
        {
            _icdSelected.Add(new IcdRow { Code = c.Code, Display = $"{c.Code} — {c.Title}", Code2 = c });
            _icdResults.Clear();
            IcdResults.Visibility = Visibility.Collapsed;
            IcdSearchBox.Text = "";
        }
    }

    private void IcdRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string code)
        {
            var row = _icdSelected.FirstOrDefault(s => s.Code == code);
            if (row != null) _icdSelected.Remove(row);
        }
    }

    private void UpdateTotal()
    {
        var total = TreatmentList.SelectedItems.Cast<TreatmentRow>().Sum(t => t.PriceCents);
        TotalText.Text = $"Treatments total: {UiHelpers.Money(total)}";
    }

    private void NewTreatmentToggle_Click(object sender, RoutedEventArgs e)
    {
        var show = NewTreatmentForm.Visibility != Visibility.Visible;
        NewTreatmentForm.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _treatmentValidation.Reset();
    }

    private void NewTreatmentSave_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTreatNameBox.Text.Trim();
        if (!_treatmentValidation.Validate()) return;
        MedRecPlus.Core.Services.RupiahAmount.TryParseCents(NewTreatPriceBox.Text, out var priceCents);
        AppSession.Treatments.Create(name, null, priceCents, null);
        _treatments.Clear();
        foreach (var t in AppSession.Treatments.GetAll())
            _treatments.Add(new TreatmentRow { Id = t.Id, Name = t.Name, Price = UiHelpers.Money(t.PriceCents), PriceCents = t.PriceCents });
        NewTreatNameBox.Text = "";
        NewTreatPriceBox.Text = "";
        NewTreatmentForm.Visibility = Visibility.Collapsed;
        _treatmentValidation.Reset();
    }

    private void RefreshTreatments()
    {
        _treatments.Clear();
        foreach (var t in AppSession.Treatments.GetAll())
            _treatments.Add(new TreatmentRow { Id = t.Id, Name = t.Name, Price = UiHelpers.Money(t.PriceCents), PriceCents = t.PriceCents });
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ErrorText.Visibility = Visibility.Collapsed;
        if (!_validation.Validate()) return;
        try
        {
            var opId = (long)((ComboBoxItem)OperatorBox.SelectedItem).Tag;

            var vitals = new Dictionary<string, object?>
            {
                ["bloodPressure"] = BpBox.Text.Trim(),
                ["respirationRate"] = double.IsNaN(RespBox.Value) ? 0 : (int)RespBox.Value,
                ["heartRate"] = double.IsNaN(HrBox.Value) ? 0 : (int)HrBox.Value,
                ["borgScale"] = double.IsNaN(BorgBox.Value) ? 0 : (int)BorgBox.Value,
            };
            foreach (var tb in _customExamBoxes)
                vitals[$"custom_{tb.Tag}"] = tb.Text.Trim();

            var items = TreatmentList.SelectedItems.Cast<TreatmentRow>()
                .Select(t => new TreatmentItem { Id = t.Id, Name = t.Name, PriceCents = t.PriceCents })
                .ToList();

            var discType = (DiscountTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
            var discRaw = DiscountValueBox.Value;
            var discValue = double.IsNaN(discRaw) ? 0m : (decimal)discRaw;

            AppSession.Visits.CreateVisit(
                _patient!.Id, opId, vitals, items, discType, discValue,
                NotesBox.Text.Trim(), null,
                _icdSelected.Select(r => r.Code2).ToList(), AppSession.Actor);
            Tag = "saved";
            Hide();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    public sealed class TreatmentRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public long PriceCents { get; set; }
    }

    public sealed class IcdRow
    {
        public string Code { get; set; } = "";
        public string Display { get; set; } = "";
        public IcdCode Code2 { get; set; } = new();
    }
}
