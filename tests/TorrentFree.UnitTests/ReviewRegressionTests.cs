using System.Reflection;
using System.Text.Json;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class ReviewRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoweringCapacity_QueuesOnlyExcessTransfersAndResumesWhenSlotIsFreed(bool seed)
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(3, 3);
        var activeStatus = seed ? DownloadStatus.Seeding : DownloadStatus.Downloading;
        var items = new List<TorrentItem>();
        for (var i = 0; i < 3; i++)
        {
            var metadata = await fixture.PrepareTorrentAsync($"payload-{i}.bin");
            if (!seed) File.Delete(Path.Combine(fixture.Storage.GetDefaultDownloadPath(), $"payload-{i}.bin"));
            var torrent = (await fixture.Service.AddTorrentFileAsync(metadata))!;
            torrent.DateAdded = new DateTime(2026, 1, 1).AddMinutes(i);
            torrent.Progress = seed ? 100 : 0;
            await fixture.Service.StartTorrentAsync(torrent);
            items.Add(torrent);
        }
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.All(m =>
            m.State == (seed ? TorrentState.Seeding : TorrentState.Downloading)));

        fixture.Service.UpdateQueueLimits(2, 2);
        // Race settings enforcement with monitor-style checks on every seed.
        if (seed)
            await Task.WhenAll(items.Select(t => CoreServiceFixture.InvokeAsync(fixture.Service,
                "EnforceSeedingLimitsAsync", t, fixture.Engine.Torrents.Single(m => m.InfoHashes.V1!.ToHex() == t.InfoHash))));
        await CoreServiceFixture.InvokeAsync(fixture.Service, "ReconcileQueueLimitsAsync");

        Assert.Equal(2, items.Count(t => t.Status == activeStatus));
        Assert.Equal(DownloadStatus.Queued, items[2].Status);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single(m => m.InfoHashes.V1!.ToHex() == items[2].InfoHash).State == TorrentState.Stopped);
        Assert.DoesNotContain(items, t => t.Status == DownloadStatus.Paused);

        await fixture.Service.RemoveTorrentAsync(items[0]);
        Assert.Equal(activeStatus, items[2].Status);
        Assert.Equal(2, fixture.Service.Torrents.Count(t => t.Status == activeStatus));

        fixture.Service.UpdateQueueLimits(1, 1);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "ReconcileQueueLimitsAsync");
        Assert.Single(fixture.Service.Torrents, t => t.Status == DownloadStatus.Queued);
        fixture.Service.UpdateQueueLimits(0, 0);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "ReconcileQueueLimitsAsync");
        Assert.All(fixture.Service.Torrents, t => Assert.Equal(activeStatus, t.Status));
    }

    [Fact]
    public async Task EditingDisabledProxy_DoesNotReserveRebuildOrReplaceActiveManager()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        var manager = Assert.Single(fixture.Engine.Torrents);

        fixture.Service.UpdateProxySettings(false, "proxy.example", 9050, "user", "password");
        var barrier = typeof(TorrentService).GetField("_activeEngineRebuild", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(barrier.GetValue(fixture.Service));
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        Assert.Same(manager, Assert.Single(fixture.Engine.Torrents));
        Assert.Equal(TorrentState.Seeding, manager.State);
    }

    [Theory]
    [InlineData(DownloadStatus.Queued)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Completed)]
    public async Task IdleTorrentLimits_AreSavedByDebouncedSave(DownloadStatus status)
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.Status = status;
        torrent.DownloadLimitKbps = 45;
        torrent.UploadLimitKbps = 67;
        torrent.MaxSeedRatio = 1.5;
        torrent.MaxSeedMinutes = 120;
        await CoreServiceFixture.InvokeAsync(fixture.Service, "SaveIfPendingAsync");

        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        var restored = Assert.Single(await reopened.LoadTorrentsAsync());
        Assert.Equal(45, restored.DownloadLimitKbps);
        Assert.Equal(67, restored.UploadLimitKbps);
        Assert.Equal(1.5, restored.MaxSeedRatio);
        Assert.Equal(120, restored.MaxSeedMinutes);
    }

    [Fact]
    public async Task Dispose_FlushesPendingIdleLimitEdit()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.MaxSeedMinutes = 42;
        await fixture.Service.DisposeAsync();
        Assert.Equal(42, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).MaxSeedMinutes);
    }

    [Fact]
    public async Task ExplicitStart_RecoversAfterBackgroundTimeout()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.PauseAllForBackgroundTimeoutAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
        Assert.Single(fixture.Engine.Torrents);
    }

    [Fact]
    public async Task RemovingTorrent_DeletesOnlyItsOwnedMetadataCopyByDefault()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        await fixture.Service.RemoveTorrentAsync(torrent);
        Assert.False(File.Exists(metadata.CachedFilePath));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(fixture.Storage.GetDefaultDownloadPath(), "payload.bin")));
    }

    [Fact]
    public async Task RemovingTorrent_DoesNotTrustCachedPathOutsideOwnedDirectory()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        var externalPath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        torrent.CachedTorrentFilePath = externalPath;
        await fixture.Service.RemoveTorrentAsync(torrent);
        Assert.True(File.Exists(externalPath));
    }

    [Fact]
    public async Task EngineCaches_AreCreatedInsideAppData()
    {
        await using var fixture = new CoreServiceFixture();
        var method = typeof(TorrentService).GetMethod("CreateEngine", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var engine = (ClientEngine)method.Invoke(fixture.Service, null)!;
        Assert.Equal(Path.Combine(fixture.Storage.GetAppDataPath(), "EngineCache"), engine.Settings.CacheDirectory);
        Assert.False(PathGuard.IsPathWithinDirectory(engine.Settings.CacheDirectory, fixture.Storage.GetDefaultDownloadPath()));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task CorruptState_RecoversBackupAndPreservesBadFile(string corruptJson)
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        await storage.SaveSettingsAsync(new AppSettings { ProxyHost = "kept.example", Language = "nb-NO" });
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "kept" }]);
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "newer" }]);
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path, corruptJson, TestContext.Current.CancellationToken);

        Assert.Equal("kept", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        Assert.Equal("kept.example", (await storage.LoadSettingsAsync()).ProxyHost);
        var badFile = Assert.Single(Directory.GetFiles(directory.Path, "torrents.json.corrupt-*"));
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(badFile, TestContext.Current.CancellationToken));
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "saved-after-recovery" }]);
        Assert.Equal("saved-after-recovery", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        Assert.Equal("nb-NO", (await storage.LoadSettingsAsync()).Language);
    }

    [Fact]
    public void TransientStats_AreNeitherRestoredNorPersisted()
    {
        const string json = """{"DownloadSpeed":123,"UploadSpeed":45,"Seeders":6,"Leechers":7,"EstimatedSecondsRemaining":90000,"HealthScore":80,"AvailabilityPercent":75,"AvailabilityLabel":"75%","DisplayIndex":5,"DownloadedSize":1234} """;
        var torrent = JsonSerializer.Deserialize<TorrentItem>(json)!;
        Assert.Equal(0, torrent.DownloadSpeed);
        Assert.Equal(0, torrent.UploadSpeed);
        Assert.Equal(0, torrent.Seeders);
        Assert.Equal(0, torrent.Leechers);
        Assert.Equal(0, torrent.EstimatedSecondsRemaining);
        Assert.Equal(0, torrent.HealthScore);
        Assert.Equal(0, torrent.AvailabilityPercent);
        Assert.Equal("—", torrent.AvailabilityLabel);
        Assert.Equal(0, torrent.DisplayIndex);
        Assert.Equal(1234, torrent.DownloadedSize);
        var saved = JsonSerializer.Serialize(torrent);
        foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject().Where(p => p.Name != "DownloadedSize"))
            Assert.DoesNotContain($"\"{property.Name}\"", saved);
    }

    [Theory]
    [InlineData(0, "—")]
    [InlineData(59, "00:59")]
    [InlineData(3600, "01:00:00")]
    [InlineData(90061, "25:01:01")]
    [InlineData(long.MaxValue, "2562047788015215:30:07")]
    public void Eta_KeepsTotalHoursWithoutWrapping(long seconds, string expected)
        => Assert.Equal(expected, new TorrentItem { EstimatedSecondsRemaining = seconds }.FormattedEstimatedTime);

    [Fact]
    public async Task DownloadPath_UsesAndPersistsEngineEscaping()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var original = await fixture.PrepareTorrentAsync();
        var data = BEncodedValue.Decode<BEncodedDictionary>(await File.ReadAllBytesAsync(original.CachedFilePath!, TestContext.Current.CancellationToken));
        ((BEncodedDictionary)data["info"])["name"] = new BEncodedString("Name: Part 2");
        var metadata = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser())
            .PrepareAsync(new TorrentPickedFile("renamed.torrent", null, data.Encode()), TestContext.Current.CancellationToken);
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata))!;
        await fixture.Service.StartTorrentAsync(torrent);
        var managerPath = Assert.Single(Assert.Single(fixture.Engine.Torrents).Files).FullPath;
        await fixture.Service.PauseTorrentAsync(torrent);
        Assert.Equal(managerPath, torrent.DownloadedFilePath);
        Assert.Equal(managerPath, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).DownloadedFilePath);
        await File.WriteAllTextAsync(managerPath, "downloaded", TestContext.Current.CancellationToken);
        torrent.Status = DownloadStatus.Completed;
        Assert.True(torrent.CanOpenDownloadedFile);
    }
}
