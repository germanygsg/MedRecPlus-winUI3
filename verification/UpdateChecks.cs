// This file is compiled ONLY into a temporary app copy, with isolated data/vault.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using ClosedXML.Excel;
using MedRecPlus.App.Security;
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
    private static readonly bool FullIdleWait = false;
    private static readonly bool Preview = false;
    private readonly List<string> _updateChecks = [];
    private void CheckUpdate(bool pass, string label)
    {
        _updateChecks.Add((pass ? "PASS " : "FAIL ") + label);
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "update-check-progress.txt"), _updateChecks);
    }

    public async void RunUpdateChecks()
    {
        try
        {
            Title = "Update verification — sample clinic";
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "update-sample-data", Guid.NewGuid().ToString("N"));
            var db = AppData.Setup("Update-Test-Password-42!", "Sample clinic", "Sample clinician");
            AppSession.Start(db, "Sample clinician");
            var patient = AppSession.Patients.Create(new Patient { RecordNumber = "PT-001", Name = "Dewi Sari – é", Age = 31, PhoneNumber = "081234567890" });
            var op = AppSession.Operators.GetAll().First().Id;
            var visit = AppSession.Visits.CreateVisit(patient, op, [], [new TreatmentItem { Name = "Consultation", PriceCents = 15000000 }], "none", 0, null, null, null, "sample");
            var invoice = AppSession.Invoices.GenerateFromAppointment(visit, "sample");
            AppSession.Invoices.RecordPayment(invoice.Id, 5000000, "cash", "Partial payment", "sample");
            EnterShell();
            await Task.Delay(250);
            var fired = 0;
            var service = new AutoLockService(TimeSpan.FromMilliseconds(300));
            service.AutoLocked += (_, _) => fired++;
            var watch = Stopwatch.StartNew();
            service.Start(); await Task.Delay(150); service.NotifyActivity(); await Task.Delay(220);
            CheckUpdate(fired == 0, "auto-lock: activity at 150 ms postpones the 300 ms deadline");
            await Task.Delay(160);
            CheckUpdate(fired == 1 && watch.ElapsedMilliseconds >= 450, "auto-lock: fires once after the reset deadline");
            service.Start(); service.Stop(); await Task.Delay(350);
            CheckUpdate(fired == 1, "auto-lock: Stop prevents an event and Start resets elapsed time");
            service = new AutoLockService(TimeSpan.Zero); service.AutoLocked += (_, _) => fired++;
            service.Start(); await Task.Delay(350); service.Stop();
            CheckUpdate(fired == 1, "auto-lock: zero timeout never fires");

            NavigateTo(typeof(SettingsPage)); await Task.Delay(250);
            var settings = (SettingsPage)ContentFrame.Content;
            var toggle = (ToggleSwitch)settings.FindName("RequirePasswordSwitch");
            var box = (ComboBox)settings.FindName("AutoLockBox");
            CheckUpdate(toggle.IsOn && box.SelectedIndex == 3, "settings: password ON and 15-minute defaults rendered");
            box.SelectedIndex = 0;
            CheckUpdate(_autoLock == null && LocalAppSettings.Load().AutoLockMinutes == 0, "settings: Never stops the service immediately");
            box.SelectedIndex = 1;
            CheckUpdate(_autoLock != null && LocalAppSettings.Load().AutoLockMinutes == 5, "settings: timeout change restarts the service immediately");
            await SaveUpdateVisual("settings-security.png");

            // Exercise the actual toggle handler against a synthetic password dialog.
            toggle.IsOn = false;
            await Task.Delay(150);
            var dialog = OpenUpdateDialogs().Single();
            var password = UpdateDescendants(dialog).OfType<PasswordBox>().Single();
            password.Password = "wrong";
            InvokeUpdatePrimary(dialog); await Task.Delay(200);
            CheckUpdate(dialog.IsLoaded && LocalAppSettings.Load().RequirePassword, "settings: wrong current password cannot disable the lock");
            password.Password = "Update-Test-Password-42!";
            InvokeUpdatePrimary(dialog); await Task.Delay(250);
            var vaultAvailable = !LocalAppSettings.Load().RequirePassword;
            CheckUpdate(vaultAvailable && !toggle.IsOn && !box.IsEnabled && box.SelectedIndex == 0 && _autoLock == null,
                "settings: verified toggle OFF stores key and forces Never in disabled picker");
            if (vaultAvailable)
            {
                AppSession.End(); ContentFrame.Content = null; Start(); await Task.Delay(200);
                CheckUpdate(AppSession.IsReady && ContentFrame.Content is DashboardPage, "startup: saved Windows key enters dashboard without password");
                NavigateTo(typeof(SettingsPage)); await Task.Delay(150);
                settings = (SettingsPage)ContentFrame.Content!;
                toggle = (ToggleSwitch)settings.FindName("RequirePasswordSwitch"); toggle.IsOn = true;
                await Task.Delay(150);
                CheckUpdate(LocalAppSettings.Load().RequirePassword && !CredentialStore.TryGetDbKey(out _) && _autoLock != null,
                    "settings: toggle ON deletes key and restores inactivity locking");
            }
            var exported = new ExcelExportService(AppSession.Db).ExportDataPointsWithCounts();
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "sample-data.xlsx"), exported.Bytes);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "sample-performance.xlsx"), new ExcelExportService(AppSession.Db).ExportOperatorPerformance(DateTime.Today.Year, null));
            NavigateTo(typeof(ReportsPage)); await Task.Delay(150);
            CheckUpdate(((ComboBox)((ReportsPage)ContentFrame.Content!).FindName("ExportYearBox")).Items.Count == 11, "reports: eleven export years and separate export controls");
            await SaveUpdateVisual("reports-export.png");

            // A popup must disappear with the locked page; stale patient data cannot survive.
            var openDialog = new ContentDialog { Title = "Sample patient dialog", Content = new TextBox { Text = "Synthetic patient" }, CloseButtonText = "Cancel", XamlRoot = ShellRoot.XamlRoot };
            _ = openDialog.ShowAsync(); await Task.Delay(150);
            var staleDb = AppSession.Db;
            LockDueToInactivity(); await Task.Delay(200);
            var activeWindow = App.Shell!;
            CheckUpdate(!AppSession.IsReady && activeWindow.AuthFrame.Content is UnlockPage && activeWindow.NavView.Visibility == Visibility.Collapsed && activeWindow.ContentFrame.Content == null,
                "lock: session ends, navigation is discarded and unlock screen appears");
            CheckUpdate(typeof(AppSession).GetProperties().Where(p => p.PropertyType != typeof(bool) && p.PropertyType != typeof(string)).All(p => p.GetValue(null) == null),
                "lock: database and every shared service reference cleared");
            CheckUpdate(!activeWindow.OpenUpdateDialogs().Any(d => d.IsLoaded && d.Visibility == Visibility.Visible), "lock: an open dialog cannot expose the abandoned patient page");
            try { staleDb.Open(); CheckUpdate(false, "lock: stale database is revoked"); }
            catch (ObjectDisposedException) { CheckUpdate(true, "lock: stale database is revoked"); }
            var unlocked = AppData.Unlock("Update-Test-Password-42!")!;
            CheckUpdate(new AuditService(unlocked).Query(AuditEvents.AppLocked).Any(a => a.MetaJson.Contains("auto")), "lock: audit recorded before teardown");
            AppSession.Start(unlocked, "sample"); activeWindow.EnterShell();
            CheckUpdate(activeWindow._autoLock != null && activeWindow.ContentFrame.Content is DashboardPage && !activeWindow.OpenUpdateDialogs().Any(), "unlock: fresh dashboard and restarted timer with no abandoned popups");
            activeWindow.LockDueToInactivity(); await Task.Delay(150);
            activeWindow = App.Shell!;
            var unlock = (UnlockPage)activeWindow.AuthFrame.Content;
            CheckUpdate(((TextBlock)unlock.FindName("LockMessage")).Text == "Locked due to inactivity.", "unlock: inactivity explanation rendered");
            await activeWindow.SaveUpdateVisual("auto-lock.png");
            ((PasswordBox)unlock.FindName("PasswordBox")).Password = "Update-Test-Password-42!";
            typeof(UnlockPage).GetMethod("TryUnlock", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(unlock, null);
            CheckUpdate(AppSession.IsReady && activeWindow._autoLock != null, "unlock: actual password handler rebuilds the session and timer");

            if (FullIdleWait)
            {
                LocalAppSettings.Save(new SecuritySettings { AutoLockMinutes = 5 }); activeWindow.ApplyAutoLockSettings();
                activeWindow.AppWindow.Hide();
                var start = DateTimeOffset.UtcNow;
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "idle-timestamps.txt"), $"Start {start:o}\n");
                await Task.Delay(TimeSpan.FromSeconds(330));
                activeWindow = App.Shell!;
                activeWindow.AppWindow.Show();
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "idle-timestamps.txt"), $"End {DateTimeOffset.UtcNow:o}\n");
                CheckUpdate(!AppSession.IsReady && activeWindow.AuthFrame.Content is UnlockPage, "five-minute idle: hidden unattended window locks with production setting after 330 seconds");
                await activeWindow.SaveUpdateVisual("five-minute-auto-lock.png");
            }
        }
        catch (Exception ex) { _updateChecks.Add("FAIL " + ex); }
        finally
        {
            CredentialStore.DeleteDbKey();
            _updateChecks.Add(_updateChecks.Any(s => s.StartsWith("FAIL")) ? "UPDATE_RESULT=FAIL" : "UPDATE_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "update-check-results.txt"), _updateChecks);
            if (Preview && AppSession.IsReady)
            {
                App.Shell!.Title = "Export preview — sample clinic";
                App.Shell.NavigateTo(typeof(ReportsPage));
            }
            else { App.Shell?.Close(); Application.Current.Exit(); }
        }
    }

    private IEnumerable<ContentDialog> OpenUpdateDialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)Content).XamlRoot)
        .SelectMany(p => UpdateDescendants(p.Child)).OfType<ContentDialog>();
    private static IEnumerable<DependencyObject> UpdateDescendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in UpdateDescendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void InvokeUpdatePrimary(ContentDialog dialog)
    {
        var button = UpdateDescendants(dialog).OfType<Button>().First(b => b.Name == "PrimaryButton");
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private async Task SaveUpdateVisual(string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)Content);
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
