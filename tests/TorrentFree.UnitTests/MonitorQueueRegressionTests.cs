using System.Collections.Specialized;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class MonitorQueueRegressionTests
{
    [Fact]
    public async Task EngineStopsSeeding_StartsWaitingSeedAutomatically()
    {
        await using var fixture = new CoreServiceFixture();
        var (active, queued) = await PrepareQueueAsync(fixture, seeding: true);
        var manager = Assert.Single(fixture.Engine.Torrents);

        // Stop the engine directly: the monitor, not the user's Stop command, must drain the queue.
        await manager.StopAsync();

        await CoreServiceFixture.WaitUntilAsync(() => active.Status == DownloadStatus.Completed);
        await CoreServiceFixture.WaitUntilAsync(() => queued.Status == DownloadStatus.Seeding);
        Assert.Equal(2, fixture.Engine.Torrents.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MonitorFailure_DrainsQueueOnlyAfterSuccessfulCleanup(bool seeding, bool failStop)
    {
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock)
            => new CleanupService(storage, notifications, background, clock));
        var (active, queued) = await PrepareQueueAsync(fixture, seeding);
        var manager = Assert.Single(fixture.Engine.Torrents);
        var service = (CleanupService)fixture.Service;
        service.FailStop = failStop;
        NotifyCollectionChangedEventHandler? failNextSample = null;
        failNextSample = (_, _) =>
        {
            active.DownloadSpeedHistory.CollectionChanged -= failNextSample;
            throw new IOException("Injected monitor failure");
        };
        active.DownloadSpeedHistory.CollectionChanged += failNextSample;

        try
        {
            await CoreServiceFixture.WaitUntilAsync(() => active.ErrorMessage == "Injected monitor failure");
            if (failStop)
            {
                Assert.Equal(DownloadStatus.Seeding, active.Status);
                Assert.Equal(TorrentState.Seeding, manager.State);
                Assert.True(active.CanStop);
                Assert.Equal(DownloadStatus.Queued, queued.Status);
                Assert.Single(fixture.Engine.Torrents);
            }
            else
            {
                Assert.Equal(DownloadStatus.Failed, active.Status);
                Assert.Equal(TorrentState.Stopped, manager.State);
                await CoreServiceFixture.WaitUntilAsync(() => queued.Status != DownloadStatus.Queued);
                Assert.Contains(queued.Status, new[] { DownloadStatus.Downloading, DownloadStatus.Seeding });
                await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Count == 2);
            }
        }
        finally
        {
            active.DownloadSpeedHistory.CollectionChanged -= failNextSample;
            service.FailStop = false;
        }
    }

    private static async Task<(TorrentItem Active, TorrentItem Queued)> PrepareQueueAsync(CoreServiceFixture fixture, bool seeding)
    {
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(1, 1);
        var active = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync("active.bin")))!;
        if (!seeding) File.Delete(Path.Combine(active.SavePath, "active.bin"));
        await fixture.Service.StartTorrentAsync(active);
        await CoreServiceFixture.WaitUntilAsync(() => Assert.Single(fixture.Engine.Torrents).State ==
            (seeding ? TorrentState.Seeding : TorrentState.Downloading));
        await CoreServiceFixture.WaitUntilAsync(() => active.Status ==
            (seeding ? DownloadStatus.Seeding : DownloadStatus.Downloading));

        var queued = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync("waiting.bin")))!;
        if (seeding) queued.Progress = 100;
        await fixture.Service.StartTorrentAsync(queued);
        Assert.Equal(DownloadStatus.Queued, queued.Status);
        return (active, queued);
    }

    private sealed class CleanupService(IStorageService storage, INotificationService notifications,
        IBackgroundDownloadService background, TimeProvider clock)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, clock)
    {
        public bool FailStop;
        protected override Task StopManagerAsync(TorrentManager manager)
            => FailStop ? Task.FromException(new IOException("Injected stop failure")) : base.StopManagerAsync(manager);
    }
}
