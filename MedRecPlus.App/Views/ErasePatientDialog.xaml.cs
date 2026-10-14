using MedRecPlus.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedRecPlus.App.Views;

public sealed partial class ErasePatientDialog : ContentDialog
{
    private readonly Patient _patient;

    public ErasePatientDialog(Patient patient)
    {
        InitializeComponent();
        _patient = patient;
        var visits = AppSession.Appointments.List(null, null, "", 100000).Count(v => v.PatientId == patient.Id);
        var invoices = AppSession.InvoiceRecords.List("", null, null, null, 100000).Count(i => i.PatientId == patient.Id);
        var files = AppSession.Attachments.ListByOwner("patient", patient.Id).Count();
        ScopeText.Text = $"{visits} visits · {invoices} invoices · {files} stored files will be erased.";
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += OnPrimary;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = NameBox.Text.Trim() == _patient.Name;

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        AppSession.Patients.HardDelete(_patient.Id);
        AppSession.Audit.Append(MedRecPlus.Core.Services.AuditEvents.PatientErased,
            "patient", _patient.Id.ToString(), AppSession.Actor, null);
        Tag = "erased";
        Hide();
    }
}
