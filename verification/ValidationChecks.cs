// Compiled only into a temporary app copy by Build-ValidationChecks.ps1.
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
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
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private readonly List<string> _validationChecks = [];
    private void CheckValidation(bool passed, string label) =>
        _validationChecks.Add((passed ? "PASS " : "FAIL ") + label);
    private static IEnumerable<DependencyObject> ValidationNodes(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ValidationNodes(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T Field<T>(FrameworkElement root, string name) where T : FrameworkElement => (T)root.FindName(name);
    private static TextBlock FieldMessage(Control field) =>
        ((Panel)field.Parent).Children.OfType<TextBlock>().Single();
    private static bool HasError(Control field) => FieldMessage(field).Visibility == Visibility.Visible;
    private static void InvokeHandler(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, [null, new RoutedEventArgs()]);
    private static async Task Press(DependencyObject root, string name)
    {
        var button = ValidationNodes(root).OfType<Button>().First(b => b.Name == name || b.Content as string == name);
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(120);
    }
    private async Task WithDialog(ContentDialog dialog, Func<Task> check)
    {
        dialog.XamlRoot = ShellRoot.XamlRoot;
        var showing = dialog.ShowAsync();
        await Task.Delay(180);
        try { await check(); }
        finally { dialog.Hide(); await showing; }
    }
    private static void CheckAdjacent(Control field)
    {
        var message = FieldMessage(field);
        var fieldPoint = field.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
        var errorPoint = message.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
        if (Math.Abs(fieldPoint.X - errorPoint.X) > 1 ||
            errorPoint.Y < fieldPoint.Y + field.ActualHeight - 1 ||
            errorPoint.Y > fieldPoint.Y + field.ActualHeight + 8)
            throw new InvalidOperationException("A field message is detached from its input.");
    }

    public async void RunValidationChecks()
    {
        try
        {
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "validation-sample-data");
            AppData.EnsureCreated();
            var db = new Database(Path.Combine(AppData.BaseDir, Guid.NewGuid() + ".db"), new string('a', 64));
            db.Initialize();
            AppSession.Start(db, "validation checks");
            var patientId = AppSession.Patients.Create(new Patient { Name = "Sample patient", RecordNumber = "V001", Age = 23 });
            AppSession.Operators.Create("Sample doctor", "Doctor");
            var visitId = AppSession.Visits.CreateVisit(patientId, AppSession.Operators.GetAll().First().Id, [],
                [new TreatmentItem { Name = "Consultation", PriceCents = 100000 }], DiscountTypes.None, 0, null, null, null, AppSession.Actor);
            var invoice = AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
            EnterShell();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 850));
            await Task.Delay(250);

            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                ShellRoot.RequestedTheme = theme;
                var patient = new PatientDialog { RequestedTheme = theme };
                await WithDialog(patient, async () =>
                {
                    await Press(patient, "PrimaryButton");
                    var name = Field<TextBox>(patient, "NameBox");
                    var age = Field<NumberBox>(patient, "AgeBox");
                    var basis = Field<ComboBox>(patient, "BasisBox");
                    CheckValidation(HasError(name) && HasError(age) && HasError(basis) && patient.Tag == null,
                        $"patient required errors appear together and prevent saving/{theme}");
                    CheckAdjacent(name); CheckAdjacent(age); CheckAdjacent(basis);
                    CheckValidation(Field<TextBlock>(patient, "ErrorText").Visibility == Visibility.Collapsed,
                        $"patient field errors do not appear in the form summary/{theme}");
                    name.Text = "Validation patient " + theme;
                    basis.SelectedIndex = 0;
                    await Task.Delay(100);
                    CheckValidation(!HasError(name) && !HasError(basis) && HasError(age),
                        $"correcting one field clears only its error/{theme}");
                    await SaveValidationVisual(ValidationNodes(patient).OfType<FrameworkElement>().First(e => e.Name == "BackgroundElement"),
                        $"patient-inline-errors-{theme}.png");
                    var editor = ValidationNodes(age).OfType<TextBox>().First();
                    editor.Focus(FocusState.Programmatic);
                    editor.Text = "23";
                    await Task.Delay(100);
                    CheckValidation(!HasError(age), $"editing age clears its error before leaving the field/{theme}");
                    await Press(patient, "PrimaryButton");
                    CheckValidation(patient.Tag as string == "saved" && AppSession.Patients.Search(name.Text).Single().Age == 23,
                        $"patient saves the currently typed age/{theme}");
                });
            }

            var treatment = new TreatmentDialog();
            await WithDialog(treatment, async () =>
            {
                await Press(treatment, "PrimaryButton");
                var name = Field<TextBox>(treatment, "NameBox");
                var price = Field<TextBox>(treatment, "PriceBox");
                CheckValidation(HasError(name) && HasError(price), "treatment requires name and price beside each field");
                name.Text = "Validation treatment"; price.Text = "300000";
                await Press(treatment, "PrimaryButton");
                CheckValidation(treatment.Tag as string == "saved" && AppSession.Treatments.GetAll().Single().PriceCents == 30000000,
                    "corrected treatment saves with whole-rupiah conversion preserved");
            });

            var payment = new PaymentDialog(invoice);
            await WithDialog(payment, async () =>
            {
                var amount = Field<NumberBox>(payment, "AmountBox");
                var editor = ValidationNodes(amount).OfType<TextBox>().First();
                editor.Text = "2000";
                await Press(payment, "PrimaryButton");
                CheckValidation(HasError(amount) && AppSession.Invoices.GetInvoice(invoice.Id).PaidCents == 0,
                    "excess payment is reported beside amount and not recorded");
                editor.Text = "50";
                await Press(payment, "PrimaryButton");
                CheckValidation(payment.Tag as string == "saved" && AppSession.Invoices.GetInvoice(invoice.Id).PaidCents == 5000,
                    "payment saves the currently typed amount after correction");
            });

            var visit = new VisitDialog();
            await WithDialog(visit, async () =>
            {
                var op = Field<ComboBox>(visit, "OperatorBox"); op.SelectedIndex = -1;
                await Press(visit, "PrimaryButton");
                CheckValidation(HasError(Field<TextBox>(visit, "PatientSearchBox")) && HasError(op),
                    "visit selection errors appear at patient and operator");
                InvokeHandler(visit, "NewTreatmentToggle_Click");
                InvokeHandler(visit, "NewTreatmentSave_Click");
                CheckValidation(HasError(Field<TextBox>(visit, "NewTreatNameBox")) && HasError(Field<TextBox>(visit, "NewTreatPriceBox")),
                    "inline new treatment has separate name and price errors");
            });
            var selectedVisit = new VisitDialog(patientId);
            await WithDialog(selectedVisit, async () =>
            {
                await Press(selectedVisit, "PrimaryButton");
                CheckValidation(selectedVisit.Tag as string == "saved", "visit saves with a selected patient and operator");
            });

            var setup = new SetupPage(); ContentFrame.Content = setup;
            await Task.Delay(100); InvokeHandler(setup, "Setup_Click");
            CheckValidation(new[] { "ClinicNameBox", "OperatorNameBox", "PasswordBox", "ConfirmBox" }
                .All(name => HasError(Field<Control>(setup, name))), "setup reports every missing required field locally");
            Field<PasswordBox>(setup, "PasswordBox").Password = "ValidPass1!";
            Field<PasswordBox>(setup, "ConfirmBox").Password = "mismatch";
            await Task.Delay(100);
            CheckValidation(HasError(Field<PasswordBox>(setup, "ConfirmBox")), "password mismatch belongs to confirmation");
            Field<PasswordBox>(setup, "ConfirmBox").Password = "ValidPass1!";
            await Task.Delay(100);
            CheckValidation(!HasError(Field<PasswordBox>(setup, "ConfirmBox")), "corrected password confirmation clears its message");

            var unlock = new UnlockPage(); ContentFrame.Content = unlock;
            await Task.Delay(100); InvokeHandler(unlock, "Unlock_Click");
            CheckValidation(HasError(Field<PasswordBox>(unlock, "PasswordBox")), "unlock requires its password next to the input");

            NavigateTo(typeof(SettingsPage)); await Task.Delay(100);
            var settings = (SettingsPage)ContentFrame.Content;
            InvokeHandler(settings, "ChangePw_Click");
            CheckValidation(new[] { "CurrentPwBox", "NewPwBox", "ConfirmPwBox" }.All(name => HasError(Field<PasswordBox>(settings, name))),
                "password settings report required values at all three inputs");
            Field<TextBox>(settings, "ClinicNameBox").Text = "";
            InvokeHandler(settings, "SaveClinic_Click");
            CheckValidation(HasError(Field<TextBox>(settings, "ClinicNameBox")), "clinic name is validated locally");
            foreach (var handler in new[] { "AddOperator_Click", "AddExam_Click" })
            {
                InvokeHandler(settings, handler); await Task.Delay(200);
                var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(ShellRoot.XamlRoot)
                    .Where(p => p.Child != null).SelectMany(p => ValidationNodes(p.Child)).OfType<ContentDialog>().First();
                await Press(dialog, "PrimaryButton");
                var name = ValidationNodes(dialog).OfType<TextBox>().First();
                CheckValidation(HasError(name), $"{handler} keeps the dialog open and identifies its missing name");
                dialog.Hide(); await Task.Delay(100);
            }
        }
        catch (Exception ex) { _validationChecks.Add("FAIL " + ex); }
        finally
        {
            _validationChecks.Add(_validationChecks.Any(line => line.StartsWith("FAIL")) ? "VALIDATION_RESULT=FAIL" : "VALIDATION_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "validation-check-results.txt"), _validationChecks);
            Application.Current.Exit();
        }
    }

    private static async Task SaveValidationVisual(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
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
