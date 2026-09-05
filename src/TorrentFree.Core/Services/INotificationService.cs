using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Abstraction for local notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Ensures notification permission is granted where required.
    /// </summary>
    Task EnsurePermissionAsync();

    /// <summary>
    /// Shows a download completed notification for a torrent.
    /// </summary>
    Task ShowDownloadCompletedAsync(TorrentItem torrent);
}
