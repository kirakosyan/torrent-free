using Android.Content;
using TorrentFree.Services;

namespace TorrentFree;

/// <summary>
/// Android implementation that keeps downloads alive using a foreground service.
/// </summary>
public sealed class AndroidBackgroundDownloadService : IBackgroundDownloadService
{
    public bool Start()
    {
        DownloadForegroundService.RequestRunning(true);
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(DownloadForegroundService));

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
#pragma warning disable CA1416
                context.StartForegroundService(intent);
#pragma warning restore CA1416
            }
            else
            {
                context.StartService(intent);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Android 12+ refuses foreground-service starts while the app is in the background.
            // TorrentService retries when the app returns to the foreground.
            System.Diagnostics.Debug.WriteLine($"Failed to start foreground service: {ex}");
            DownloadForegroundService.RequestRunning(false);
            return false;
        }
    }

    public void Stop()
    {
        // Stopping a service started with StartForegroundService before it has called
        // StartForeground crashes the app on several Android versions. A service which has not
        // reached the foreground yet sees the cleared request and stops itself once it does.
        if (!DownloadForegroundService.RequestRunning(false))
        {
            return;
        }

        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(DownloadForegroundService));
            context.StopService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to stop foreground service: {ex}");
        }
    }

    public void SetKeepDeviceAwake(bool enabled) => TransferWakeLock.SetEnabled(enabled);
}
