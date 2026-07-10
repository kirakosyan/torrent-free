using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using TorrentFree.Services;

namespace TorrentFree;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "torrentfree_downloads";
    private int _timeoutHandled;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();

        TryStartForeground(notification);
        return StartCommandResult.NotSticky;
    }

    public override void OnTimeout(int startId)
    {
        System.Diagnostics.Debug.WriteLine("Download foreground service timed out.");
        StopAfterTimeout();
    }

    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        System.Diagnostics.Debug.WriteLine($"Download foreground service timed out for type {fgsType}.");
        StopAfterTimeout();
    }

    private bool TryStartForeground(Notification notification)
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            {
#pragma warning disable CA1416
                StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
#pragma warning restore CA1416
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            return true;
        }
        catch (ForegroundServiceStartNotAllowedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Android refused to start the dataSync foreground service: {ex}");
            StopSelf();
            return false;
        }
        catch (Java.Lang.RuntimeException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start download foreground service: {ex}");
            StopSelf();
            return false;
        }
    }

    private void StopAfterTimeout()
    {
        if (Interlocked.Exchange(ref _timeoutHandled, 1) != 0)
        {
            return;
        }

        // Android only grants a few seconds after a foreground-service timeout.
        // Stop the service unconditionally; StopSelf(startId) can leave it alive
        // if the service has received a newer start request or sticky restart.
        // Pause and persist the transfers through TorrentService as a fire-and-forget
        // operation. The service itself must be stopped synchronously before Android's
        // timeout grace period expires.
        _ = PauseTransfersAfterTimeoutAsync();
        StopSelf();
        StopForegroundSafely();
    }

    private static async Task PauseTransfersAfterTimeoutAsync()
    {
        try
        {
            var torrentService = MauiProgram.Services?.GetService<ITorrentService>();
            if (torrentService is null)
            {
                System.Diagnostics.Debug.WriteLine("Torrent service was unavailable after the foreground-service timeout.");
                return;
            }

            await torrentService.PauseAllForBackgroundTimeoutAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The foreground service has already been stopped as Android requires. Keep
            // this exception observed so a best-effort pause cannot crash the process.
            System.Diagnostics.Debug.WriteLine($"Failed to pause downloads after the foreground-service timeout: {ex}");
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        StopForegroundSafely();
        base.OnDestroy();
    }

    private void StopForegroundSafely()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
#pragma warning disable CA1416
                StopForeground(StopForegroundFlags.Remove);
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CA1422
                StopForeground(true);
#pragma warning restore CA1422
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to stop download foreground service notification: {ex}");
        }
    }

    private Notification BuildNotification()
    {
        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle(LocalizationResourceManager.Instance["AppTitle"]);
        builder.SetContentText(LocalizationResourceManager.Instance["BackgroundDownloadNotificationText"]);
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetVisibility(NotificationCompat.VisibilityPublic);
        builder.SetPriority((int)NotificationPriority.Low);

        return builder.Build()!;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

#pragma warning disable CA1416
        var channel = new NotificationChannel(
            ChannelId,
            LocalizationResourceManager.Instance["BackgroundDownloadChannelName"],
            NotificationImportance.Low)
        {
            Description = LocalizationResourceManager.Instance["BackgroundDownloadChannelDescription"]
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
#pragma warning restore CA1416
    }
}
