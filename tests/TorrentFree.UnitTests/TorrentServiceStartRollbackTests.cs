using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentServiceStartRollbackTests
{
    [Fact]
    public async Task StartTorrentAsync_MarksTorrentFailedAndStopsBackgroundService_WhenManagerCreationThrows()
    {
        var storage = new RecordingStorageService();
        var notifications = new StubNotificationService();
        var background = new RecordingBackgroundDownloadService();
        await using var service = new ThrowingTorrentService(
            storage,
            notifications,
            background,
            new InvalidOperationException("simulated manager failure"));

        var torrent = new TorrentItem
        {
            Id = "torrent-1",
            Name = "Ubuntu",
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Queued
        };
        service.Torrents.Add(torrent);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartTorrentAsync(torrent));

        Assert.Equal("simulated manager failure", ex.Message);
        Assert.Equal(DownloadStatus.Failed, torrent.Status);
        Assert.Equal("simulated manager failure", torrent.ErrorMessage);
        Assert.Equal(0, torrent.DownloadSpeed);
        Assert.Equal(0, torrent.UploadSpeed);
        Assert.Equal(1, background.StartCalls);
        Assert.Equal(1, background.StopCalls);

        var lastSnapshot = Assert.Single(storage.SavedSnapshots[^1]);
        Assert.Equal(DownloadStatus.Failed, lastSnapshot.Status);
        Assert.Equal("simulated manager failure", lastSnapshot.ErrorMessage);
    }

    private sealed class ThrowingTorrentService : TorrentService
    {
        private readonly Exception _exception;

        public ThrowingTorrentService(
            IStorageService storageService,
            INotificationService notificationService,
            IBackgroundDownloadService backgroundDownloadService,
            Exception exception)
            : base(storageService, notificationService, backgroundDownloadService, ImmediateDispatcher.Instance)
        {
            _exception = exception;
        }

        protected override Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent)
            => Task.FromException<TorrentManager>(_exception);
    }

    private sealed class RecordingStorageService : IStorageService
    {
        public List<List<TorrentSnapshot>> SavedSnapshots { get; } = [];

        public Task<List<TorrentItem>> LoadTorrentsAsync() => Task.FromResult(new List<TorrentItem>());

        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
        {
            SavedSnapshots.Add(
                torrents.Select(static torrent => new TorrentSnapshot(torrent.Status, torrent.ErrorMessage)).ToList());
            return Task.CompletedTask;
        }

        public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;

        public Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized) => Task.CompletedTask;

        public string GetDefaultDownloadPath()
        {
            var path = Path.Combine(Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private sealed class StubNotificationService : INotificationService
    {
        public Task EnsurePermissionAsync() => Task.CompletedTask;

        public Task ShowDownloadCompletedAsync(TorrentItem torrent) => Task.CompletedTask;
    }

    private sealed class RecordingBackgroundDownloadService : IBackgroundDownloadService
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start() => StartCalls++;

        public void Stop() => StopCalls++;
    }

    private sealed record TorrentSnapshot(DownloadStatus Status, string? ErrorMessage);
}
