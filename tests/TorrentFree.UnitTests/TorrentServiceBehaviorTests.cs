using System.Reflection;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentServiceBehaviorTests
{
    [Fact]
    public async Task InitializeAsync_ClearsMissingTorrentFileMetadata_WhenFallbackMagnetIsValid()
    {
        var missingTorrentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.torrent");
        var storage = new RecordingStorageService
        {
            InitialTorrents =
            [
                new TorrentItem
                {
                    Id = "restore-1",
                    Name = "Ubuntu",
                    InfoHash = "0123456789abcdef0123456789abcdef01234567",
                    MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
                    SavePath = Path.Combine(Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N")),
                    TorrentFilePath = missingTorrentPath,
                    TorrentFileName = "ubuntu.torrent",
                    Status = DownloadStatus.Queued
                }
            ]
        };

        await using var service = CreateService(storage);

        await service.InitializeAsync();

        var restored = Assert.Single(service.Torrents);
        Assert.Null(restored.TorrentFilePath);
        Assert.Null(restored.TorrentFileName);

        var saveSnapshot = Assert.Single(storage.SavedSnapshots);
        var persisted = Assert.Single(saveSnapshot);
        Assert.Null(persisted.TorrentFilePath);
        Assert.Null(persisted.TorrentFileName);
    }

    [Fact]
    public async Task InitializeAsync_SkipsTorrent_WhenMissingTorrentFileHasNoValidFallback()
    {
        var missingTorrentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.torrent");
        var storage = new RecordingStorageService
        {
            InitialTorrents =
            [
                new TorrentItem
                {
                    Id = "restore-2",
                    Name = "Broken",
                    MagnetLink = "not-a-magnet",
                    SavePath = Path.Combine(Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N")),
                    TorrentFilePath = missingTorrentPath,
                    TorrentFileName = "broken.torrent",
                    Status = DownloadStatus.Queued
                }
            ]
        };

        await using var service = CreateService(storage);

        await service.InitializeAsync();

        Assert.Empty(service.Torrents);
        var saveSnapshot = Assert.Single(storage.SavedSnapshots);
        Assert.Empty(saveSnapshot);
    }

    [Fact]
    public async Task AddTorrentAsync_ThrowsDuplicateTorrentException_WhenInfoHashAlreadyExists()
    {
        var storage = new RecordingStorageService();
        await using var service = CreateService(storage);

        service.Torrents.Add(new TorrentItem
        {
            Id = "existing-1",
            Name = "Existing",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            MagnetLink = "magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Queued
        });

        await Assert.ThrowsAsync<DuplicateTorrentException>(() => service.AddTorrentAsync(
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Duplicate"));
    }

    [Fact]
    public async Task EnforceSeedingLimitsAsync_PausesTorrent_WhenGlobalRatioReached()
    {
        var storage = new RecordingStorageService();
        await using var service = CreateService(storage);
        var torrent = new TorrentItem
        {
            Id = "seed-1",
            Name = "Ubuntu",
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Seeding,
            TotalSize = 100,
            UploadedSize = 100
        };

        service.Torrents.Add(torrent);
        service.UpdateSeedingLimits(1.0, 0);

        await InvokePrivateTaskAsync(service, "EnforceSeedingLimitsAsync", torrent, null);

        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        var saveSnapshot = Assert.Single(storage.SavedSnapshots);
        var persisted = Assert.Single(saveSnapshot);
        Assert.Equal(DownloadStatus.Paused, persisted.Status);
    }

    [Fact]
    public void TorrentItem_CanStop_ReturnsTrue_WhenStatusIsSeeding()
    {
        var torrent = new TorrentItem
        {
            Status = DownloadStatus.Seeding
        };

        Assert.True(torrent.CanStop);
    }

    [Fact]
    public void TorrentItem_CanStart_ReturnsTrue_WhenStatusIsCompleted()
    {
        var torrent = new TorrentItem
        {
            Status = DownloadStatus.Completed
        };

        Assert.True(torrent.CanStart);
    }

    [Fact]
    public async Task UpdateQueueLimits_NormalizesNegativeValuesToZero()
    {
        var storage = new RecordingStorageService();
        await using var service = CreateService(storage);

        service.UpdateQueueLimits(-1, -5);

        Assert.Equal(0, GetPrivateField<int>(service, "_maxActiveDownloads"));
        Assert.Equal(0, GetPrivateField<int>(service, "_maxActiveSeeds"));
    }

    [Fact]
    public async Task UpdateProxySettings_NormalizesInvalidValues()
    {
        var storage = new RecordingStorageService();
        await using var service = CreateService(storage);

        service.UpdateProxySettings(true, null!, -1, null!, null!);

        Assert.True(GetPrivateField<bool>(service, "_proxyEnabled"));
        Assert.Equal(string.Empty, GetPrivateField<string>(service, "_proxyHost"));
        Assert.Equal(1080, GetPrivateField<int>(service, "_proxyPort"));
        Assert.Equal(string.Empty, GetPrivateField<string>(service, "_proxyUsername"));
        Assert.Equal(string.Empty, GetPrivateField<string>(service, "_proxyPassword"));
    }

    private static TorrentService CreateService(RecordingStorageService storage)
        => new(storage, new StubNotificationService(), new RecordingBackgroundDownloadService());

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static async Task InvokePrivateTaskAsync(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(instance, arguments);
        var task = Assert.IsAssignableFrom<Task>(result);
        await task;
    }

    private sealed class RecordingStorageService : IStorageService
    {
        public List<TorrentItem> InitialTorrents { get; init; } = [];

        public List<List<TorrentSnapshot>> SavedSnapshots { get; } = [];

        public Task<List<TorrentItem>> LoadTorrentsAsync()
            => Task.FromResult(InitialTorrents.Select(CloneTorrent).ToList());

        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
        {
            SavedSnapshots.Add(torrents.Select(static torrent => new TorrentSnapshot(
                torrent.Id,
                torrent.Status,
                torrent.TorrentFilePath,
                torrent.TorrentFileName)).ToList());
            return Task.CompletedTask;
        }

        public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;

        public string GetDefaultDownloadPath()
        {
            var path = Path.Combine(Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static TorrentItem CloneTorrent(TorrentItem torrent)
        {
            return new TorrentItem
            {
                Id = torrent.Id,
                Name = torrent.Name,
                MagnetLink = torrent.MagnetLink,
                InfoHash = torrent.InfoHash,
                TotalSize = torrent.TotalSize,
                DownloadedSize = torrent.DownloadedSize,
                UploadedSize = torrent.UploadedSize,
                Progress = torrent.Progress,
                Status = torrent.Status,
                DownloadSpeed = torrent.DownloadSpeed,
                UploadSpeed = torrent.UploadSpeed,
                DownloadLimitKbps = torrent.DownloadLimitKbps,
                UploadLimitKbps = torrent.UploadLimitKbps,
                Seeders = torrent.Seeders,
                Leechers = torrent.Leechers,
                EstimatedSecondsRemaining = torrent.EstimatedSecondsRemaining,
                DateAdded = torrent.DateAdded,
                DateCompleted = torrent.DateCompleted,
                DateSeedingStarted = torrent.DateSeedingStarted,
                MaxSeedRatio = torrent.MaxSeedRatio,
                MaxSeedMinutes = torrent.MaxSeedMinutes,
                SavePath = torrent.SavePath,
                TorrentFilePath = torrent.TorrentFilePath,
                TorrentFileName = torrent.TorrentFileName,
                ErrorMessage = torrent.ErrorMessage,
                DisplayIndex = torrent.DisplayIndex,
                HealthScore = torrent.HealthScore,
                AvailabilityPercent = torrent.AvailabilityPercent,
                AvailabilityLabel = torrent.AvailabilityLabel
            };
        }
    }

    private sealed class StubNotificationService : INotificationService
    {
        public Task EnsurePermissionAsync() => Task.CompletedTask;

        public Task ShowDownloadCompletedAsync(TorrentItem torrent) => Task.CompletedTask;
    }

    private sealed class RecordingBackgroundDownloadService : IBackgroundDownloadService
    {
        public void Start()
        {
        }

        public void Stop()
        {
        }
    }

    private sealed record TorrentSnapshot(string Id, DownloadStatus Status, string? TorrentFilePath, string? TorrentFileName);
}
