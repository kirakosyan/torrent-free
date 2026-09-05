using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class CoreTransferRegressionTests
{
    [Fact]
    public async Task ProviderImport_RetainsMetadataAndStartsWithoutMetadataPeers()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var metadata = await fixture.PrepareTorrentAsync();
        Assert.Null(metadata.SourceFilePath);
        Assert.Null(metadata.DownloadSourcePath);
        Assert.True(File.Exists(metadata.CachedFilePath));
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory.Path, "payload.bin.torrent"), TestContext.Current.CancellationToken);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(metadata.CachedFilePath!, TestContext.Current.CancellationToken));
        File.Delete(Path.Combine(fixture.Directory.Path, "payload.bin.torrent"));

        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata))!;
        var persisted = Assert.Single(await fixture.Storage.LoadTorrentsAsync());
        Assert.Equal(metadata.CachedFilePath, persisted.CachedTorrentFilePath);
        Assert.Equal(fixture.Storage.GetDefaultDownloadPath(), torrent.SavePath);
        await fixture.Service.StartTorrentAsync(torrent);
        Assert.True(Assert.Single(fixture.Engine.Torrents).HasMetadata);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotStopMonitoringOrDisableTransferControls()
    {
        await using var fixture = new CoreServiceFixture();
        fixture.Notifications.Throw = true;
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Notifications.Calls > 0 && torrent.DownloadSpeedHistory.Count >= 2);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        Assert.Equal(TorrentState.Seeding, Assert.Single(fixture.Engine.Torrents).State);
        Assert.True(torrent.CanPause);
        Assert.True(torrent.CanStop);
        Assert.Null(torrent.ErrorMessage);
        await fixture.Service.PauseTorrentAsync(torrent);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
    }

    [Fact]
    public async Task CompletedTorrent_UsesSeedCapacityWhileDownloadCapacityIsFull()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(1, 2);
        fixture.Service.Torrents.Add(new TorrentItem { Id = "busy-download", Status = DownloadStatus.Downloading });
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.Progress = 100;
        torrent.Status = DownloadStatus.Completed;
        await fixture.Service.StartTorrentAsync(torrent);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        await CoreServiceFixture.WaitUntilAsync(() => Assert.Single(fixture.Engine.Torrents).State == TorrentState.Seeding);
    }

    [Fact]
    public async Task SeedQueue_ResumesWhenSeedCapacityIsFreed()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(1, 1);
        var blocker = new TorrentItem { Id = "busy-seed", Status = DownloadStatus.Seeding };
        fixture.Service.Torrents.Add(blocker);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.Progress = 100;
        torrent.Status = DownloadStatus.Completed;
        await fixture.Service.StartTorrentAsync(torrent);
        Assert.Equal(DownloadStatus.Queued, torrent.Status);
        Assert.Empty(fixture.Engine.Torrents);
        await fixture.Service.RemoveTorrentAsync(blocker);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        Assert.Single(fixture.Engine.Torrents);
    }

    [Fact]
    public async Task SeedingClock_ExcludesPausedTimeAndPersistsAccumulatedActiveTime()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateSeedingLimits(0, 1);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateSeedingStarted is not null);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.Service.PauseTorrentAsync(torrent);
        Assert.Equal(30, torrent.SeededSeconds);
        Assert.Null(torrent.DateSeedingStarted);

        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateSeedingStarted is not null);
        fixture.Clock.Advance(TimeSpan.FromSeconds(29));
        await CoreServiceFixture.InvokeAsync(fixture.Service, "EnforceSeedingLimitsAsync", torrent, Assert.Single(fixture.Engine.Torrents));
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await CoreServiceFixture.InvokeAsync(fixture.Service, "EnforceSeedingLimitsAsync", torrent, Assert.Single(fixture.Engine.Torrents));
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        Assert.Equal(60, torrent.SeededSeconds);
        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        var restored = Assert.Single(await reopened.LoadTorrentsAsync());
        Assert.Equal(60, restored.SeededSeconds);
        Assert.Null(restored.DateSeedingStarted);
    }

    [Fact]
    public async Task FinishingDownload_QueuesForSeedCapacityAndNotifiesOnce()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(1, 1);
        var blocker = new TorrentItem { Id = "busy-seed", Status = DownloadStatus.Seeding };
        fixture.Service.Torrents.Add(blocker);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Queued && fixture.Notifications.Calls == 1);
        Assert.Equal(100, torrent.Progress);
        Assert.NotNull(torrent.DateCompleted);
        Assert.Equal(TorrentState.Stopped, Assert.Single(fixture.Engine.Torrents).State);
        await fixture.Service.RemoveTorrentAsync(blocker);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateSeedingStarted is not null);
        Assert.Equal(1, fixture.Notifications.Calls);
    }

    [Fact]
    public async Task MissingPayload_UsesDownloadCapacityAfterRecheckingCompletedTorrent()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(1, 1);
        var blocker = new TorrentItem { Id = "busy-download", Status = DownloadStatus.Downloading };
        fixture.Service.Torrents.Add(blocker);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        File.Delete(Path.Combine(fixture.Storage.GetDefaultDownloadPath(), "payload.bin"));
        torrent.Progress = 100;
        torrent.Status = DownloadStatus.Completed;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Queued && Assert.Single(fixture.Engine.Torrents).State == TorrentState.Stopped);
        Assert.Equal(0, torrent.Progress);
        Assert.Equal(0, torrent.SeededSeconds);
        await fixture.Service.RemoveTorrentAsync(blocker);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
    }

    [Fact]
    public async Task RestoredActiveSession_PreservesAccumulatedTimeWithoutCountingDowntime()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Storage.LoadTorrentsAsync();
        await fixture.Storage.SaveTorrentsAsync([new TorrentItem
        {
            Id = "saved-seed", Status = DownloadStatus.Seeding, SeededSeconds = 30,
            DateSeedingStarted = DateTime.Now.AddDays(-1),
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"
        }]);
        await fixture.Service.InitializeAsync();
        var restored = Assert.Single(fixture.Service.Torrents);
        Assert.Equal(DownloadStatus.Paused, restored.Status);
        Assert.Equal(30, restored.SeededSeconds);
        Assert.Null(restored.DateSeedingStarted);
    }

    [Fact]
    public async Task Initialization_CanRetryStorageReadFailureWithoutLosingExistingTorrent()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Storage.LoadTorrentsAsync();
        await fixture.Storage.SaveTorrentsAsync([new TorrentItem
        {
            Id = "saved", MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", Status = DownloadStatus.Paused
        }]);
        using (File.Open(Path.Combine(fixture.Directory.Path, "torrents.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.Service.InitializeAsync());
        await fixture.Service.InitializeAsync();
        Assert.Equal("saved", Assert.Single(fixture.Service.Torrents).Id);
    }
}
