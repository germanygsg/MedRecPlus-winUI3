// Shared dialog + picker helpers for WinUI3 unpackaged.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MedRecPlus.App;

public static class Dialogs
{
    public static async Task<ContentDialogResult> Show(ContentDialog dialog)
    {
        // Popups have their own event route. Listen on the dialog too, including
        // input handled by an editor, so writing a long clinical note is activity.
        var db = AppSession.IsReady ? AppSession.Db : null;
        PointerEventHandler pointer = (_, _) => App.Shell?.NotifyActivity();
        KeyEventHandler key = (_, _) => App.Shell?.NotifyActivity();
        dialog.AddHandler(UIElement.PointerMovedEvent, pointer, true);
        dialog.AddHandler(UIElement.PointerPressedEvent, pointer, true);
        dialog.AddHandler(UIElement.KeyDownEvent, key, true);
        try
        {
            var result = await dialog.ShowAsync();
            return db != null && (!AppSession.IsReady || !ReferenceEquals(db, AppSession.Db))
                ? ContentDialogResult.None : result;
        }
        catch (Exception) when (db != null && (!AppSession.IsReady || !ReferenceEquals(db, AppSession.Db)))
        {
            return ContentDialogResult.None; // Old native window was abandoned by auto-lock.
        }
        finally
        {
            dialog.RemoveHandler(UIElement.PointerMovedEvent, pointer);
            dialog.RemoveHandler(UIElement.PointerPressedEvent, pointer);
            dialog.RemoveHandler(UIElement.KeyDownEvent, key);
        }
    }

    public static async Task<bool> Confirm(XamlRoot root, string title, string content,
        string ok = "Delete")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = ok,
            CloseButtonText = "Cancel",
            XamlRoot = root,
        };
        if (ok == "Delete")
            dlg.PrimaryButtonStyle = (Style)Application.Current.Resources["DeleteConfirmationButtonStyle"];
        return await Show(dlg) == ContentDialogResult.Primary;
    }

    public static void Alert(XamlRoot root, string title, string content)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        _ = Show(dlg);
    }

    public static async Task<string?> PickImageFile(XamlRoot _)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.Shell);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> SaveFile(XamlRoot _, string suggestedName, string extension)
    {
        var picker = new FileSavePicker();
        var hwnd = WindowNative.GetWindowHandle(App.Shell);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file",
            new List<string> { extension });
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
