using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MedRecPlus.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Single-instance: a second launch redirects to the running instance (same as the
/// old Tauri app's single-instance plugin).
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static MainWindow? Shell => ((App)Current)._window as MainWindow;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "MedRecPlusOffline");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash.log"),
                    System.DateTime.Now.ToString("o") + "\n" + e.Exception.ToString());
            }
            catch { }
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instance = AppInstance.FindOrRegisterForKey("MedRecPlusOffline");
        if (!instance.IsCurrent)
        {
            // Another instance is running — hand it this activation and exit.
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return;
        }
        instance.Activated += OnActivated;

        var shell = new MainWindow();
        _window = shell;
        shell.Activate();
        shell.Start();
    }

    private void OnActivated(object? sender, AppActivationArguments e)
    {
        // A redirected second launch lands here: just bring the window forward.
        _window?.Activate();
    }
}
