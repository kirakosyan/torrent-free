using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Local notification implementation using Plugin.LocalNotification.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private bool _permissionRequested;
    private static string? _pendingTorrentId;
    private static int _launchNotificationHandled;
    internal static event Action? DownloadNotificationTapped;
    private static bool IsSupported =>
#if ANDROID || IOS
        LocalNotificationCenter.Current is not null;
#else
        false;
#endif

    public NotificationService()
    {
        if (IsSupported)
        {
            LocalNotificationCenter.Current.NotificationActionTapped += args =>
            {
                if (!args.IsTapped || string.IsNullOrWhiteSpace(args.Request.ReturningData)) return;
                Interlocked.Exchange(ref _pendingTorrentId, args.Request.ReturningData);
                DownloadNotificationTapped?.Invoke();
            };
        }
    }

    internal static string? TakePendingTorrentId()
    {
        if (IsSupported
            && LocalNotificationCenter.LaunchNotificationDetails is { DidNotificationLaunchApp: true } launch
            && Interlocked.Exchange(ref _launchNotificationHandled, 1) == 0)
        {
            // The platform may publish a cold-start tap before the page subscribes.
            // A more recent live tap takes precedence over the launch notification.
            Interlocked.CompareExchange(ref _pendingTorrentId, launch.Request?.ReturningData, null);
        }

        return Interlocked.Exchange(ref _pendingTorrentId, null);
    }

    public async Task EnsurePermissionAsync()
    {
        if (_permissionRequested || !IsSupported)
        {
            return;
        }

        _permissionRequested = true;
        _ = await LocalNotificationCenter.Current.RequestNotificationPermission();
    }

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

        var request = new NotificationRequest
        {
            NotificationId = torrent.Id.GetHashCode() & 0x7FFFFFFF,
            Title = title,
            Description = body,
            ReturningData = torrent.Id
        };

        await LocalNotificationCenter.Current.Show(request);
    }
}
