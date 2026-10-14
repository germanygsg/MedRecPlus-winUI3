using System.Collections.ObjectModel;
using System.Text.Json;
using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class PatientDialog : ContentDialog
{
    private readonly long? _patientId;
    private readonly ObservableCollection<IcdCode> _results = new();
    private readonly ObservableCollection<IcdRow> _selected = new();

    public PatientDialog(long? patientId = null)
    {
        InitializeComponent();
        _patientId = patientId;
        IcdResults.ItemsSource = _results;
        IcdSelected.ItemsSource = _selected;
        GenderBox.SelectedIndex = 0;

        if (patientId is long id)
        {
            Title = "Edit patient";
            var p = AppSession.Patients.GetById(id);
            if (p == null) { Hide(); return; }
            RecordNumberBox.Text = p.RecordNumber;
            NameBox.Text = p.Name;
            GenderBox.SelectedIndex = p.Gender switch { "Ms." => 1, "Mx." => 2, _ => 0 };
            AgeBox.Value = p.Age;
            PhoneBox.Text = p.PhoneNumber ?? "";
            AddressBox.Text = p.Address ?? "";
            DiagnosisBox.Text = p.InitialDiagnosis ?? "";
            if (!string.IsNullOrEmpty(p.IcdCodesJson))
            {
                try
                {
                    foreach (var c in JsonSerializer.Deserialize<List<IcdCode>>(p.IcdCodesJson) ?? new())
                        _selected.Add(new IcdRow { Code = c.Code, Display = $"{c.Code} — {c.Title}", Code2 = c });
                }
                catch { }
            }
            SelectBasis(p.ConsentBasis);
        }
        else
        {
            Title = "Add patient";
            RecordNumberBox.Text = "(auto-generated)";
        }

        PrimaryButtonClick += OnPrimary;
    }

    private void SelectBasis(string? basis)
    {
        for (var i = 0; i < BasisBox.Items.Count; i++)
            if ((BasisBox.Items[i] as ComboBoxItem)?.Tag as string == basis)
            { BasisBox.SelectedIndex = i; return; }
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // we close manually after validation
        try
        {
            var name = NameBox.Text.Trim();
            if (name.Length == 0) throw new InvalidOperationException("Patient name is required.");
            if (double.IsNaN(AgeBox.Value)) throw new InvalidOperationException("Age is required.");
            var basis = (BasisBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(basis))
                throw new InvalidOperationException("Lawful basis is required (CON-01).");

            var gender = (GenderBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Mr.";
            var codes = _selected.Select(r => r.Code2).ToList();

            if (_patientId is long id)
            {
                var p = AppSession.Patients.GetById(id) ?? throw new InvalidOperationException("Patient not found.");
                p.Name = name; p.Gender = gender; p.Age = (int)AgeBox.Value;
                p.PhoneNumber = PhoneBox.Text.Trim(); p.Address = AddressBox.Text.Trim();
                p.InitialDiagnosis = DiagnosisBox.Text.Trim();
                p.IcdCodesJson = JsonSerializer.Serialize(codes);
                p.ConsentBasis = basis; p.ConsentDate = DateTime.UtcNow.ToString("o");
                p.ConsentRecordedBy = AppSession.Actor;
                AppSession.Patients.Update(p);
            }
            else
            {
                string recordNo;
                using (var conn = AppSession.Db.Open())
                using (var tx = conn.BeginTransaction())
                {
                    recordNo = AppSession.Numbering.NextPatientRecordNumber(conn, tx, DateTime.UtcNow.Year);
                    tx.Commit();
                }
                var p = new Patient
                {
                    RecordNumber = recordNo, Name = name, Gender = gender, Age = (int)AgeBox.Value,
                    PhoneNumber = PhoneBox.Text.Trim(), Address = AddressBox.Text.Trim(),
                    InitialDiagnosis = DiagnosisBox.Text.Trim(),
                    IcdCodesJson = JsonSerializer.Serialize(codes),
                    ConsentBasis = basis, ConsentDate = DateTime.UtcNow.ToString("o"),
                    ConsentRecordedBy = AppSession.Actor,
                };
                AppSession.Patients.Create(p);
            }
            Hide();
            // Signal success: the caller checks a flag via Tag.
            Tag = "saved";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void IcdSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _results.Clear();
        IcdSearchStatus.Visibility = Visibility.Collapsed;
        var q = IcdSearchBox.Text.Trim();
        if (q.Length < 2) { IcdResults.Visibility = Visibility.Collapsed; return; }
        var results = UiHelpers.SearchIcd(q, 15, out var searchError);
        IcdSearchStatus.Text = searchError ?? "";
        IcdSearchStatus.Visibility = searchError == null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var c in results)
            if (!_selected.Any(s => s.Code == c.Code))
                _results.Add(c);
        IcdResults.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IcdResult_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IcdCode c)
        {
            _selected.Add(new IcdRow { Code = c.Code, Display = $"{c.Code} — {c.Title}", Code2 = c });
            _results.Clear();
            IcdResults.Visibility = Visibility.Collapsed;
            IcdSearchBox.Text = "";
        }
    }

    private void IcdRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string code)
        {
            var row = _selected.FirstOrDefault(s => s.Code == code);
            if (row != null) _selected.Remove(row);
        }
    }

    public sealed class IcdRow
    {
        public string Code { get; set; } = "";
        public string Display { get; set; } = "";
        public IcdCode Code2 { get; set; } = new();
    }
}
