using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentServiceSafetyTests
{
    [Fact]
    public async Task AddTorrentAsync_ConcurrentDuplicateImports_AddsOnlyOneTorrent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var bothCallsReachedSettingsLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settingsLoadCount = 0;
        var storage = new TestStorageService(temporaryDirectory.Path)
        {
            LoadSettingsAsyncHandler = async () =>
            {
                if (Interlocked.Increment(ref settingsLoadCount) == 2)
                {
                    bothCallsReachedSettingsLoad.SetResult(true);
                }

                await bothCallsReachedSettingsLoad.Task;
                return new AppSettings();
            }
        };
        await using var service = CreateService(storage);
        var magnet = $"magnet:?xt=urn:btih:{new string('a', 40)}&dn=duplicate";

        static async Task<Exception?> CaptureAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var attempts = await Task.WhenAll(
            CaptureAsync(async () => _ = await service.AddTorrentAsync(magnet)),
            CaptureAsync(async () => _ = await service.AddTorrentAsync(magnet)));

        Assert.Single(service.Torrents);
        Assert.Single(attempts.OfType<DuplicateTorrentException>());
        Assert.Single(attempts, static exception => exception is null);
    }

    [Fact]
    public async Task RemoveTorrentAsync_MetadataUnavailable_DoesNotDeleteDirectoryMatchingDisplayName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var storage = new TestStorageService(temporaryDirectory.Path);
        await using var service = CreateService(storage);
        var unrelatedDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "family-photos");
        Directory.CreateDirectory(unrelatedDirectory);
        var sentinelPath = System.IO.Path.Combine(unrelatedDirectory, "sentinel.jpg");
        File.WriteAllText(sentinelPath, "must survive");
        var torrent = CreateTorrent("magnet-only", "family-photos", temporaryDirectory.Path, DownloadStatus.Queued);
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.True(File.Exists(sentinelPath));
        Assert.True(Directory.Exists(unrelatedDirectory));
    }

    [Fact]
    public async Task RemoveTorrentAsync_ReplacedTorrentMetadata_DoesNotDeleteFilesListedByReplacement()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var replacementSource = System.IO.Path.Combine(temporaryDirectory.Path, "replacement-source.bin");
        File.WriteAllText(replacementSource, "replacement source");
        var replacementTorrentPath = System.IO.Path.Combine(temporaryDirectory.Path, "replacement.torrent");
        var creator = new TorrentCreator(TorrentType.V1Only);
        await creator.CreateAsync(
            new TorrentFileSource(replacementSource),
            replacementTorrentPath,
            TestContext.Current.CancellationToken);
        var replacementMetadata = await Torrent.LoadAsync(replacementTorrentPath);

        var downloadDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var sentinelPath = System.IO.Path.Combine(downloadDirectory, Assert.Single(replacementMetadata.Files).Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sentinelPath)!);
        File.WriteAllText(sentinelPath, "belongs to a different torrent");

        var storage = new TestStorageService(downloadDirectory);
        await using var service = CreateService(storage);
        var torrent = CreateTorrent("original-a", "original A", downloadDirectory, DownloadStatus.Queued);
        torrent.InfoHash = new string('a', 40);
        torrent.MagnetLink = $"magnet:?xt=urn:btih:{torrent.InfoHash}";
        torrent.TorrentFilePath = replacementTorrentPath;
        torrent.TorrentFileName = System.IO.Path.GetFileName(replacementTorrentPath);
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.True(File.Exists(sentinelPath));
        Assert.Equal("belongs to a different torrent", File.ReadAllText(sentinelPath));
    }

    [Fact]
    public async Task RemoveTorrentAsync_DeletesOnlyMetadataOwnedFiles_AndPrunesOnlyEmptyDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var sourceDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "source", "owned-collection");
        Directory.CreateDirectory(System.IO.Path.Combine(sourceDirectory, "nested"));
        File.WriteAllText(System.IO.Path.Combine(sourceDirectory, "one.bin"), "one");
        File.WriteAllText(System.IO.Path.Combine(sourceDirectory, "nested", "two.bin"), "two");

        var torrentFilePath = System.IO.Path.Combine(temporaryDirectory.Path, "metadata", "owned.torrent");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(torrentFilePath)!);
        var creator = new TorrentCreator(TorrentType.V1Only);
        await creator.CreateAsync(
            new TorrentFileSource(sourceDirectory),
            torrentFilePath,
            TestContext.Current.CancellationToken);
        var metadata = await Torrent.LoadAsync(torrentFilePath);
        Assert.Equal(2, metadata.Files.Count);

        var downloadDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "downloads");
        var containingDirectory = System.IO.Path.Combine(downloadDirectory, metadata.Name);
        Directory.CreateDirectory(containingDirectory);
        var ownedPaths = metadata.Files
            .Select(file => System.IO.Path.Combine(containingDirectory, file.Path))
            .ToArray();
        foreach (var ownedPath in ownedPaths)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ownedPath)!);
            File.WriteAllText(ownedPath, "payload");
            File.WriteAllText(ownedPath + ".!mt", "partial payload");
        }

        var unrelatedPath = System.IO.Path.Combine(containingDirectory, "keep.txt");
        File.WriteAllText(unrelatedPath, "not part of the torrent");
        var displayNameDirectory = System.IO.Path.Combine(downloadDirectory, "family-photos");
        Directory.CreateDirectory(displayNameDirectory);
        var sentinelPath = System.IO.Path.Combine(displayNameDirectory, "sentinel.jpg");
        File.WriteAllText(sentinelPath, "must survive");

        var storage = new TestStorageService(downloadDirectory);
        await using var service = CreateService(storage);
        var torrent = CreateTorrent("metadata-owned", "family-photos", downloadDirectory, DownloadStatus.Queued);
        torrent.InfoHash = metadata.InfoHashes.V1?.ToHex() ?? metadata.InfoHashes.V2!.ToHex();
        torrent.MagnetLink = $"magnet:?xt=urn:btih:{torrent.InfoHash}";
        torrent.TorrentFilePath = torrentFilePath;
        torrent.TorrentFileName = System.IO.Path.GetFileName(torrentFilePath);
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.All(ownedPaths, path =>
        {
            Assert.False(File.Exists(path));
            // Metadata-only fallback cannot prove the engine used partial files. The app's
            // engine default is false, so a same-named sibling must remain untouched.
            Assert.True(File.Exists(path + ".!mt"));
        });
        Assert.True(File.Exists(torrentFilePath));
        Assert.True(File.Exists(unrelatedPath));
        Assert.True(File.Exists(sentinelPath));
        Assert.True(Directory.Exists(System.IO.Path.Combine(containingDirectory, "nested")));
        Assert.True(Directory.Exists(containingDirectory));
    }

    [Fact]
    public async Task RemoveTorrentAsync_DeleteTorrentFile_DoesNotDeleteGuessedSavePathFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var guessedPath = System.IO.Path.Combine(temporaryDirectory.Path, "unrelated.torrent");
        File.WriteAllText(guessedPath, "not imported by this torrent");
        var storage = new TestStorageService(temporaryDirectory.Path);
        await using var service = CreateService(storage);
        var torrent = CreateTorrent("no-source", "anything", temporaryDirectory.Path, DownloadStatus.Queued);
        torrent.TorrentFileName = "unrelated.torrent";
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: true, deleteFiles: false);

        Assert.True(File.Exists(guessedPath));
    }

    [Fact]
    public async Task RemoveTorrentAsync_ReplacedMetadataDoesNotDeleteReplacementTorrentPayload()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var metadataDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "metadata");
        var originalSource = System.IO.Path.Combine(temporaryDirectory.Path, "source-original");
        var replacementSource = System.IO.Path.Combine(temporaryDirectory.Path, "source-replacement");
        Directory.CreateDirectory(metadataDirectory);
        Directory.CreateDirectory(originalSource);
        Directory.CreateDirectory(replacementSource);
        File.WriteAllText(System.IO.Path.Combine(originalSource, "original.bin"), "original");
        File.WriteAllText(System.IO.Path.Combine(replacementSource, "replacement.bin"), "replacement");

        var creator = new TorrentCreator(TorrentType.V1Only);
        var originalTorrentPath = System.IO.Path.Combine(metadataDirectory, "original.torrent");
        var replacementTorrentPath = System.IO.Path.Combine(metadataDirectory, "replacement.torrent");
        await creator.CreateAsync(
            new TorrentFileSource(originalSource),
            originalTorrentPath,
            TestContext.Current.CancellationToken);
        await creator.CreateAsync(
            new TorrentFileSource(replacementSource),
            replacementTorrentPath,
            TestContext.Current.CancellationToken);

        var originalMetadata = await Torrent.LoadAsync(originalTorrentPath);
        var replacementMetadata = await Torrent.LoadAsync(replacementTorrentPath);
        var originalHash = originalMetadata.InfoHashes.V1?.ToHex() ?? originalMetadata.InfoHashes.V2!.ToHex();
        var downloadDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var replacementPayload = System.IO.Path.Combine(
            downloadDirectory,
            replacementMetadata.Files.Single().Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(replacementPayload)!);
        File.WriteAllText(replacementPayload, "must survive");

        var storage = new TestStorageService(downloadDirectory);
        await using var service = CreateService(storage);
        var torrent = CreateTorrent("metadata-replaced", "original", downloadDirectory, DownloadStatus.Queued);
        torrent.InfoHash = originalHash;
        torrent.MagnetLink = $"magnet:?xt=urn:btih:{originalHash}";
        torrent.TorrentFilePath = replacementTorrentPath;
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.True(File.Exists(replacementPayload));
        Assert.True(File.Exists(replacementTorrentPath));
    }

    [Fact]
    public async Task RemoveTorrentAsync_OversizedLegacyMetadata_DoesNotParseOrDeletePayloads()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var downloadDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var sentinelPath = System.IO.Path.Combine(downloadDirectory, "sentinel.bin");
        File.WriteAllText(sentinelPath, "must survive");
        var torrentFilePath = System.IO.Path.Combine(temporaryDirectory.Path, "oversized.torrent");
        await using (var stream = new FileStream(torrentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(TorrentFileLimits.MaxFileSizeBytes + 1L);
        }

        var storage = new TestStorageService(downloadDirectory);
        await using var service = CreateService(storage);
        var torrent = CreateTorrent("oversized", "sentinel", downloadDirectory, DownloadStatus.Queued);
        torrent.TorrentFilePath = torrentFilePath;
        service.Torrents.Add(torrent);

        await service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.True(File.Exists(sentinelPath));
        Assert.True(File.Exists(torrentFilePath));
    }

    [Fact]
    public async Task InitializeAsync_NormalizesPersistedSeedingToPausedAndPersistsIt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var storage = new TestStorageService(temporaryDirectory.Path)
        {
            InitialTorrents =
            [
                CreateTorrent("seed-restore", "seed", temporaryDirectory.Path, DownloadStatus.Seeding)
            ]
        };
        storage.InitialTorrents[0].DateSeedingStarted = DateTime.UtcNow;
        await using var service = CreateService(storage);

        await service.InitializeAsync();

        var restored = Assert.Single(service.Torrents);
        Assert.Equal(DownloadStatus.Paused, restored.Status);
        Assert.Null(restored.DateSeedingStarted);
        var saved = Assert.Single(storage.SavedStatuses);
        Assert.Equal(DownloadStatus.Paused, Assert.Single(saved).Status);
    }

    [Fact]
    public async Task PauseAllForBackgroundTimeoutAsync_PausesActiveTransfersWithoutDrainingQueue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var storage = new TestStorageService(temporaryDirectory.Path);
        await using var service = CreateService(storage);
        var download = CreateTorrent("download", "download", temporaryDirectory.Path, DownloadStatus.Downloading);
        var seed = CreateTorrent("seed", "seed", temporaryDirectory.Path, DownloadStatus.Seeding);
        var queued = CreateTorrent("queued", "queued", temporaryDirectory.Path, DownloadStatus.Queued);
        service.Torrents.Add(download);
        service.Torrents.Add(seed);
        service.Torrents.Add(queued);

        await service.PauseAllForBackgroundTimeoutAsync();
        await service.PauseAllForBackgroundTimeoutAsync();

        Assert.Equal(DownloadStatus.Paused, download.Status);
        Assert.Equal(DownloadStatus.Paused, seed.Status);
        Assert.Equal(DownloadStatus.Queued, queued.Status);
        var saved = Assert.Single(storage.SavedStatuses);
        Assert.Equal(DownloadStatus.Queued, saved.Single(status => status.Id == queued.Id).Status);
    }

    [Fact]
    public async Task PauseAllForBackgroundTimeoutAsync_BlocksLaterStartsUntilForegroundResume()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var storage = new TestStorageService(temporaryDirectory.Path);
        await using var service = CreateService(storage);
        var queued = CreateTorrent("queued-after-timeout", "queued", temporaryDirectory.Path, DownloadStatus.Queued);
        service.Torrents.Add(queued);

        await service.PauseAllForBackgroundTimeoutAsync();
        await service.StartTorrentAsync(queued);

        Assert.Equal(DownloadStatus.Paused, queued.Status);

        service.ResumeAfterBackgroundTimeout();
        Assert.False(GetPrivateField<bool>(service, "_backgroundExecutionSuspended"));
    }

    [Fact]
    public async Task ProxyEnabledWithBlankHost_CreatesEngineWithDirectChannelsDisabled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var storage = new TestStorageService(temporaryDirectory.Path);
        await using var service = CreateService(storage);
        service.UpdateProxySettings(enabled: true, host: " ", port: 1080, username: string.Empty, password: string.Empty);
        var createEngine = typeof(TorrentService).GetMethod(
            "CreateEngine",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(createEngine);
        var engine = Assert.IsType<ClientEngine>(createEngine!.Invoke(service, null));

        try
        {
            Assert.False(engine.Settings.AllowPortForwarding);
            Assert.False(engine.Settings.AllowLocalPeerDiscovery);
            Assert.Null(engine.Settings.DhtEndPoint);
            Assert.Empty(engine.Settings.ListenEndPoints);
        }
        finally
        {
            if (engine is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (engine is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static TorrentItem CreateTorrent(string id, string name, string savePath, DownloadStatus status)
        => new()
        {
            Id = id,
            Name = name,
            MagnetLink = $"magnet:?xt=urn:btih:{new string(id[0], 40)}",
            InfoHash = new string(id[0], 40),
            SavePath = savePath,
            Status = status
        };

    private static TorrentService CreateService(TestStorageService storage)
        => new(storage, new StubNotificationService(), new StubBackgroundDownloadService());

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private sealed class TestStorageService(string defaultPath) : IStorageService
    {
        public List<TorrentItem> InitialTorrents { get; init; } = [];

        public Func<Task<AppSettings>>? LoadSettingsAsyncHandler { get; init; }

        public List<List<(string Id, DownloadStatus Status)>> SavedStatuses { get; } = [];

        public Task<List<TorrentItem>> LoadTorrentsAsync()
            => Task.FromResult(InitialTorrents.Select(Clone).ToList());

        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
        {
            SavedStatuses.Add(torrents.Select(torrent => (torrent.Id, torrent.Status)).ToList());
            return Task.CompletedTask;
        }

        public Task<AppSettings> LoadSettingsAsync()
            => LoadSettingsAsyncHandler?.Invoke() ?? Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;

        public string GetDefaultDownloadPath()
        {
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }

        private static TorrentItem Clone(TorrentItem torrent)
            => new()
            {
                Id = torrent.Id,
                Name = torrent.Name,
                MagnetLink = torrent.MagnetLink,
                InfoHash = torrent.InfoHash,
                SavePath = torrent.SavePath,
                Status = torrent.Status,
                DateSeedingStarted = torrent.DateSeedingStarted,
                TorrentFilePath = torrent.TorrentFilePath,
                TorrentFileName = torrent.TorrentFileName
            };
    }

    private sealed class StubNotificationService : INotificationService
    {
        public Task EnsurePermissionAsync() => Task.CompletedTask;

        public Task ShowDownloadCompletedAsync(TorrentItem torrent) => Task.CompletedTask;
    }

    private sealed class StubBackgroundDownloadService : IBackgroundDownloadService
    {
        public void Start()
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
