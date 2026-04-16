using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TorrentFree.Services;

namespace TorrentFree.Models;

/// <summary>
/// Represents a torrent download item.
/// </summary>
public partial class TorrentItem : ObservableObject
{
    private const int MaxSpeedSamples = 60;
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());
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
    /// Uploaded size in bytes.
    /// </summary>
    [ObservableProperty]
    public partial long UploadedSize { get; set; }

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
    [JsonIgnore]
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
    /// Name of the .torrent file (if applicable).
    /// </summary>
    [ObservableProperty]
    public partial string? TorrentFileName { get; set; }

    /// <summary>
    /// Error message explaining why the download failed or is stalled.
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Current display index in the list (used for styling).
    /// </summary>
    [ObservableProperty]
    public partial int DisplayIndex { get; set; }

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
    [JsonIgnore]
    public ObservableCollection<double> DownloadSpeedHistory { get; } = [];

    /// <summary>
    /// Upload speed history in KB/s.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<double> UploadSpeedHistory { get; } = [];

    [JsonIgnore]
    public ICommand? ShowInFolderCommand { get; set; }

    [JsonIgnore]
    public ICommand? StartSpecificTorrentCommand { get; set; }

    [JsonIgnore]
    public ICommand? PauseSpecificTorrentCommand { get; set; }

    [JsonIgnore]
    public ICommand? StopSpecificTorrentCommand { get; set; }

    [JsonIgnore]
    public ICommand? RemoveSpecificTorrentCommand { get; set; }

    /// <summary>
    /// Gets the full path to the downloaded file or folder.
    /// </summary>
    [JsonIgnore]
    public string DownloadedFilePath
    {
        get
        {
            var basePath = SavePath ?? string.Empty;
            var safeName = string.IsNullOrWhiteSpace(Name) ? "unnamed_torrent" : Name;
            safeName = string.Concat(safeName.Where(c => !InvalidFileNameChars.Contains(c))).Trim();
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
    [JsonIgnore]
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
    [JsonIgnore]
    public string FormattedDownloadSpeed => FormatBytes(DownloadSpeed) + "/s";

    /// <summary>
    /// Gets the formatted upload speed string.
    /// </summary>
    [JsonIgnore]
    public string FormattedUploadSpeed => FormatBytes(UploadSpeed) + "/s";

    /// <summary>
    /// Gets the formatted total size string.
    /// </summary>
    [JsonIgnore]
    public string FormattedTotalSize => FormatBytes(TotalSize);

    /// <summary>
    /// Gets the formatted downloaded size string.
    /// </summary>
    [JsonIgnore]
    public string FormattedDownloadedSize => FormatBytes(DownloadedSize);

    /// <summary>
    /// Gets the status display text.
    /// </summary>
    [JsonIgnore]
    public string StatusText => Status switch
    {
        DownloadStatus.Queued => LocalizationResourceManager.Instance["StatusQueued"],
        DownloadStatus.Downloading => $"{LocalizationResourceManager.Instance["StatusDownloading"]} - {Progress:F1}%",
        DownloadStatus.Paused => LocalizationResourceManager.Instance["StatusPaused"],
        DownloadStatus.Completed => LocalizationResourceManager.Instance["StatusCompleted"],
        DownloadStatus.Seeding => LocalizationResourceManager.Instance["StatusSeeding"],
        DownloadStatus.Failed => LocalizationResourceManager.Instance["StatusFailed"],
        DownloadStatus.Stopped => LocalizationResourceManager.Instance["StatusStopped"],
        _ => "Unknown" // Not localized by design: defensive fallback for unexpected DownloadStatus values.
    };

    /// <summary>
    /// Short hint shown when a torrent is not actively downloading.
    /// </summary>
    [JsonIgnore]
    public string StatusHint
    {
        get
        {
            if (Status == DownloadStatus.Failed)
            {
                return string.IsNullOrWhiteSpace(ErrorMessage)
                    ? LocalizationResourceManager.Instance["HintDownloadFailed"]
                    : ErrorMessage;
            }

            return Status switch
            {
                DownloadStatus.Queued => LocalizationResourceManager.Instance["HintQueued"],
                DownloadStatus.Paused => LocalizationResourceManager.Instance["HintPaused"],
                DownloadStatus.Stopped => LocalizationResourceManager.Instance["HintStopped"],
                DownloadStatus.Downloading => BuildDownloadingHint(),
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// Indicates whether the download can be started or resumed.
    /// </summary>
    [JsonIgnore]
    public bool CanStart => Status is DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Stopped or DownloadStatus.Failed or DownloadStatus.Completed;

    /// <summary>
    /// Indicates whether the download can be paused.
    /// </summary>
    [JsonIgnore]
    public bool CanPause => Status is DownloadStatus.Downloading or DownloadStatus.Seeding;

    /// <summary>
    /// Indicates whether the download can be stopped.
    /// </summary>
    [JsonIgnore]
    public bool CanStop => Status is DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Queued or DownloadStatus.Seeding;

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

    /// <summary>
    /// Raises property changed notifications for all localizable display properties.
    /// Call this when the application language changes to refresh displayed strings.
    /// </summary>
    public void RefreshLocalizableProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusHint));
    }

    partial void OnStatusChanged(DownloadStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusHint));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanOpenDownloadedFile));
    }

    partial void OnSeedersChanged(int value)
    {
        OnPropertyChanged(nameof(StatusHint));
    }

    partial void OnLeechersChanged(int value)
    {
        OnPropertyChanged(nameof(StatusHint));
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
        OnPropertyChanged(nameof(StatusHint));
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

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(StatusHint));
    }

    private string BuildDownloadingHint()
    {
        if (DownloadSpeed > 0)
        {
            return string.Empty;
        }

        if (Seeders == 0 && Leechers == 0)
        {
            return LocalizationResourceManager.Instance["HintSearchingPeers"];
        }

        if (Seeders == 0)
        {
            return LocalizationResourceManager.Instance["HintWaitingSeeds"];
        }

        return LocalizationResourceManager.Instance["HintConnectingPeers"];
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
