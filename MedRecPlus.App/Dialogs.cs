// Shared dialog + picker helpers for WinUI3 unpackaged.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MedRecPlus.App;

public static class Dialogs
{
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
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
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
        _ = dlg.ShowAsync();
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
