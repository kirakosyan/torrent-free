using System.Reflection;
using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class ReviewFixesTests
{
    private const string UnreachableTracker = "http%3A%2F%2F127.0.0.1%3A1%2Fannounce";

    [Fact]
    public async Task PauseWhileFetchingMetadata_StopsManagerSoItCannotStartDownloadingLater()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var hash = Guid.NewGuid().ToString("N") + "01234567";
        var torrent = (await fixture.Service.AddTorrentAsync($"magnet:?xt=urn:btih:{hash}&dn=pending&tr={UnreachableTracker}"))!;
        await fixture.Service.StartTorrentAsync(torrent);
        var manager = Assert.Single(fixture.Engine.Torrents);
        await CoreServiceFixture.WaitUntilAsync(() => manager.State == TorrentState.Metadata);

        await fixture.Service.PauseTorrentAsync(torrent);

        // MonoTorrent ignores PauseAsync in metadata mode and would start the download itself
        // once metadata arrived, while the item showed Paused.
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        Assert.Equal(TorrentState.Stopped, manager.State);
    }

    [Fact]
    public void PauseInPlace_IsOnlyUsedForStatesMonoTorrentCanPause()
    {
        Assert.True(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Downloading));
        Assert.True(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Seeding));
        Assert.False(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Metadata));
        Assert.False(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Starting));
        Assert.False(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Hashing));
        Assert.False(TorrentManagerStateRules.CanPauseInPlace(TorrentState.Error));
    }

    [Fact]
    public async Task WifiLossRebuild_KeepsFastResumeDataForTheNextStart()
    {
        var network = new TestNetwork();
        await using var fixture = new CoreServiceFixture(networkMonitor: network);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
        var manager = Assert.Single(fixture.Engine.Torrents);
        var fastResumePath = fixture.Engine.Settings.GetFastResumePath(manager.InfoHashes);

        network.IsWifiConnected = false;
        network.Notify();
        await fixture.Service.UpdateWifiOnlyAsync(true);

        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.True(File.Exists(fastResumePath));
    }

    [Fact]
    public async Task RemoveAfterRestart_UsesEngineMetadataCacheToDeleteMagnetDownload()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var (torrent, payloadPath, metadataCachePath) = await CreateRestoredMagnetDownloadAsync(fixture);

        var result = await fixture.Service.RemoveTorrentAsync(torrent, deleteTorrentFile: false, deleteFiles: true);

        Assert.True(result.Removed);
        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.False(File.Exists(payloadPath));
        // No manager existed, so the engine never removed its cache entry for this torrent.
        Assert.False(File.Exists(metadataCachePath));
    }

    [Fact]
    public async Task RemoveWithUnknownOwnership_ReportsFilesLeftInPlaceOnlyWhenDataMayExist()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var downloaded = CreateMagnetItem(fixture, progress: 40);
        var neverStarted = CreateMagnetItem(fixture, progress: 0);
        fixture.Service.Torrents.Add(downloaded);
        fixture.Service.Torrents.Add(neverStarted);

        var downloadedResult = await fixture.Service.RemoveTorrentAsync(downloaded, deleteFiles: true);
        var neverStartedResult = await fixture.Service.RemoveTorrentAsync(neverStarted, deleteFiles: true);
        var untracked = await fixture.Service.RemoveTorrentAsync(downloaded, deleteFiles: true);

        Assert.True(downloadedResult.DownloadedFilesLeftInPlace);
        Assert.False(neverStartedResult.DownloadedFilesLeftInPlace);
        Assert.False(untracked.Removed);
    }

    [Fact]
    public async Task RestoredActiveTransfer_IsQueuedAndResumesWhenHostStartsQueue()
    {
        await using var fixture = new CoreServiceFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        var saved = new TorrentItem
        {
            Name = "payload.bin",
            InfoHash = metadata.InfoHashHex!,
            MagnetLink = $"magnet:?xt=urn:btih:{metadata.InfoHashHex}",
            CachedTorrentFilePath = metadata.CachedFilePath,
            SavePath = fixture.Storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Seeding,
            Progress = 100
        };
        await fixture.Storage.LoadTorrentsAsync();
        await fixture.Storage.SaveTorrentsAsync([saved]);

        await fixture.Service.InitializeAsync();
        var restored = Assert.Single(fixture.Service.Torrents);
        Assert.Equal(DownloadStatus.Queued, restored.Status);
        Assert.Empty(fixture.Engine.Torrents);

        await fixture.Service.StartQueuedTorrentsAsync();
        await CoreServiceFixture.WaitUntilAsync(() => restored.Status == DownloadStatus.Seeding);
    }

    [Fact]
    public async Task RefusedBackgroundStart_IsRetriedOnForegroundResumeOnly()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        var background = new RefusingBackground { Accept = false };
        await using var service = new TorrentService(storage, new CoreServiceFixture.TestNotifications(), background, ImmediateDispatcher.Instance);
        await service.InitializeAsync();
        service.Torrents.Add(new TorrentItem { Status = DownloadStatus.Downloading });

        InvokeUpdateBackgroundState(service);
        InvokeUpdateBackgroundState(service);
        Assert.Equal(1, background.StartCalls);

        background.Accept = true;
        service.ResumeAfterBackgroundTimeout();
        Assert.Equal(2, background.StartCalls);
        InvokeUpdateBackgroundState(service);
        Assert.Equal(2, background.StartCalls);

        service.Torrents.Single().Status = DownloadStatus.Paused;
        InvokeUpdateBackgroundState(service);
        Assert.Equal(1, background.StopCalls);
    }

    [Fact]
    public void Migration_WithLinkedDownloadFolderAndNoLegacyCache_CompletesWithoutFollowingLink()
    {
        using var directory = new CoreTestDirectory();
        var target = Path.Combine(directory.Path, "real-downloads");
        Directory.CreateDirectory(target);
        var linked = Path.Combine(directory.Path, "linked-downloads");
        Directory.CreateSymbolicLink(linked, target);
        var cache = Path.Combine(directory.Path, "EngineCache");

        EngineCacheMigration.Migrate(linked, cache);

        Assert.True(File.Exists(Path.Combine(cache, EngineCacheMigration.CompletionMarker)));
    }

    [Fact]
    public async Task FailedLegacyMigration_DoesNotPreventEngineCreation()
    {
        using var directory = new CoreTestDirectory();
        var target = Path.Combine(directory.Path, "real-downloads");
        Directory.CreateDirectory(Path.Combine(target, "fastresume"));
        var linked = Path.Combine(directory.Path, "linked-downloads");
        Directory.CreateSymbolicLink(linked, target);
        using var storage = new StorageService(new StoragePaths(directory.Path, linked));
        await using var service = new TorrentService(storage, new CoreServiceFixture.TestNotifications(), new RefusingBackground(), ImmediateDispatcher.Instance);
        // Proxy mode keeps the engine free of listeners and DHT sockets in this test.
        service.UpdateProxySettings(true, "127.0.0.1", 1080, "", "");

        var engine = (ClientEngine)typeof(TorrentService)
            .GetMethod("CreateEngine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, null)!;

        try
        {
            Assert.Equal(Path.Combine(directory.Path, "EngineCache"), engine.Settings.CacheDirectory);
        }
        finally
        {
            engine.Dispose();
        }
    }

    [Theory]
    [InlineData(" proxy.example ", "proxy.example")]
    [InlineData("[::1]", "::1")]
    [InlineData("::1", "::1")]
    [InlineData("", "")]
    public void ProxyHost_IsTrimmedAndIPv6BracketsAreRemoved(string input, string expected)
        => Assert.Equal(expected, TorrentService.NormalizeProxyHost(input));

    [Fact]
    public void ProxyUri_BracketsIPv6Literals()
    {
        Assert.Equal("socks5://[::1]:1080/", TorrentService.BuildProxyUri("::1", 1080).ToString());
        Assert.Equal("socks5://proxy.example:9050/", TorrentService.BuildProxyUri("proxy.example", 9050).ToString());
    }

    [Fact]
    public async Task LegacyPlaintextProxyPassword_MovesToSecretStoreAndLeavesNoCopyOnDisk()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new MemorySecretStore();
        using (var legacy = new StorageService(directory.StoragePaths))
        {
            await legacy.SaveSettingsAsync(new AppSettings { ProxyEnabled = true, ProxyPassword = "hunter2" });
            await legacy.SaveSettingsAsync(new AppSettings { ProxyEnabled = true, ProxyPassword = "hunter2" });
        }

        using var storage = new StorageService(directory.StoragePaths, secrets);
        Assert.Equal("hunter2", (await storage.LoadSettingsAsync()).ProxyPassword);
        Assert.Equal("hunter2", secrets.Values[StorageService.ProxyPasswordSecretKey]);
        foreach (var file in Directory.GetFiles(directory.Path, "torrents.json*"))
        {
            Assert.DoesNotContain("hunter2", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }

        using var reopened = new StorageService(directory.StoragePaths, secrets);
        Assert.Equal("hunter2", (await reopened.LoadSettingsAsync()).ProxyPassword);
    }

    [Fact]
    public async Task ProxyPassword_IsSavedToSecretStoreAndClearedWhenEmpty()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new MemorySecretStore();
        using var storage = new StorageService(directory.StoragePaths, secrets);
        var settings = new AppSettings { ProxyUsername = "user", ProxyPassword = "secret" };

        await storage.SaveSettingsAsync(settings);

        Assert.Equal("secret", settings.ProxyPassword);
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(Path.Combine(directory.Path, "torrents.json"), TestContext.Current.CancellationToken));
        Assert.Equal("secret", (await storage.LoadSettingsAsync()).ProxyPassword);

        await storage.SaveSettingsAsync(new AppSettings { ProxyUsername = "user" });
        Assert.False(secrets.Values.ContainsKey(StorageService.ProxyPasswordSecretKey));
        Assert.Equal(string.Empty, (await storage.LoadSettingsAsync()).ProxyPassword);
    }

    [Fact]
    public async Task BrokenSecretStore_KeepsProxyPasswordUsable()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths, new MemorySecretStore { Fail = true });

        await storage.SaveSettingsAsync(new AppSettings { ProxyPassword = "fallback" });

        Assert.Equal("fallback", (await storage.LoadSettingsAsync()).ProxyPassword);
    }

    [Fact]
    public async Task SecretReadFailure_DoesNotDeleteProxyPasswordOnUnrelatedSave()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new MemorySecretStore();
        using var storage = new StorageService(directory.StoragePaths, secrets);
        await storage.SaveSettingsAsync(new AppSettings { ProxyUsername = "user", ProxyPassword = "secret" });

        secrets.FailingReads = 1;
        await AppSettingsPersistence.MergeAndSaveAsync(storage, existing => AppSettingsFactory.CreateWithSortByStatus(existing, sortByStatus: true));

        var reloaded = await storage.LoadSettingsAsync();
        Assert.Equal("secret", reloaded.ProxyPassword);
        Assert.False(reloaded.ProxyPasswordUnavailable);
        Assert.True(reloaded.SortByStatus);
    }

    [Fact]
    public async Task SettingsPage_KeepsProxyPasswordItCouldNotRead()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new MemorySecretStore();
        using var storage = new StorageService(directory.StoragePaths, secrets);
        await storage.SaveSettingsAsync(new AppSettings { ProxyUsername = "user", ProxyPassword = "secret" });
        secrets.FailingReads = 1;
        var page = await storage.LoadSettingsAsync();
        Assert.True(page.ProxyPasswordUnavailable);
        Assert.Equal(string.Empty, page.ProxyPassword);

        // Secure storage works again by the time the page saves an unrelated change.
        var saved = await SaveFromSettingsPageAsync(storage, page.ProxyPassword, proxyPasswordUnavailable: true);
        Assert.Equal("secret", saved.ProxyPassword);
        Assert.Equal("secret", secrets.Values[StorageService.ProxyPasswordSecretKey]);

        // Once the user has edited the field, an empty password is a deliberate removal.
        await SaveFromSettingsPageAsync(storage, string.Empty, proxyPasswordUnavailable: false);
        Assert.False(secrets.Values.ContainsKey(StorageService.ProxyPasswordSecretKey));
    }

    [Fact]
    public async Task SeedingTime_IsSavedAtTheProgressIntervalAndPromptlyWhenSeedingEnds()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        UpdateSeedingTime(fixture.Service, torrent, active: true);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveIfPendingAsync");
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveAsync");

        // A monitor tick of a seeding torrent must not rewrite the state file every 5 seconds.
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        UpdateSeedingTime(fixture.Service, torrent, active: true);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveIfPendingAsync");
        Assert.Equal(0, await ReadSavedSeededSecondsAsync(fixture));

        fixture.Clock.Advance(TimeSpan.FromSeconds(25));
        UpdateSeedingTime(fixture.Service, torrent, active: true);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveIfPendingAsync");
        Assert.Equal(30, await ReadSavedSeededSecondsAsync(fixture));

        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        UpdateSeedingTime(fixture.Service, torrent, active: false);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveIfPendingAsync");
        Assert.Equal(32, await ReadSavedSeededSecondsAsync(fixture));
    }

    [Fact]
    public async Task MissingStateFileAfterInterruptedRotation_RecoversInsteadOfLookingEmpty()
    {
        using var directory = new CoreTestDirectory();
        using (var storage = new StorageService(directory.StoragePaths))
        {
            await storage.LoadTorrentsAsync();
            await storage.SaveTorrentsAsync([new TorrentItem { Id = "first" }]);
            await storage.SaveTorrentsAsync([new TorrentItem { Id = "second" }]);
        }

        // Simulate a stop after the state file was rotated into the backup.
        File.Delete(Path.Combine(directory.Path, "torrents.json"));

        using var reopened = new StorageService(directory.StoragePaths);
        Assert.Equal("first", Assert.Single(await reopened.LoadTorrentsAsync()).Id);
        Assert.True(File.Exists(Path.Combine(directory.Path, "torrents.json")));
    }

    [Fact]
    public async Task RoutineSaves_KeepPreviousStateAsBackupWithoutCopying()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        await storage.SaveSettingsAsync(new AppSettings { ProxyHost = "kept.example" });
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "one" }]);
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "two" }]);

        var backup = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(directory.Path, "torrents.json.bak"), TestContext.Current.CancellationToken));
        Assert.Equal("one", backup.GetProperty("torrents")[0].GetProperty("id").GetString());
        Assert.Equal("kept.example", backup.GetProperty("settings").GetProperty("proxyHost").GetString());
        Assert.False(File.Exists(Path.Combine(directory.Path, "torrents.json.tmp")));
    }

    [Fact]
    public void KeepDeviceAwake_IsPreservedBySortPreferenceSaves()
    {
        var existing = new AppSettings { KeepDeviceAwake = true };
        Assert.True(AppSettingsFactory.CreateWithSortByStatus(existing, true).KeepDeviceAwake);
        Assert.True(AppSettingsFactory.CreateForSettingsPage(existing, 0, 0, 1, 1, 0, 0, true, "", false, "", 1080, "", "", null, null).KeepDeviceAwake);
        Assert.False(AppSettingsFactory.CreateForSettingsPage(existing, 0, 0, 1, 1, 0, 0, true, "", false, "", 1080, "", "", null, null, keepDeviceAwake: false).KeepDeviceAwake);
    }

    private static async Task<(TorrentItem Torrent, string PayloadPath, string MetadataCachePath)> CreateRestoredMagnetDownloadAsync(CoreServiceFixture fixture)
    {
        var downloads = fixture.Storage.GetDefaultDownloadPath();
        var payloadPath = Path.Combine(downloads, "magnet-payload.bin");
        await File.WriteAllTextAsync(payloadPath, "downloaded through a magnet link");
        var torrentPath = Path.Combine(fixture.Directory.Path, "magnet-payload.torrent");
        await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(payloadPath), torrentPath);
        var metadata = await Torrent.LoadAsync(torrentPath);
        // MonoTorrent saves magnet metadata as <cache>/metadata/<infohash>.torrent.
        var metadataCachePath = Path.Combine(fixture.Engine.Settings.MetadataCacheDirectory, metadata.InfoHashes.V1OrV2.ToHex() + ".torrent");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataCachePath)!);
        File.Copy(torrentPath, metadataCachePath);
        File.Delete(torrentPath);

        var hash = metadata.InfoHashes.V1!.ToHex().ToLowerInvariant();
        var torrent = new TorrentItem
        {
            Name = metadata.Name,
            InfoHash = hash,
            MagnetLink = $"magnet:?xt=urn:btih:{hash}",
            SavePath = downloads,
            Progress = 100,
            Status = DownloadStatus.Queued
        };
        fixture.Service.Torrents.Add(torrent);
        return (torrent, payloadPath, metadataCachePath);
    }

    private static TorrentItem CreateMagnetItem(CoreServiceFixture fixture, double progress)
    {
        var hash = Guid.NewGuid().ToString("N") + "01234567";
        return new TorrentItem
        {
            Name = "unknown-" + hash,
            InfoHash = hash,
            MagnetLink = $"magnet:?xt=urn:btih:{hash}",
            SavePath = fixture.Storage.GetDefaultDownloadPath(),
            Progress = progress,
            Status = DownloadStatus.Paused
        };
    }

    private static Task<AppSettings> SaveFromSettingsPageAsync(StorageService storage, string proxyPassword, bool proxyPasswordUnavailable)
        => AppSettingsPersistence.MergeAndSaveAsync(storage, existing => AppSettingsFactory.CreateForSettingsPage(
            existing, existing.GlobalDownloadLimitKbps, existing.GlobalUploadLimitKbps, existing.MaxActiveDownloads,
            existing.MaxActiveSeeds, existing.GlobalMaxSeedRatio, existing.GlobalMaxSeedMinutes,
            existing.DownloadToTorrentFolder, existing.SpecificDownloadFolder, existing.ProxyEnabled, existing.ProxyHost,
            existing.ProxyPort, existing.ProxyUsername, proxyPassword, existing.Language, existing.Theme,
            proxyPasswordUnavailable: proxyPasswordUnavailable));

    private static void UpdateSeedingTime(TorrentService service, TorrentItem torrent, bool active)
        => typeof(TorrentService)
            .GetMethod("UpdateSeedingTime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [torrent, active]);

    private static async Task<double> ReadSavedSeededSecondsAsync(CoreServiceFixture fixture)
    {
        using var reader = new StorageService(fixture.Directory.StoragePaths);
        return Assert.Single(await reader.LoadTorrentsAsync()).SeededSeconds;
    }

    private static void InvokeUpdateBackgroundState(TorrentService service)
        => typeof(TorrentService)
            .GetMethod("UpdateBackgroundTransferState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [false]);

    private sealed class RefusingBackground : IBackgroundDownloadService
    {
        public bool Accept { get; set; } = true;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public bool Start()
        {
            StartCalls++;
            return Accept;
        }

        public void Stop() => StopCalls++;
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool Fail { get; init; }
        public int FailingReads { get; set; }

        public Task<string?> GetAsync(string key)
        {
            if (Fail) throw new InvalidOperationException("Keystore unavailable");
            if (FailingReads > 0)
            {
                FailingReads--;
                throw new InvalidOperationException("Keystore temporarily unavailable");
            }
            return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetAsync(string key, string? value)
        {
            if (Fail) throw new InvalidOperationException("Keystore unavailable");
            if (string.IsNullOrEmpty(value)) Values.Remove(key);
            else Values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class TestNetwork : ITransferNetworkMonitor
    {
        public bool IsWifiConnected { get; set; } = true;
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
