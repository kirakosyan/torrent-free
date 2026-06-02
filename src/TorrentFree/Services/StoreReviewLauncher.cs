using Microsoft.Maui.ApplicationModel;

namespace TorrentFree.Services;

public interface IStoreReviewLauncher
{
    bool IsSupported { get; }

    Task<bool> OpenReviewPageAsync();
}

public sealed class StoreReviewLauncher : IStoreReviewLauncher
{
    private const string AndroidPackageId = "com.torrentfree.app";
    private const string WindowsStoreProductId = "9nnx2ztpxc26";

    public bool IsSupported =>
        DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.WinUI;

    public async Task<bool> OpenReviewPageAsync()
    {
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            return await TryOpenAsync($"market://details?id={AndroidPackageId}")
                || await TryOpenAsync($"https://play.google.com/store/apps/details?id={AndroidPackageId}");
        }

        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            return await TryOpenAsync($"ms-windows-store://review/?ProductId={WindowsStoreProductId}")
                || await TryOpenAsync($"https://apps.microsoft.com/detail/{WindowsStoreProductId}");
        }

        return false;
    }

    private static async Task<bool> TryOpenAsync(string uri)
    {
        try
        {
            return await Launcher.Default.OpenAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Store review launch failed for '{uri}': {ex.Message}");
            return false;
        }
    }
}
