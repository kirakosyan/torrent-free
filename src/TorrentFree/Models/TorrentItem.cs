using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFree.Models;

/// <summary>
/// Represents a torrent download item.
/// </summary>
public partial class TorrentItem : ObservableObject
{
    private const int MaxSpeedSamples = 60;
    /// <summary>
    /// Unique identifier for the torrent item.
    /// </summary>
    [ObservableProperty]
    public partial string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Name of the torrent/file being downloaded.
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// The magnet link or torrent URL.
    /// </summary>
    [ObservableProperty]
    public partial string MagnetLink { get; set; } = string.Empty;

    /// <summary>
    /// InfoHash (hex) if known, used for duplicate detection.
    /// </summary>
    [ObservableProperty]
    public partial string InfoHash { get; set; } = string.Empty;

    /// <summary>
    /// Total size of the download in bytes.
    /// </summary>
    [ObservableProperty]
    public partial long TotalSize { get; set; }

    /// <summary>
    /// Downloaded size in bytes.
    /// </summary>
    [ObservableProperty]
    public partial long DownloadedSize { get; set; }

    /// <summary>
    /// Current download progress (0-100).
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>
    /// Current download status.
    /// </summary>
    [ObservableProperty]
    public partial DownloadStatus Status { get; set; } = DownloadStatus.Queued;

    /// <summary>
    /// Download speed in bytes per second.
    /// </summary>
    [ObservableProperty]
    public partial long DownloadSpeed { get; set; }

    /// <summary>
    /// Upload speed in bytes per second.
    /// </summary>
    [ObservableProperty]
    public partial long UploadSpeed { get; set; }

    /// <summary>
    /// Per-torrent download limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int DownloadLimitKbps { get; set; }

    /// <summary>
    /// Per-torrent upload limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int UploadLimitKbps { get; set; }

    /// <summary>
    /// Number of seeders connected.
    /// </summary>
    [ObservableProperty]
    public partial int Seeders { get; set; }

    /// <summary>
    /// Number of leechers connected.
    /// </summary>
    [ObservableProperty]
    public partial int Leechers { get; set; }

    /// <summary>
    /// Estimated seconds remaining to finish download.
    /// </summary>
    [ObservableProperty]
    public partial long EstimatedSecondsRemaining { get; set; }

    /// <summary>
    /// Formatted ETA string for UI.
    /// </summary>
    public string FormattedEstimatedTime => EstimatedSecondsRemaining <= 0
        ? "—"
        : TimeSpan.FromSeconds(EstimatedSecondsRemaining).ToString(EstimatedSecondsRemaining >= 3600 ? "hh\\:mm\\:ss" : "mm\\:ss");

    /// <summary>
    /// Date and time when the torrent was added.
    /// </summary>
    [ObservableProperty]
    public partial DateTime DateAdded { get; set; } = DateTime.Now;

    /// <summary>
    /// Date and time when the download completed.
    /// </summary>
    [ObservableProperty]
    public partial DateTime? DateCompleted { get; set; }

    /// <summary>
    /// Date and time when seeding started.
    /// </summary>
    [ObservableProperty]
    public partial DateTime? DateSeedingStarted { get; set; }

    /// <summary>
    /// Per-torrent max seed ratio (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial double MaxSeedRatio { get; set; }

    /// <summary>
    /// Per-torrent max seed time in minutes (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int MaxSeedMinutes { get; set; }

    /// <summary>
    /// Local file path where the download is saved.
    /// </summary>
    [ObservableProperty]
    public partial string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// Local .torrent file path when imported from disk.
    /// </summary>
    [ObservableProperty]
    public partial string? TorrentFilePath { get; set; }

    /// <summary>
    /// Original .torrent file name when imported from disk.
    /// </summary>
    [ObservableProperty]
    public partial string? TorrentFileName { get; set; }

    /// <summary>
    /// Health score of the torrent (0-100).
    /// </summary>
    [ObservableProperty]
    public partial int HealthScore { get; set; }

    /// <summary>
    /// Availability percentage (0-100).
    /// </summary>
    [ObservableProperty]
    public partial double AvailabilityPercent { get; set; }

    /// <summary>
    /// Availability label (e.g., 1.2x or 75%).
    /// </summary>
    [ObservableProperty]
    public partial string AvailabilityLabel { get; set; } = "—";

    /// <summary>
    /// Download speed history in KB/s.
    /// </summary>
    public ObservableCollection<double> DownloadSpeedHistory { get; } = [];

    /// <summary>
    /// Upload speed history in KB/s.
    /// </summary>
    public ObservableCollection<double> UploadSpeedHistory { get; } = [];

    /// <summary>
    /// Gets the full path to the downloaded file or folder.
    /// </summary>
    public string DownloadedFilePath
    {
        get
        {
            var basePath = SavePath ?? string.Empty;
            var safeName = string.IsNullOrWhiteSpace(Name) ? "unnamed_torrent" : Name;
            safeName = string.Concat(safeName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "unnamed_torrent";
            }

            return Path.Combine(basePath, safeName);
        }
    }

    /// <summary>
    /// Indicates whether the downloaded file or folder can be opened from the UI.
    /// </summary>
    public bool CanOpenDownloadedFile
    {
        get
        {
            var isComplete = Status is DownloadStatus.Completed or DownloadStatus.Seeding
                             || (Status is DownloadStatus.Paused or DownloadStatus.Stopped && (Progress >= 100 || DateCompleted is not null));

            if (!isComplete)
            {
                return false;
            }

            var path = DownloadedFilePath;
            return File.Exists(path) || Directory.Exists(path);
        }
    }

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
        DownloadStatus.Seeding => "Seeding",
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
    public bool CanPause => Status is DownloadStatus.Downloading or DownloadStatus.Seeding;

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
        OnPropertyChanged(nameof(CanOpenDownloadedFile));
    }

    partial void OnSavePathChanged(string value)
    {
        OnPropertyChanged(nameof(DownloadedFilePath));
        OnPropertyChanged(nameof(CanOpenDownloadedFile));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DownloadedFilePath));
        OnPropertyChanged(nameof(CanOpenDownloadedFile));
    }

    partial void OnDownloadSpeedChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedDownloadSpeed));
        OnPropertyChanged(nameof(FormattedEstimatedTime));
    }

    partial void OnUploadSpeedChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedUploadSpeed));
    }

    partial void OnTotalSizeChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedTotalSize));
        OnPropertyChanged(nameof(FormattedEstimatedTime));
    }

    partial void OnDownloadedSizeChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedDownloadedSize));
        OnPropertyChanged(nameof(FormattedEstimatedTime));
    }

    partial void OnEstimatedSecondsRemainingChanged(long value)
    {
        OnPropertyChanged(nameof(FormattedEstimatedTime));
    }

    public void AddSpeedSample(long downloadBytesPerSecond, long uploadBytesPerSecond)
    {
        AppendSample(DownloadSpeedHistory, BytesToKbps(downloadBytesPerSecond));
        AppendSample(UploadSpeedHistory, BytesToKbps(uploadBytesPerSecond));
    }

    private static double BytesToKbps(long bytesPerSecond) => bytesPerSecond / 1024d;

    private static void AppendSample(ObservableCollection<double> samples, double value)
    {
        samples.Add(Math.Max(0, value));
        while (samples.Count > MaxSpeedSamples)
        {
            samples.RemoveAt(0);
        }
    }
}
