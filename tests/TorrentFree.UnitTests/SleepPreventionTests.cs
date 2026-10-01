using System.Collections.Concurrent;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class SleepPreventionTests
{
    [Fact]
    public async Task Setting_IsOffByDefaultAndPersistsAcrossRestart()
    {
        using var directory = new CoreTestDirectory();
        using (var storage = new StorageService(directory.StoragePaths))
        {
            Assert.False((await storage.LoadSettingsAsync()).KeepDeviceAwake);
            await storage.SaveSettingsAsync(new AppSettings { KeepDeviceAwake = true });
        }
        using var restored = new StorageService(directory.StoragePaths);
        Assert.True((await restored.LoadSettingsAsync()).KeepDeviceAwake);
    }

    [Theory]
    [InlineData(DownloadStatus.Downloading, 0, true)]
    [InlineData(DownloadStatus.Downloading, 99.9, true)]
    [InlineData(DownloadStatus.Downloading, 100, false)]
    [InlineData(DownloadStatus.Seeding, 100, false)]
    [InlineData(DownloadStatus.Completed, 100, false)]
    [InlineData(DownloadStatus.Paused, 50, false)]
    [InlineData(DownloadStatus.Stopped, 50, false)]
    [InlineData(DownloadStatus.Failed, 50, false)]
    [InlineData(DownloadStatus.Queued, 50, false)]
    [InlineData(DownloadStatus.WaitingForWifi, 50, false)]
    public async Task OnlyActiveIncompleteDownloads_PreventSleep(DownloadStatus status, double progress, bool preventSleep)
    {
        var sleep = new RecordingSleepPrevention();
        await using var fixture = new CoreServiceFixture(sleepPreventionService: sleep);
        var torrent = await AddMagnetAsync(fixture.Service, 1);
        fixture.Service.UpdateKeepDeviceAwake(true);

        torrent.Progress = progress;
        torrent.Status = status;

        Assert.Equal(preventSleep, sleep.PreventSleep);
    }

    [Fact]
    public async Task DisabledSetting_AllowsSleepDuringDownloadsAndTakesEffectImmediatelyWhenToggled()
    {
        var sleep = new RecordingSleepPrevention();
        await using var fixture = new CoreServiceFixture(sleepPreventionService: sleep);
        var torrent = await AddMagnetAsync(fixture.Service, 1);
        torrent.Status = DownloadStatus.Downloading;
        Assert.False(sleep.PreventSleep);
        Assert.DoesNotContain(true, sleep.Requests);

        fixture.Service.UpdateKeepDeviceAwake(true);
        Assert.True(sleep.PreventSleep);
        fixture.Service.UpdateKeepDeviceAwake(false);
        Assert.False(sleep.PreventSleep);
    }

    [Fact]
    public async Task MixedDownloads_ReleaseOnlyAfterLastActiveDownloadCompletesAndReacquireOnResume()
    {
        var sleep = new RecordingSleepPrevention();
        await using var fixture = new CoreServiceFixture(sleepPreventionService: sleep);
        var first = await AddMagnetAsync(fixture.Service, 1);
        var second = await AddMagnetAsync(fixture.Service, 2);
        var paused = await AddMagnetAsync(fixture.Service, 3);
        var stopped = await AddMagnetAsync(fixture.Service, 4);
        paused.Status = DownloadStatus.Paused;
        stopped.Status = DownloadStatus.Stopped;
        fixture.Service.UpdateKeepDeviceAwake(true);
        first.Status = second.Status = DownloadStatus.Downloading;
        Assert.True(sleep.PreventSleep);

        first.Progress = 100;
        first.Status = DownloadStatus.Seeding;
        Assert.True(sleep.PreventSleep);
        second.Progress = 100;
        second.Status = DownloadStatus.Seeding;
        Assert.False(sleep.PreventSleep);

        paused.Status = DownloadStatus.Downloading;
        Assert.True(sleep.PreventSleep);
        paused.Status = DownloadStatus.Paused;
        Assert.False(sleep.PreventSleep);
        stopped.Status = DownloadStatus.Downloading;
        Assert.True(sleep.PreventSleep);
        stopped.Status = DownloadStatus.Stopped;
        Assert.False(sleep.PreventSleep);
    }

    [Fact]
    public async Task RemovingLastDownload_ReleasesRequestWhileSeedingContinues()
    {
        var sleep = new RecordingSleepPrevention();
        await using var fixture = new CoreServiceFixture(sleepPreventionService: sleep);
        var downloading = await AddMagnetAsync(fixture.Service, 1);
        var seeding = await AddMagnetAsync(fixture.Service, 2);
        seeding.Progress = 100;
        seeding.Status = DownloadStatus.Seeding;
        downloading.Status = DownloadStatus.Downloading;
        fixture.Service.UpdateKeepDeviceAwake(true);
        Assert.True(sleep.PreventSleep);

        Assert.True((await fixture.Service.RemoveTorrentAsync(downloading)).Removed);

        Assert.False(sleep.PreventSleep);
        Assert.Same(seeding, Assert.Single(fixture.Service.Torrents));
    }

    [Fact]
    public async Task Shutdown_ReleasesSleepRequestAndCannotReacquireIt()
    {
        var sleep = new RecordingSleepPrevention();
        await using var fixture = new CoreServiceFixture(sleepPreventionService: sleep);
        var torrent = await AddMagnetAsync(fixture.Service, 1);
        fixture.Service.UpdateKeepDeviceAwake(true);
        torrent.Status = DownloadStatus.Downloading;
        Assert.True(sleep.PreventSleep);

        await fixture.Service.DisposeAsync();

        Assert.False(sleep.PreventSleep);
        torrent.Progress = 10;
        fixture.Service.UpdateKeepDeviceAwake(true);
        Assert.False(sleep.PreventSleep);
    }

    private static async Task<TorrentItem> AddMagnetAsync(TorrentService service, int number)
        => (await service.AddTorrentAsync($"magnet:?xt=urn:btih:{number:x40}"))!;

    private sealed class RecordingSleepPrevention : ISleepPreventionService
    {
        private int _preventSleep;
        public bool PreventSleep => Volatile.Read(ref _preventSleep) != 0;
        public ConcurrentQueue<bool> Requests { get; } = new();
        public void SetPreventSleep(bool preventSleep)
        {
            Interlocked.Exchange(ref _preventSleep, preventSleep ? 1 : 0);
            Requests.Enqueue(preventSleep);
        }
    }
}
