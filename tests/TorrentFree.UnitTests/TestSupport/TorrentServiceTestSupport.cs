global using Microsoft.Maui.ApplicationModel;

using System.ComponentModel;
using TorrentFree.Models;

namespace Microsoft.Maui.ApplicationModel
{
    public static class MainThread
    {
        public static Task InvokeOnMainThreadAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
            return Task.CompletedTask;
        }
    }
}

namespace TorrentFree.Services
{
    public interface IStorageService
    {
        Task<List<TorrentItem>> LoadTorrentsAsync();
        Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents);
        Task<AppSettings> LoadSettingsAsync();
        Task SaveSettingsAsync(AppSettings settings);
        string GetDefaultDownloadPath();
    }

    public interface INotificationService
    {
        Task EnsurePermissionAsync();
        Task ShowDownloadCompletedAsync(TorrentItem torrent);
    }

    public interface IBackgroundDownloadService
    {
        void Start();
        void Stop();
    }

    public sealed record TorrentMetadata(string? Name, string? InfoHashHex, IReadOnlyList<string> Trackers);

    public sealed class DuplicateTorrentException(string message) : InvalidOperationException(message);
}

namespace TorrentFree.Models
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Completed,
        Seeding,
        Failed,
        Stopped
    }

    public sealed class TorrentItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string MagnetLink { get; set; } = string.Empty;
        public string InfoHash { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        public long UploadedSize { get; set; }
        public double Progress { get; set; }
        public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
        public long DownloadSpeed { get; set; }
        public long UploadSpeed { get; set; }
        public int DownloadLimitKbps { get; set; }
        public int UploadLimitKbps { get; set; }
        public int Seeders { get; set; }
        public int Leechers { get; set; }
        public long EstimatedSecondsRemaining { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime? DateCompleted { get; set; }
        public DateTime? DateSeedingStarted { get; set; }
        public double MaxSeedRatio { get; set; }
        public int MaxSeedMinutes { get; set; }
        public string SavePath { get; set; } = string.Empty;
        public string? TorrentFilePath { get; set; }
        public string? TorrentFileName { get; set; }
        public string? ErrorMessage { get; set; }
        public int DisplayIndex { get; set; }
        public int HealthScore { get; set; }
        public double AvailabilityPercent { get; set; }
        public string AvailabilityLabel { get; set; } = string.Empty;

        public bool CanStart => Status is DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Stopped or DownloadStatus.Failed;
        public bool CanPause => Status is DownloadStatus.Downloading or DownloadStatus.Seeding;
        public bool CanStop => Status is DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Queued;

        public void AddSpeedSample(long downloadBytesPerSecond, long uploadBytesPerSecond)
        {
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
