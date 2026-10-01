using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using TorrentFree.Models;
#if WINDOWS
using Microsoft.Windows.AppNotifications;
#endif

namespace TorrentFree.Services;

/// <summary>
/// Local download notifications using the platform's notification service.
/// </summary>
public sealed class NotificationService : INotificationService
{
#if ANDROID || IOS
    private bool _permissionRequested;
    private static int _launchNotificationHandled;
#endif
    private static string? _pendingTorrentId;
    internal static event Action? DownloadNotificationTapped;
    private static bool IsSupported =>
#if ANDROID || IOS
        LocalNotificationCenter.Current is not null;
#elif WINDOWS
        true;
#else
        false;
#endif

    public NotificationService()
    {
#if ANDROID || IOS
        if (IsSupported)
        {
            LocalNotificationCenter.Current.NotificationActionTapped += args =>
            {
                if (!args.IsTapped || string.IsNullOrWhiteSpace(args.Request.ReturningData)) return;
                QueueTorrentSelection(args.Request.ReturningData);
            };
        }
#endif
    }

    internal static void QueueTorrentSelection(string torrentId)
    {
        Interlocked.Exchange(ref _pendingTorrentId, torrentId);
        DownloadNotificationTapped?.Invoke();
    }

    internal static string? TakePendingTorrentId()
    {
#if ANDROID || IOS
        if (IsSupported
            && LocalNotificationCenter.LaunchNotificationDetails is { DidNotificationLaunchApp: true } launch
            && Interlocked.Exchange(ref _launchNotificationHandled, 1) == 0)
        {
            // The platform may publish a cold-start tap before the page subscribes.
            // A more recent live tap takes precedence over the launch notification.
            Interlocked.CompareExchange(ref _pendingTorrentId, launch.Request?.ReturningData, null);
        }
#endif

        return Interlocked.Exchange(ref _pendingTorrentId, null);
    }

    public Task EnsurePermissionAsync()
    {
#if ANDROID || IOS
        return RequestPermissionAsync();
#else
        // Windows notification permission is controlled by the system's per-app settings.
        return Task.CompletedTask;
#endif
    }

#if ANDROID || IOS
    private async Task RequestPermissionAsync()
    {
        if (_permissionRequested || !IsSupported)
        {
            return;
        }

        _permissionRequested = true;
        _ = await LocalNotificationCenter.Current.RequestNotificationPermission();
    }
#endif

    public async Task ShowDownloadCompletedAsync(TorrentItem torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        if (!IsSupported)
        {
            return;
        }

        await EnsurePermissionAsync();

        var title = LocalizationResourceManager.Instance["NotificationDownloadComplete"];
        var name = string.IsNullOrWhiteSpace(torrent.Name)
            ? LocalizationResourceManager.Instance["NotificationYourDownload"]
            : torrent.Name;
        var body = string.Format(
            LocalizationResourceManager.Instance["NotificationDownloadCompletedBody"],
            name);

#if WINDOWS
        var notification = new AppNotification(DownloadCompletionNotification.CreatePayload(torrent.Id, title, body))
        {
            Tag = DownloadCompletionNotification.GetTag(torrent.Id),
            Group = "downloads"
        };
        AppNotificationManager.Default.Show(notification);
#elif ANDROID || IOS
        var request = new NotificationRequest
        {
            NotificationId = torrent.Id.GetHashCode() & 0x7FFFFFFF,
            Title = title,
            Description = body,
            ReturningData = torrent.Id
        };

        await LocalNotificationCenter.Current.Show(request);
#endif
    }
}
