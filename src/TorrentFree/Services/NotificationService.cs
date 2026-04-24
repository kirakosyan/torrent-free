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
    private static bool IsSupported =>
#if ANDROID || IOS
        LocalNotificationCenter.Current is not null;
#else
        false;
#endif

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
            ReturningData = torrent.Id,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = DateTime.Now
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }
}
