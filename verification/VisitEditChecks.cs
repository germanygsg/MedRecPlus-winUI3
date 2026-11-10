// Compiled only into the isolated validation app by Build-ValidationChecks.ps1.
using System.Text.Json;
using Dapper;
using MedRecPlus.App.Views;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private async Task<ContentDialog> OpenVisitDialogFromPage(VisitDetailPage page)
    {
        InvokeHandler(page, "Edit_Click");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(50);
            var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(ShellRoot.XamlRoot)
                .Where(popup => popup.Child != null).SelectMany(popup => ValidationNodes(popup.Child))
                .OfType<ContentDialog>().FirstOrDefault();
            if (dialog != null) return dialog;
        }
        throw new InvalidOperationException("Edit visit did not open a dialog.");
    }

    private async Task RunVisitEditChecks(long patientId)
    {
        var savedOperator = AppSession.Operators.Create("Former operator", "Doctor");
        var activeOperator = AppSession.Operators.GetAll().First(op => op.Id != savedOperator).Id;
        var savedTreatment = AppSession.Treatments.Create("Saved treatment", null, 25000000, null);
        var removedTreatment = AppSession.Treatments.Create("Removed treatment", null, 5000000, null);
        var examId = AppSession.CustomExams.Create("Range of motion", "degrees");
        var id = AppSession.Visits.CreateVisit(patientId, savedOperator,
            new Dictionary<string, object?>
            {
                ["bloodPressure"] = "120/80", ["respirationRate"] = 22, ["heartRate"] = 60, ["borgScale"] = 4,
                [$"custom_{examId}"] = "90", ["custom_999999"] = "historical examination", ["legacyFlag"] = true,
            },
            [new TreatmentItem { Id = savedTreatment, Name = "Saved treatment", PriceCents = 25000000, Notes = "Keep these notes", Description = "Historical description" },
             new TreatmentItem { Id = removedTreatment, Name = "Removed treatment", PriceCents = 5000000, Notes = "Also keep" }],
            DiscountTypes.Fixed, 12500, "Original visit note", "Preserve visit description",
            [new IcdCode { Code = "1A00", Title = "Cholera", FoundationUri = "saved-foundation" }], AppSession.Actor);
        var original = AppSession.Visits.GetVisit(id);
        var count = AppSession.Appointments.Count(null, null);
        AppSession.Operators.SoftDelete(savedOperator);
        AppSession.Treatments.SoftDelete(removedTreatment);
        using (var conn = AppSession.Db.Open())
            conn.Execute("UPDATE treatments SET name = 'Changed catalog name', price_cents = 90000000 WHERE id = @id", new { id = savedTreatment });

        var cancel = new VisitDialog(visitToEdit: original) { RequestedTheme = ElementTheme.Light };
        await WithDialog(cancel, async () =>
        {
            CheckValidation(cancel.Title as string == "Edit visit" && cancel.PrimaryButtonText == "Save changes",
                "edit visit exposes save and cancel actions");
            CheckValidation(Field<TextBlock>(cancel, "PatientSelectedText").Text.Contains("Sample patient")
                && (long)((ComboBoxItem)Field<ComboBox>(cancel, "OperatorBox").SelectedItem).Tag == savedOperator,
                "edit visit loads its patient and saved operator even after operator deletion");
            CheckValidation(Field<TextBox>(cancel, "BpBox").Text == "120/80"
                && Field<NumberBox>(cancel, "RespBox").Value == 22
                && Field<NumberBox>(cancel, "HrBox").Value == 60
                && Field<NumberBox>(cancel, "BorgBox").Value == 4
                && Field<StackPanel>(cancel, "CustomExamsPanel").Children.OfType<TextBox>().Single().Text == "90",
                "edit visit prefills all vital signs and custom examinations");
            var treatments = Field<ListView>(cancel, "TreatmentList").SelectedItems.Cast<VisitDialog.TreatmentRow>().ToList();
            CheckValidation(treatments.Count == 2 && treatments[0].Name == "Saved treatment"
                && treatments[0].PriceCents == 25000000 && treatments[0].Notes == "Keep these notes"
                && treatments[1].Id == removedTreatment,
                "edit visit uses saved prices and notes and retains removed treatments");
            CheckValidation(Field<ItemsControl>(cancel, "IcdChips").Items.Cast<VisitDialog.IcdRow>().Single().Code2.FoundationUri == "saved-foundation"
                && Field<NumberBox>(cancel, "DiscountValueBox").Value == 12500
                && ((ComboBoxItem)Field<ComboBox>(cancel, "DiscountTypeBox").SelectedItem).Tag as string == DiscountTypes.Fixed
                && Field<TextBox>(cancel, "NotesBox").Text == "Original visit note",
                "edit visit loads diagnoses, whole-rupiah fixed discount and notes");
            await SaveValidationVisual(ValidationNodes(cancel).OfType<FrameworkElement>().First(element => element.Name == "BackgroundElement"), "visit-edit-light.png");
            Field<TextBox>(cancel, "NotesBox").Text = "Discard this";
            Field<ListView>(cancel, "TreatmentList").SelectedItems.Clear();
            await Press(cancel, "CloseButton");
        });
        CheckValidation(AppSession.Visits.GetVisit(id).Notes == "Original visit note"
            && AppSession.Visits.GetVisit(id).TreatmentsJson == original.TreatmentsJson
            && AppSession.Appointments.Count(null, null) == count, "cancelling edit leaves the stored visit unchanged");

        var unchanged = new VisitDialog(visitToEdit: AppSession.Visits.GetVisit(id));
        await WithDialog(unchanged, async () => await Press(unchanged, "PrimaryButton"));
        var roundTrip = AppSession.Visits.GetVisit(id);
        var roundTripTreatments = JsonSerializer.Deserialize<List<TreatmentItem>>(roundTrip.TreatmentsJson)!;
        CheckValidation(unchanged.Tag as string == "saved" && roundTrip.TotalPriceCents == 30000000
            && roundTrip.DiscountValueCents == 1250000 && roundTrip.Date == original.Date
            && roundTrip.CreatedAt == original.CreatedAt && roundTrip.Description == original.Description
            && roundTripTreatments[0].Description == "Historical description" && roundTripTreatments[0].Notes == "Keep these notes"
            && roundTrip.DiagnosisCodesJson == original.DiagnosisCodesJson,
            "saving without changes preserves identity, date, prices, discounts, descriptions and diagnosis metadata");

        var changedPatient = AppSession.Patients.Create(new Patient { Name = "Visit edit patient", RecordNumber = "V-EDIT" });
        NavigateTo(typeof(VisitDetailPage), id);
        await Task.Delay(150);
        var page = (VisitDetailPage)ContentFrame.Content;
        var edit = (VisitDialog)await OpenVisitDialogFromPage(page);
        try
        {
            InvokeHandler(edit, "ClearPatient_Click");
            Field<ComboBox>(edit, "OperatorBox").SelectedIndex = -1;
            await Press(edit, "PrimaryButton");
            CheckValidation(HasError(Field<TextBox>(edit, "PatientSearchBox")) && HasError(Field<ComboBox>(edit, "OperatorBox"))
                && AppSession.Visits.GetVisit(id).Notes == original.Notes, "edit validates required selections without saving");
            // Select through the same search results used by the patient picker.
            Field<TextBox>(edit, "PatientSearchBox").Text = "Visit edit patient";
            await Task.Delay(120);
            var result = Field<ListView>(edit, "PatientResults").Items.Cast<Patient>().Single();
            typeof(VisitDialog).GetField("_patient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(edit, result);
            typeof(VisitDialog).GetMethod("UpdatePatientSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(edit, null);
            Field<ComboBox>(edit, "OperatorBox").SelectedItem = Field<ComboBox>(edit, "OperatorBox").Items.Cast<ComboBoxItem>()
                .Single(item => (long)item.Tag == activeOperator);
            Field<TextBox>(edit, "BpBox").Text = "110/70";
            var heartRate = Field<NumberBox>(edit, "HrBox");
            heartRate.Focus(FocusState.Programmatic);
            ValidationNodes(heartRate).OfType<TextBox>().First().Text = "72";
            Field<StackPanel>(edit, "CustomExamsPanel").Children.OfType<TextBox>().Single().Text = "100";
            var treatmentList = Field<ListView>(edit, "TreatmentList");
            treatmentList.SelectedItems.Remove(treatmentList.SelectedItems.Cast<VisitDialog.TreatmentRow>().Single(row => row.Id == removedTreatment));
            InvokeHandler(edit, "NewTreatmentToggle_Click");
            Field<TextBox>(edit, "NewTreatNameBox").Text = "Added while editing";
            Field<TextBox>(edit, "NewTreatPriceBox").Text = "100000";
            InvokeHandler(edit, "NewTreatmentSave_Click");
            CheckValidation(treatmentList.SelectedItems.Cast<VisitDialog.TreatmentRow>().Single().Id == savedTreatment,
                "inline treatment creation keeps the existing visit selection");
            treatmentList.SelectedItems.Add(treatmentList.Items.Cast<VisitDialog.TreatmentRow>().Single(row => row.Name == "Added while editing"));
            await Press(edit, "✕");
            Field<ComboBox>(edit, "DiscountTypeBox").SelectedIndex = 1;
            Field<NumberBox>(edit, "DiscountValueBox").Value = 10;
            Field<TextBox>(edit, "NotesBox").Text = "Updated visit note";
            await SaveValidationVisual(ValidationNodes(edit).OfType<FrameworkElement>().First(element => element.Name == "BackgroundElement"), "visit-edit-dark.png");
            await Press(edit, "PrimaryButton");
        }
        finally { edit.Hide(); }
        await Task.Delay(180);
        var saved = AppSession.Visits.GetVisit(id);
        var vitals = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(saved.VitalSignsJson)!;
        CheckValidation(edit.Tag as string == "saved" && saved.Id == id && saved.Date == original.Date
            && saved.CreatedAt == original.CreatedAt && saved.Description == original.Description
            && AppSession.Appointments.Count(null, null) == count,
            "edit updates the existing appointment instead of creating another");
        CheckValidation(saved.PatientId == changedPatient && saved.PatientName == "Visit edit patient"
            && saved.OperatorId == activeOperator && saved.OperatorName == AppSession.Operators.GetAll().Single(op => op.Id == activeOperator).Name,
            "changing patient and operator updates their saved names");
        CheckValidation(vitals["bloodPressure"].GetString() == "110/70" && vitals["heartRate"].GetInt32() == 72
            && vitals[$"custom_{examId}"].GetString() == "100" && vitals["custom_999999"].GetString() == "historical examination"
            && vitals["legacyFlag"].GetBoolean(), "edit saves typed vitals and retains unshown historical examination values");
        CheckValidation(saved.TotalPriceCents == 35000000 && saved.DiscountType == DiscountTypes.Percent
            && saved.DiscountValueCents == 10 && saved.DiagnosisCodesJson == "[]" && saved.Notes == "Updated visit note",
            "edit saves treatment changes, recalculated subtotal, percent discount, diagnosis removal and notes");
        CheckValidation(Field<TextBlock>(page, "TitleText").Text == "Visit edit patient"
            && Field<TextBlock>(page, "NotesText").Text.Contains("Updated visit note")
            && Field<TextBlock>(page, "TotalText").Text.Contains(UiHelpers.Money(31500000))
            && page.TreatmentRows.Count == 2, "visit details refresh with saved values and payable total");
        ShellRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
        await SaveValidationVisual(ShellRoot, "visit-edit-saved.png");

        var removeDiscount = new VisitDialog(visitToEdit: saved);
        await WithDialog(removeDiscount, async () =>
        {
            CheckValidation(Field<NumberBox>(removeDiscount, "DiscountValueBox").Value == 10,
                "reopening edit loads the saved percent discount");
            InvokeHandler(removeDiscount, "RemoveDiscount_Click");
            await Press(removeDiscount, "PrimaryButton");
        });
        CheckValidation(AppSession.Visits.GetVisit(id).DiscountType == DiscountTypes.None
            && AppSession.Visits.GetVisit(id).DiscountValueCents == 0, "removing the discount clears its stored value");

        var staleEditor = new VisitDialog(visitToEdit: AppSession.Visits.GetVisit(id));
        var invoice = AppSession.Invoices.GenerateFromAppointment(id, AppSession.Actor);
        await WithDialog(staleEditor, async () =>
        {
            Field<TextBox>(staleEditor, "NotesBox").Text = "Must not save after invoice";
            await Press(staleEditor, "PrimaryButton");
            CheckValidation(staleEditor.Tag == null && Field<TextBlock>(staleEditor, "ErrorText").Visibility == Visibility.Visible
                && AppSession.Visits.GetVisit(id).Notes == "Updated visit note", "an editor opened before invoicing cannot save a frozen visit");
        });
        var notes = JsonSerializer.Deserialize<List<TreatmentItem>>(AppSession.Visits.GetVisit(id).TreatmentsJson)!;
        notes[0].Notes = "Allowed after invoice";
        AppSession.Visits.UpdateTreatmentNotes(id, notes, AppSession.Actor);
        CheckValidation(JsonSerializer.Deserialize<List<TreatmentItem>>(AppSession.Visits.GetVisit(id).TreatmentsJson)![0].Notes == "Allowed after invoice"
            && AppSession.Invoices.GetInvoice(invoice.Id).TreatmentsJson == invoice.TreatmentsJson,
            "frozen visits still allow treatment notes and leave the invoice snapshot unchanged");
        // Force a fresh visit page, as when returning from the invoice screen.
        ContentFrame.Navigate(typeof(VisitDetailPage), id);
        await Task.Delay(150);
        var frozenPage = (VisitDetailPage)ContentFrame.Content;
        var frozen = await OpenVisitDialogFromPage(frozenPage);
        CheckValidation(frozen is not VisitDialog && frozen.Title as string == "Frozen"
            && Field<TextBlock>(frozenPage, "FrozenNote").Visibility == Visibility.Visible,
            "invoiced visit shows the frozen banner and explains the edit restriction");
        frozen.Hide();
        await Task.Delay(150);
        CheckValidation(AppSession.Audit.VerifyChain(), "appointment edits retain a valid audit chain");
    }
}
