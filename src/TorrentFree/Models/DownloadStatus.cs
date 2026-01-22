namespace TorrentFree.Models;

/// <summary>
/// Represents the download status of a torrent item.
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// Download is queued and waiting to start.
    /// </summary>
    Queued,

    /// <summary>
    /// Download is currently in progress.
    /// </summary>
    Downloading,

    /// <summary>
    /// Download is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Download has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Download has failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Download has been stopped/cancelled.
    /// </summary>
    Stopped
}
