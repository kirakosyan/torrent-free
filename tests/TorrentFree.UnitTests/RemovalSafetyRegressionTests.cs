using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class RemovalSafetyRegressionTests
{
    [Fact]
    public async Task FailedStop_KeepsActiveTorrentVisibleMonitoredAndRetryable()
    {
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock)
            => new FailingStopService(storage, notifications, background, clock));
        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var payloadPath = Path.Combine(fixture.Storage.GetDefaultDownloadPath(), "payload.bin");
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
        var service = (FailingStopService)fixture.Service;
        service.FailStop = true;

        await Assert.ThrowsAsync<IOException>(() => service.RemoveTorrentAsync(torrent, true, true));

        Assert.Same(torrent, Assert.Single(service.Torrents));
        Assert.Equal(TorrentState.Seeding, Assert.Single(fixture.Engine.Torrents).State);
        Assert.True(torrent.CanPause);
        Assert.True(torrent.CanStop);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(payloadPath));
        Assert.Equal(torrent.Id, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).Id);
        var samples = torrent.DownloadSpeedHistory.Count;
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DownloadSpeedHistory.Count > samples);

        service.FailStop = false;
        var result = await service.RemoveTorrentAsync(torrent, true, true);
        Assert.True(result.Removed);
        Assert.Empty(service.Torrents);
        Assert.Empty(fixture.Engine.Torrents);
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(payloadPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPayload_IsPreservedForOtherTrackedTorrent(bool startOther)
    {
        await using var fixture = new CoreServiceFixture();
        var firstMetadata = await fixture.PrepareTorrentAsync();
        var first = (await fixture.Service.AddTorrentFileAsync(firstMetadata))!;
        // Keep the old torrent from auto-starting and rewriting the shared file while the
        // other torrent is hash-checking. This test exercises removal, not competing writes.
        first.Status = DownloadStatus.Paused;
        var payload = Path.Combine(first.SavePath, "payload.bin");
        await File.WriteAllTextAsync(payload, "Different torrent with the same payload path", TestContext.Current.CancellationToken);
        var secondPath = Path.Combine(fixture.Directory.Path, "second.torrent");
        await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(payload), secondPath, TestContext.Current.CancellationToken);
        var secondMetadata = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser())
            .PrepareAsync(new TorrentPickedFile("second.torrent", null, await File.ReadAllBytesAsync(secondPath, TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        var second = (await fixture.Service.AddTorrentFileAsync(secondMetadata))!;
        Assert.NotEqual(first.InfoHash, second.InfoHash);
        if (startOther)
        {
            await fixture.Service.StartTorrentAsync(second);
            await CoreServiceFixture.WaitUntilAsync(() => second.Status == DownloadStatus.Seeding);
        }
        else second.Status = DownloadStatus.Paused;

        var removal = await fixture.Service.RemoveTorrentAsync(first, deleteFiles: true);

        Assert.True(removal.Removed);
        Assert.True(removal.DownloadedFilesLeftInPlace);
        Assert.Same(second, Assert.Single(fixture.Service.Torrents));
        Assert.Equal("Different torrent with the same payload path", await File.ReadAllTextAsync(payload, TestContext.Current.CancellationToken));

        var finalRemoval = await fixture.Service.RemoveTorrentAsync(second, deleteFiles: true);
        Assert.False(finalRemoval.DownloadedFilesLeftInPlace);
        Assert.False(File.Exists(payload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedSourceTorrent_IsPreservedAndReported(bool validReplacement)
    {
        await using var fixture = new CoreServiceFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        byte[] replacement;
        if (validReplacement)
        {
            await fixture.PrepareTorrentAsync("replacement.bin");
            replacement = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory.Path, "replacement.bin.torrent"), TestContext.Current.CancellationToken);
        }
        else replacement = "Unrelated replacement file"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, replacement, TestContext.Current.CancellationToken);

        var result = await fixture.Service.RemoveTorrentAsync(torrent, true, true);

        Assert.True(result.Removed);
        Assert.True(result.TorrentFileLeftInPlace);
        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.Equal(replacement, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(torrent.SavePath, "payload.bin")));
    }

    [Fact]
    public async Task TorrentWithoutMetadata_ProtectsPotentiallySharedPayload()
    {
        await using var fixture = new CoreServiceFixture();
        var selected = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        var other = (await fixture.Service.AddTorrentAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"))!;
        Assert.Equal(selected.SavePath, other.SavePath);

        var result = await fixture.Service.RemoveTorrentAsync(selected, deleteFiles: true);

        Assert.True(result.DownloadedFilesLeftInPlace);
        Assert.True(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        Assert.Same(other, Assert.Single(fixture.Service.Torrents));
    }

    private sealed class FailingStopService(IStorageService storage, INotificationService notifications,
        IBackgroundDownloadService background, TimeProvider clock)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, clock)
    {
        public bool FailStop { get; set; }
        protected override Task StopManagerAsync(TorrentManager manager)
            => FailStop ? Task.FromException(new IOException("Injected stop failure")) : base.StopManagerAsync(manager);
    }
}
