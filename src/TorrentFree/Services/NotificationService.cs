using Plugin.LocalNotification;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Local notification implementation using Plugin.LocalNotification.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private bool _permissionRequested;

    /// <summary>
    /// Check if local notifications are supported on this platform.
    /// </summary>
    private static bool IsSupported => LocalNotificationCenter.Current is not null;

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
        if (!IsSupported)
        {
            return;
        }

        await EnsurePermissionAsync();

        var title = "Download complete";
        var name = string.IsNullOrWhiteSpace(torrent.Name) ? "Your download" : torrent.Name;
        var body = $"{name} has finished downloading.";

        var request = new NotificationRequest
        {
            NotificationId = Math.Abs(torrent.Id.GetHashCode()),
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
