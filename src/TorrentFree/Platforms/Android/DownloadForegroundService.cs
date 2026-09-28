using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using DownloadStatus = TorrentFree.Models.DownloadStatus;
using TorrentFree.Services;

namespace TorrentFree;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "torrentfree_downloads";
    private static readonly object StateGate = new();
    private static bool _runRequested;
    private static bool _inForeground;
    private int _timeoutHandled;
    private CancellationTokenSource? _notificationUpdates;

    /// <summary>
    /// Records whether active transfers need this service.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the caller must stop a service which is already in the
    /// foreground. A service which has not reached the foreground yet stops itself after
    /// calling StartForeground, so it is never stopped before satisfying that requirement.
    /// </returns>
    internal static bool RequestRunning(bool running)
    {
        lock (StateGate)
        {
            _runRequested = running;
            return !running && _inForeground;
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();

        if (!TryStartForeground(notification))
        {
            return StartCommandResult.NotSticky;
        }

        bool stopRequested;
        lock (StateGate)
        {
            _inForeground = true;
            stopRequested = !_runRequested;
        }

        if (stopRequested)
        {
            // Transfers ended before this service reached the foreground.
            StopForegroundSafely();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        TransferWakeLock.SetServiceActive(true);
        if (_notificationUpdates is null)
        {
            _notificationUpdates = new CancellationTokenSource();
            _ = RefreshNotificationAsync(_notificationUpdates.Token);
        }
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
        TransferWakeLock.SetServiceActive(false);
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
        lock (StateGate)
        {
            _inForeground = false;
        }

        TransferWakeLock.SetServiceActive(false);
        StopForegroundSafely();
        base.OnDestroy();
    }

    private void StopForegroundSafely()
    {
        var updates = Interlocked.Exchange(ref _notificationUpdates, null);
        updates?.Cancel();
        updates?.Dispose();
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
        var torrents = MauiProgram.Services?.GetService<ITorrentService>()?.Torrents;
        var downloading = torrents?.Where(torrent => torrent.Status == DownloadStatus.Downloading).ToArray() ?? [];
        if (downloading.Length > 0)
        {
            var totalSize = downloading.Sum(torrent => (double)Math.Max(0, torrent.TotalSize));
            var downloadedSize = downloading.Sum(torrent => (double)Math.Clamp(torrent.DownloadedSize, 0, Math.Max(0, torrent.TotalSize)));
            var indeterminate = downloading.Any(torrent => torrent.TotalSize <= 0);
            var progress = totalSize > 0 ? Math.Clamp(downloadedSize / totalSize * 100, 0, 100) : 0;
            var status = $"{LocalizationResourceManager.Instance["StatusDownloading"]}: {downloading.Length}";
            builder.SetContentText(indeterminate ? status : $"{status} · {progress:F1}%");
            builder.SetProgress(100, (int)progress, indeterminate);
        }
        else if (torrents?.Count(torrent => torrent.Status == DownloadStatus.Seeding) is > 0 and var seeding)
        {
            builder.SetContentText($"{LocalizationResourceManager.Instance["StatusSeeding"]}: {seeding}");
        }
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetVisibility(NotificationCompat.VisibilityPublic);
        builder.SetPriority((int)NotificationPriority.Low);
        if (CreateOpenAppIntent() is { } openApp)
        {
            builder.SetContentIntent(openApp);
        }

        return builder.Build()!;
    }

    private async Task RefreshNotificationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // Torrent collections and properties are published on the MAUI UI thread.
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    var manager = (NotificationManager?)GetSystemService(NotificationService);
                    using var notification = BuildNotification();
                    manager?.Notify(NotificationId, notification);
                });
            }
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Refreshing transfer notification failed: {ex}");
        }
    }

    private PendingIntent? CreateOpenAppIntent()
    {
        try
        {
            // Same as a launcher tap: brings the existing task to the front.
            var launch = new Intent(this, typeof(MainActivity));
            launch.SetAction(Intent.ActionMain);
            launch.AddCategory(Intent.CategoryLauncher);
            launch.AddFlags(ActivityFlags.SingleTop);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                flags |= PendingIntentFlags.Immutable;
            }

            return PendingIntent.GetActivity(this, 0, launch, flags);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not create the open-app notification intent: {ex}");
            return null;
        }
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
