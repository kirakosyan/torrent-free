namespace TorrentFree.Services;

public sealed class MauiFolderPickerService : IFolderPickerService
{
    public bool IsSupported
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var platformWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (platformWindow is null)
        {
            return null;
        }

        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.FileTypeFilter.Add("*");

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, windowHandle);

        var folder = await folderPicker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
#else
        return null;
#endif
    }
}