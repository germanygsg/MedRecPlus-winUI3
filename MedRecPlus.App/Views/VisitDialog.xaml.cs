using System.Collections.ObjectModel;
using System.Text.Json;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class VisitDialog : ContentDialog
{
    private Patient? _patient;
    private readonly Appointment? _editingVisit;
    private readonly Dictionary<string, object?> _savedVitals = new();
    private readonly FormValidation _validation = new();
    private readonly FormValidation _treatmentValidation = new();
    private readonly ObservableCollection<Patient> _patientResults = new();
    private readonly ObservableCollection<IcdCode> _icdResults = new();
    private readonly ObservableCollection<IcdRow> _icdSelected = new();
    private readonly ObservableCollection<TreatmentRow> _treatments = new();
    private readonly List<TextBox> _customExamBoxes = new();
    private XamlRoot? _xamlRoot;

    public VisitDialog(long? preselectPatientId = null, Appointment? visitToEdit = null)
    {
        InitializeComponent();
        _editingVisit = visitToEdit;
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

        if (visitToEdit != null)
        {
            LoadVisit(visitToEdit);
        }
        else if (preselectPatientId is long pid)
        {
            _patient = AppSession.Patients.GetById(pid);
        }
        UpdatePatientSelection();

        PrimaryButtonClick += OnPrimary;
        UpdateTotal();
    }

    private void LoadVisit(Appointment visit)
    {
        Title = "Edit visit";
        PrimaryButtonText = "Save changes";
        // Keep historical references available even when a catalog entry was removed.
        _patient = AppSession.Patients.GetById(visit.PatientId)
            ?? new Patient { Id = visit.PatientId, Name = visit.PatientName };
        var operatorItem = OperatorBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => (long)item.Tag == visit.OperatorId);
        if (operatorItem == null)
        {
            operatorItem = new ComboBoxItem { Content = $"{visit.OperatorName} (saved operator)", Tag = visit.OperatorId };
            OperatorBox.Items.Add(operatorItem);
        }
        OperatorBox.SelectedItem = operatorItem;

        var vitals = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(visit.VitalSignsJson) ?? new();
        foreach (var (key, value) in vitals) _savedVitals[key] = value;
        if (vitals.TryGetValue("bloodPressure", out var bp) && bp.ValueKind == JsonValueKind.String)
            BpBox.Text = bp.GetString() ?? "";
        foreach (var (key, box) in new[] { ("respirationRate", RespBox), ("heartRate", HrBox), ("borgScale", BorgBox) })
            if (vitals.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                box.Value = number;
        foreach (var box in _customExamBoxes)
            if (vitals.TryGetValue($"custom_{box.Tag}", out var value))
                box.Text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

        foreach (var code in JsonSerializer.Deserialize<List<IcdCode>>(visit.DiagnosisCodesJson ?? "[]") ?? new())
            _icdSelected.Add(new IcdRow { Code = code.Code, Display = $"{code.Code} — {code.Title}", Code2 = code });

        foreach (var treatment in JsonSerializer.Deserialize<List<TreatmentItem>>(visit.TreatmentsJson) ?? new())
        {
            var catalogRow = _treatments.FirstOrDefault(row => row.Id == treatment.Id);
            if (catalogRow != null && !TreatmentList.SelectedItems.Contains(catalogRow)) _treatments.Remove(catalogRow);
            var row = new TreatmentRow
            {
                Id = treatment.Id, Name = treatment.Name, PriceCents = treatment.PriceCents,
                Price = UiHelpers.Money(treatment.PriceCents), Description = treatment.Description, Notes = treatment.Notes,
            };
            _treatments.Add(row);
            TreatmentList.SelectedItems.Add(row);
        }

        if (visit.DiscountType != DiscountTypes.None)
        {
            DiscountTypeBox.SelectedItem = DiscountTypeBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == visit.DiscountType);
            DiscountValueBox.Value = visit.DiscountType == DiscountTypes.Percent
                ? visit.DiscountValueCents : (double)(visit.DiscountValueCents / 100m);
            DiscountPanel.Visibility = Visibility.Visible;
            AddDiscountButton.Visibility = Visibility.Collapsed;
        }
        NotesBox.Text = visit.Notes ?? "";
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

    private void AddDiscount_Click(object sender, RoutedEventArgs e)
    {
        DiscountTypeBox.SelectedIndex = 1;
        DiscountValueBox.Value = double.NaN;
        DiscountPanel.Visibility = Visibility.Visible;
        AddDiscountButton.Visibility = Visibility.Collapsed;
        DiscountTypeBox.Focus(FocusState.Programmatic);
    }

    private void RemoveDiscount_Click(object sender, RoutedEventArgs e)
    {
        DiscountTypeBox.SelectedIndex = 0;
        DiscountValueBox.Value = double.NaN;
        DiscountPanel.Visibility = Visibility.Collapsed;
        AddDiscountButton.Visibility = Visibility.Visible;
        AddDiscountButton.Focus(FocusState.Programmatic);
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
        var id = AppSession.Treatments.Create(name, null, priceCents, null);
        // Adding to the catalog must not clear selected treatments or their saved snapshots.
        _treatments.Add(new TreatmentRow { Id = id, Name = name, Price = UiHelpers.Money(priceCents), PriceCents = priceCents });
        NewTreatNameBox.Text = "";
        NewTreatPriceBox.Text = "";
        NewTreatmentForm.Visibility = Visibility.Collapsed;
        _treatmentValidation.Reset();
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ErrorText.Visibility = Visibility.Collapsed;
        if (!_validation.Validate()) return;
        try
        {
            var opId = (long)((ComboBoxItem)OperatorBox.SelectedItem).Tag;

            var vitals = new Dictionary<string, object?>(_savedVitals)
            {
                ["bloodPressure"] = BpBox.Text.Trim(),
                ["respirationRate"] = ReadVital(RespBox),
                ["heartRate"] = ReadVital(HrBox),
                ["borgScale"] = ReadVital(BorgBox),
            };
            foreach (var tb in _customExamBoxes)
                vitals[$"custom_{tb.Tag}"] = tb.Text.Trim();

            var items = TreatmentList.SelectedItems.Cast<TreatmentRow>()
                .Select(t => new TreatmentItem { Id = t.Id, Name = t.Name, PriceCents = t.PriceCents, Description = t.Description, Notes = t.Notes })
                .ToList();

            var discType = DiscountPanel.Visibility == Visibility.Visible
                ? (DiscountTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none"
                : "none";
            var discRaw = discType == "none" ? 0 : FormValidation.Number(DiscountValueBox);
            var discValue = double.IsNaN(discRaw) ? 0m : (decimal)discRaw;

            if (_editingVisit == null)
            {
                AppSession.Visits.CreateVisit(
                    _patient!.Id, opId, vitals, items, discType, discValue,
                    NotesBox.Text.Trim(), null,
                    _icdSelected.Select(r => r.Code2).ToList(), AppSession.Actor);
            }
            else
            {
                _editingVisit.PatientId = _patient!.Id;
                _editingVisit.OperatorId = opId;
                _editingVisit.VitalSignsJson = JsonSerializer.Serialize(vitals);
                _editingVisit.TreatmentsJson = JsonSerializer.Serialize(items);
                _editingVisit.DiscountType = discType;
                _editingVisit.DiscountValueCents = discType == DiscountTypes.Percent
                    ? (long)discValue : (long)Math.Round(discValue * 100m, MidpointRounding.AwayFromZero);
                _editingVisit.Notes = NotesBox.Text.Trim();
                _editingVisit.DiagnosisCodesJson = JsonSerializer.Serialize(_icdSelected.Select(row => row.Code2).ToList());
                AppSession.Visits.UpdateVisit(_editingVisit, AppSession.Actor);
            }
            Tag = "saved";
            Hide();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private static int ReadVital(NumberBox box)
    {
        var value = FormValidation.Number(box);
        return double.IsNaN(value) ? 0 : (int)value;
    }

    public sealed class TreatmentRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public long PriceCents { get; set; }
        public string? Description { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class IcdRow
    {
        public string Code { get; set; } = "";
        public string Display { get; set; } = "";
        public IcdCode Code2 { get; set; } = new();
    }
}
