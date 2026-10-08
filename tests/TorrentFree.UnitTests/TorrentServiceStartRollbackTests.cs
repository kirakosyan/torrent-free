using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentServiceStartRollbackTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShutdownDuringStart_PreservesResumableState(bool failDuringManagerCreation)
    {
        var storage = new RecordingStorageService();
        await using var service = new InterruptedStartTorrentService(storage, failDuringManagerCreation);
        var torrent = new TorrentItem
        {
            Name = "Interrupted start",
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Queued
        };
        service.Torrents.Add(torrent);

        var startTask = service.StartTorrentAsync(torrent);
        await service.StartBlocked.Task.WaitAsync(TestContext.Current.CancellationToken);
        await service.DisposeAsync();
        service.ReleaseStart.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Null(torrent.ErrorMessage);
        Assert.All(storage.SavedSnapshots, snapshot =>
        {
            var saved = Assert.Single(snapshot);
            Assert.Equal(DownloadStatus.Downloading, saved.Status);
            Assert.Null(saved.ErrorMessage);
        });
        Assert.NotEmpty(storage.SavedSnapshots);
    }

    [Fact]
    public async Task ShutdownDuringStartRollback_PreservesResumableState()
    {
        var storage = new RecordingStorageService();
        await using var service = new InterruptedRollbackTorrentService(storage);
        var torrent = new TorrentItem
        {
            Name = "Interrupted rollback",
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Queued
        };
        service.Torrents.Add(torrent);

        var startTask = service.StartTorrentAsync(torrent);
        await service.RollbackBlocked.Task.WaitAsync(TestContext.Current.CancellationToken);
        await service.DisposeAsync();
        service.ReleaseRollback.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Null(torrent.ErrorMessage);
        var lastSnapshot = Assert.Single(storage.SavedSnapshots[^1]);
        Assert.Equal(DownloadStatus.Downloading, lastSnapshot.Status);
        Assert.Null(lastSnapshot.ErrorMessage);
    }

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

        protected override Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent, CancellationToken cancellationToken = default, bool rebuildRestart = false)
            => Task.FromException<TorrentManager>(_exception);
    }

    private sealed class InterruptedStartTorrentService(IStorageService storage, bool failDuringManagerCreation)
        : TorrentService(storage, new StubNotificationService(), new RecordingBackgroundDownloadService(), ImmediateDispatcher.Instance)
    {
        public TaskCompletionSource StartBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent, CancellationToken cancellationToken = default, bool rebuildRestart = false)
        {
            if (failDuringManagerCreation)
                await WaitForShutdownAsync();
            return await base.GetOrCreateManagerAsync(torrent, cancellationToken, rebuildRestart);
        }

        protected override Task StartManagerAsync(TorrentManager manager) => WaitForShutdownAsync();

        private async Task WaitForShutdownAsync()
        {
            StartBlocked.TrySetResult();
            await ReleaseStart.Task;
            throw new ObjectDisposedException("Simulated engine disposal during start");
        }
    }

    private sealed class InterruptedRollbackTorrentService(IStorageService storage)
        : TorrentService(storage, new StubNotificationService(), new RecordingBackgroundDownloadService(), ImmediateDispatcher.Instance)
    {
        private int _stopCalls;
        public TaskCompletionSource RollbackBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRollback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task StartManagerAsync(TorrentManager manager)
            => Task.FromException(new InvalidOperationException("Simulated start failure before shutdown"));

        protected override async Task StopManagerAsync(TorrentManager manager)
        {
            if (Interlocked.Increment(ref _stopCalls) == 1)
            {
                RollbackBlocked.TrySetResult();
                await ReleaseRollback.Task;
            }
        }
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

        public string GetAppDataPath() => GetDefaultDownloadPath();

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

        public bool Start()
        {
            StartCalls++;
            return true;
        }

        public void Stop() => StopCalls++;
    }

    private sealed record TorrentSnapshot(DownloadStatus Status, string? ErrorMessage);
}
