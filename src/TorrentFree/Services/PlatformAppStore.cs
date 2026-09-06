using Microsoft.Maui.ApplicationModel;
#if WINDOWS
using Windows.Services.Store;
#elif ANDROID
using Android.Gms.Extensions;
using Android.Content;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;
#endif

namespace TorrentFree.Services;

public sealed class PlatformAppStore : IAppStore
{
    private const string WindowsListing = "https://apps.microsoft.com/detail/9NNX2ZTPXC26";
    private const string AndroidListing = "https://play.google.com/store/apps/details?id=com.torrentfree.app";

    public bool IsSupported
    {
        get
        {
#if DEBUG
            // Development installations cannot reliably use production store APIs.
            return false;
#elif WINDOWS
            try { return Windows.ApplicationModel.Package.Current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store; }
            catch { return false; } // Unpackaged/sideloaded installations.
#elif ANDROID
            return true;
#else
            return false;
#endif
        }
    }

    public string InstalledVersion => $"{AppInfo.Current.VersionString}:{AppInfo.Current.BuildString}";

    public Task<AppUpdateAvailability> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!IsSupported) return AppUpdateAvailability.Unknown;
#if WINDOWS
            var context = CreateStoreContext();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            // Optional add-ons must not advertise an update to the app itself.
            return updates.Any(update => !update.Package.IsOptional)
                ? AppUpdateAvailability.Available : AppUpdateAvailability.Current;
#elif ANDROID
            using var manager = AppUpdateManagerFactory.Create(Android.App.Application.Context);
            using var info = await manager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>().WaitAsync(cancellationToken);
            return info.UpdateAvailability() switch
            {
                UpdateAvailability.UpdateAvailable => AppUpdateAvailability.Available,
                UpdateAvailability.UpdateNotAvailable => AppUpdateAvailability.Current,
                _ => AppUpdateAvailability.Unknown
            };
#else
            await Task.CompletedTask;
            return AppUpdateAvailability.Unknown;
#endif
        });

    public Task<bool> OpenListingAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
#if WINDOWS
        if (await TryLaunchAsync("ms-windows-store://pdp/?ProductId=9NNX2ZTPXC26")) return true;
        return await TryLaunchAsync(WindowsListing);
#elif ANDROID
        try
        {
            // Explicitly target Play; a third-party handler must not intercept the review link.
            using var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse("market://details?id=com.torrentfree.app"));
            intent.SetPackage("com.android.vending");
            intent.AddFlags(ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException) { }
        catch (Java.Lang.SecurityException) { }
        return await TryLaunchAsync(AndroidListing);
#else
        await Task.CompletedTask;
        return false;
#endif
    });

    public Task<AppReviewResult> RequestReviewAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
#if WINDOWS
        if (!IsSupported) return AppReviewResult.Failed;
        try
        {
            var result = await CreateStoreContext().RequestRateAndReviewAppAsync();
            return result.Status switch
            {
                StoreRateAndReviewStatus.Succeeded => AppReviewResult.Submitted,
                StoreRateAndReviewStatus.CanceledByUser => AppReviewResult.Canceled,
                _ => AppReviewResult.Failed
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"In-app review unavailable: {ex.Message}");
            return AppReviewResult.Failed;
        }
#elif ANDROID
        // Play review flow does not confirm submission (or even display). A visible Rate button
        // opens the listing instead; the caller records this as an opt-out, never a confirmed rating.
        return await OpenListingAsync() ? AppReviewResult.StoreOpened : AppReviewResult.Failed;
#else
        await Task.CompletedTask;
        return AppReviewResult.Failed;
#endif
    });

#if WINDOWS
    private static StoreContext CreateStoreContext()
    {
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No active store dialog window.");
        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return context;
    }
#endif

    private static async Task<bool> TryLaunchAsync(string uri)
    {
        try { return await Launcher.Default.OpenAsync(new Uri(uri)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Store launch failed: {ex.Message}");
            return false;
        }
    }
}
