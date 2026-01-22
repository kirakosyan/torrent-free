using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFree.Models;

/// <summary>
/// Represents a torrent download item.
/// </summary>
public partial class TorrentItem : ObservableObject
{
    /// <summary>
    /// Unique identifier for the torrent item.
    /// </summary>
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString();

    /// <summary>
    /// Name of the torrent/file being downloaded.
    /// </summary>
    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>
    /// The magnet link or torrent URL.
    /// </summary>
    [ObservableProperty]
    private string magnetLink = string.Empty;

    /// <summary>
    /// Total size of the download in bytes.
    /// </summary>
    [ObservableProperty]
    private long totalSize;

    /// <summary>
    /// Downloaded size in bytes.
    /// </summary>
    [ObservableProperty]
    private long downloadedSize;

    /// <summary>
    /// Current download progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double progress;

    /// <summary>
    /// Current download status.
    /// </summary>
    [ObservableProperty]
    private DownloadStatus status = DownloadStatus.Queued;

    /// <summary>
    /// Download speed in bytes per second.
    /// </summary>
    [ObservableProperty]
    private long downloadSpeed;

    /// <summary>
    /// Upload speed in bytes per second.
    /// </summary>
    [ObservableProperty]
    private long uploadSpeed;

    /// <summary>
    /// Number of seeders connected.
    /// </summary>
    [ObservableProperty]
    private int seeders;

    /// <summary>
    /// Number of leechers connected.
    /// </summary>
    [ObservableProperty]
    private int leechers;

    /// <summary>
    /// Date and time when the torrent was added.
    /// </summary>
    [ObservableProperty]
    private DateTime dateAdded = DateTime.Now;

    /// <summary>
    /// Date and time when the download completed.
    /// </summary>
    [ObservableProperty]
    private DateTime? dateCompleted;

    /// <summary>
    /// Local file path where the download is saved.
    /// </summary>
    [ObservableProperty]
    private string savePath = string.Empty;

    /// <summary>
    /// Gets the formatted download speed string.
    /// </summary>
    public string FormattedDownloadSpeed => FormatBytes(DownloadSpeed) + "/s";

    /// <summary>
    /// Gets the formatted upload speed string.
    /// </summary>
    public string FormattedUploadSpeed => FormatBytes(UploadSpeed) + "/s";

    /// <summary>
    /// Gets the formatted total size string.
    /// </summary>
    public string FormattedTotalSize => FormatBytes(TotalSize);

    /// <summary>
    /// Gets the formatted downloaded size string.
    /// </summary>
    public string FormattedDownloadedSize => FormatBytes(DownloadedSize);

    /// <summary>
    /// Gets the status display text.
    /// </summary>
    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Downloading => $"Downloading - {Progress:F1}%",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Stopped => "Stopped",
        _ => "Unknown"
    };

    /// <summary>
    /// Indicates whether the download can be started or resumed.
    /// </summary>
    public bool CanStart => Status is DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Stopped or DownloadStatus.Failed;

    /// <summary>
    /// Indicates whether the download can be paused.
    /// </summary>
    public bool CanPause => Status == DownloadStatus.Downloading;

    /// <summary>
    /// Indicates whether the download can be stopped.
    /// </summary>
    public bool CanStop => Status is DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Queued;

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnStatusChanged(DownloadStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnDownloadSpeedChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedDownloadSpeed));
    }

    partial void OnUploadSpeedChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedUploadSpeed));
    }

    partial void OnTotalSizeChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedTotalSize));
    }

    partial void OnDownloadedSizeChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedDownloadedSize));
    }
}
