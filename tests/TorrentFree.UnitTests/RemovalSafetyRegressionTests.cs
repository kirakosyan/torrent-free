using MonoTorrent;
using MonoTorrent.Client;
using System.Collections.Concurrent;
using System.Reflection;
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
            => new RemovalTestService(storage, notifications, background, clock));
        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var payloadPath = Path.Combine(fixture.Storage.GetDefaultDownloadPath(), "payload.bin");
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
        var service = (RemovalTestService)fixture.Service;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TorrentWithoutMetadata_DoesNotBlockDeletingAnotherTorrentsKnownFiles(bool hasProgress)
    {
        await using var fixture = new CoreServiceFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        var other = (await fixture.Service.AddTorrentAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"))!;
        Assert.Equal(selected.SavePath, other.SavePath);
        other.SavePath = fixture.Directory.Path; // Includes both the payload and source metadata.
        other.Status = DownloadStatus.Paused;
        if (hasProgress) other.Progress = 1;

        var result = await fixture.Service.RemoveTorrentAsync(selected, true, true);

        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.False(result.TorrentFileLeftInPlace);
        Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        Assert.False(File.Exists(sourcePath));
        Assert.Same(other, Assert.Single(fixture.Service.Torrents));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompletedLegacyTorrentInSharedFolder_DoesNotBlockSelectedDownload(bool startSelected, bool corruptMetadata)
    {
        await using var fixture = new CoreServiceFixture();
        var otherMetadata = await fixture.PrepareTorrentAsync("legacy.bin");
        var other = (await fixture.Service.AddTorrentFileAsync(otherMetadata))!;
        other.Status = DownloadStatus.Stopped;
        other.Progress = 100;
        other.DownloadedSize = new FileInfo(Path.Combine(other.SavePath, "legacy.bin")).Length;
        other.DateCompleted = DateTime.Now;
        if (corruptMetadata)
            await File.WriteAllTextAsync(other.CachedTorrentFilePath!, "Unreadable legacy metadata", TestContext.Current.CancellationToken);
        else
            File.Delete(other.CachedTorrentFilePath!);
        await fixture.Storage.SaveTorrentsAsync(fixture.Service.Torrents);
        var restored = Assert.Single(await fixture.Storage.LoadTorrentsAsync());
        fixture.Service.Torrents.Clear();
        fixture.Service.Torrents.Add(restored);

        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        Assert.Equal(restored.SavePath, selected.SavePath);
        if (startSelected)
        {
            await fixture.Service.StartTorrentAsync(selected);
            await CoreServiceFixture.WaitUntilAsync(() => selected.Status == DownloadStatus.Seeding);
        }
        else selected.Status = DownloadStatus.Stopped;

        var result = await fixture.Service.RemoveTorrentAsync(selected, true, true);

        Assert.True(result.Removed);
        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.False(result.TorrentFileLeftInPlace);
        Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(restored.SavePath, "legacy.bin")));
        Assert.Same(restored, Assert.Single(fixture.Service.Torrents));
        Assert.Equal(restored.Id, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).Id);
    }

    [Fact]
    public async Task FailedEngineRemoval_KeepsStoppedManagerVisibleWithRetryControlsAndFiles()
    {
        await using var fixture = CreateFixture();
        var service = (RemovalTestService)fixture.Service;
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var torrent = (await service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        await service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single().State == TorrentState.Seeding);
        service.FailRemove = true;

        await Assert.ThrowsAsync<IOException>(() => service.RemoveTorrentAsync(torrent, true, true));

        Assert.Same(torrent, Assert.Single(service.Torrents));
        Assert.Equal(TorrentState.Stopped, Assert.Single(fixture.Engine.Torrents).State);
        Assert.Equal(DownloadStatus.Stopped, torrent.Status);
        Assert.True(torrent.CanStart);
        Assert.False(torrent.CanPause);
        Assert.Equal(DownloadStatus.Stopped, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).Status);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(torrent.CachedTorrentFilePath));
        Assert.True(File.Exists(Path.Combine(torrent.SavePath, "payload.bin")));
        var notificationCalls = fixture.Notifications.Calls;
        await Task.Delay(1250, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadStatus.Stopped, torrent.Status);
        Assert.Equal(notificationCalls, fixture.Notifications.Calls);

        service.FailRemove = false;
        Assert.True((await service.RemoveTorrentAsync(torrent, true, true)).Removed);
        Assert.Empty(fixture.Engine.Torrents);
        Assert.Empty(service.Torrents);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(torrent.CachedTorrentFilePath));
        Assert.False(File.Exists(Path.Combine(torrent.SavePath, "payload.bin")));
    }

    [Fact]
    public async Task Removal_CancelsMonitorBeforeStopAndAllowsUnrelatedImportWhileStopWaits()
    {
        var observer = new CompletionObserver();
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock)
            => new RemovalTestService(storage, notifications, background, clock, observer));
        var service = (RemovalTestService)fixture.Service;
        var torrent = (await service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        var incoming = await fixture.PrepareTorrentAsync("incoming.bin");
        await service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single().State == TorrentState.Seeding);
        torrent.DateCompleted = null;
        torrent.Status = DownloadStatus.Downloading;
        service.BlockStop = true;
        var removal = service.RemoveTorrentAsync(torrent, deleteFiles: true);
        try
        {
            await service.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var tokens = (ConcurrentDictionary<string, CancellationTokenSource>)typeof(TorrentService)
                .GetField("_downloadTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            Assert.False(tokens.ContainsKey(torrent.Id));
            var notificationCalls = fixture.Notifications.Calls;
            var observerCalls = observer.Calls;
            var samples = torrent.DownloadSpeedHistory.Count;
            var imported = await service.AddTorrentFileAsync(incoming).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            imported!.Status = DownloadStatus.Paused;
            await Task.Delay(1250, TestContext.Current.CancellationToken);
            Assert.Null(torrent.DateCompleted);
            Assert.Equal(DownloadStatus.Downloading, torrent.Status);
            Assert.Equal(samples, torrent.DownloadSpeedHistory.Count);
            Assert.Equal(notificationCalls, fixture.Notifications.Calls);
            Assert.Equal(observerCalls, observer.Calls);
        }
        finally { service.ReleaseStop.TrySetResult(); }
        Assert.True((await removal).Removed);
    }

    [Fact]
    public async Task ImportDuringDeletion_WaitsForOwnershipGateAndKeepsItsMetadataCache()
    {
        await using var fixture = CreateFixture();
        var service = (RemovalTestService)fixture.Service;
        var selected = (await service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        var other = (await service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync("other.bin")))!;
        selected.Status = other.Status = DownloadStatus.Paused;
        var incoming = await fixture.PrepareTorrentAsync("incoming.bin");
        service.Torrents.CollectionChanged += (_, _) =>
        {
            var added = service.Torrents.FirstOrDefault(t => string.Equals(t.InfoHash, incoming.InfoHashHex, StringComparison.OrdinalIgnoreCase));
            if (added is not null) added.Status = DownloadStatus.Paused;
        };
        service.BlockOwnershipId = other.Id;
        var removal = service.RemoveTorrentAsync(selected, deleteFiles: true);
        Task<TorrentItem?>? import = null;
        try
        {
            await service.OwnershipEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            import = service.AddTorrentFileAsync(incoming);
            await CoreServiceFixture.WaitUntilAsync(() => File.Exists(incoming.CachedFilePath));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(import.IsCompleted);
            Assert.Equal(2, service.Torrents.Count);
            Assert.True(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        }
        finally { service.ReleaseOwnership.TrySetResult(); }
        Assert.True((await removal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Removed);
        var added = await import!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(added!, service.Torrents);
        Assert.True(File.Exists(added!.CachedTorrentFilePath));
        Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        Assert.True(File.Exists(Path.Combine(added.SavePath, "incoming.bin")));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("cache")]
    [InlineData("save")]
    public async Task MalformedOtherPath_DoesNotPreventUnrelatedDeletion(string malformedProperty)
    {
        await using var fixture = CreateFixture();
        var service = fixture.Service;
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        var other = await AddEmptyMagnetAsync(service);
        switch (malformedProperty)
        {
            case "source": other.TorrentFilePath = "bad\0source.torrent"; break;
            case "cache": other.CachedTorrentFilePath = "bad\0cache.torrent"; break;
            case "save": other.SavePath = "bad\0directory"; break;
        }

        var result = await service.RemoveTorrentAsync(selected, true, true);

        Assert.True(result.Removed);
        Assert.False(result.TorrentFileLeftInPlace);
        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(selected.CachedTorrentFilePath));
        Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        Assert.Same(other, Assert.Single(service.Torrents));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipReads_SkipSourceOnlyRemovalAndDisjointSavePaths(bool deletePayload)
    {
        await using var fixture = CreateFixture();
        var service = (RemovalTestService)fixture.Service;
        var metadata = await fixture.PrepareTorrentAsync();
        var selected = (await service.AddTorrentFileAsync(metadata with
            { SourceFilePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent") }))!;
        var other = (await service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync("other.bin")))!;
        other.SavePath = Path.Combine(fixture.Directory.Path, "disjoint");
        other.Status = DownloadStatus.Paused;

        var result = await service.RemoveTorrentAsync(selected, true, deletePayload);

        Assert.True(result.Removed);
        Assert.False(result.TorrentFileLeftInPlace);
        Assert.DoesNotContain(other.Id, service.OwnershipReads);
        Assert.Equal(deletePayload ? new[] { selected.Id } : [], service.OwnershipReads.ToArray());
    }

    [Fact]
    public async Task SourceInsideAnotherDownloadedDirectory_IsDeletedWhenOwnershipIsUnknown()
    {
        await using var fixture = CreateFixture();
        var service = (RemovalTestService)fixture.Service;
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        var other = await AddEmptyMagnetAsync(service);
        other.SavePath = fixture.Directory.Path;
        other.DownloadedSize = 10;

        var result = await service.RemoveTorrentAsync(selected, deleteTorrentFile: true);

        Assert.False(result.TorrentFileLeftInPlace);
        Assert.False(File.Exists(source));
        Assert.Contains(other.Id, service.OwnershipReads);
    }

    [Fact]
    public async Task SourceUsedAsAnotherTorrentsPayload_IsPreservedEvenBeforeProgressIsRecorded()
    {
        await using var fixture = CreateFixture();
        var service = (RemovalTestService)fixture.Service;
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        var otherMetadataPath = Path.Combine(fixture.Directory.Path, "owner.torrent");
        await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(source), otherMetadataPath, TestContext.Current.CancellationToken);
        var otherMetadata = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser()).PrepareAsync(
            new TorrentPickedFile("owner.torrent", null, await File.ReadAllBytesAsync(otherMetadataPath, TestContext.Current.CancellationToken)),
            TestContext.Current.CancellationToken);
        var other = (await service.AddTorrentFileAsync(otherMetadata))!;
        other.SavePath = fixture.Directory.Path;
        other.Status = DownloadStatus.Paused;

        var result = await service.RemoveTorrentAsync(selected, deleteTorrentFile: true);

        Assert.True(result.TorrentFileLeftInPlace);
        Assert.True(File.Exists(source));
        Assert.Contains(other.Id, service.OwnershipReads);
    }

    [Fact]
    public async Task RemovalSave_DoesNotHoldOwnershipGateOrPreventImportPublication()
    {
        BlockingSaveStorage? delayedStorage = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock)
            => new RemovalTestService(delayedStorage = new BlockingSaveStorage(storage), notifications, background, clock));
        var service = fixture.Service;
        var selected = (await service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        var incoming = await fixture.PrepareTorrentAsync("incoming.bin");
        service.Torrents.CollectionChanged += (_, _) =>
        {
            var added = service.Torrents.FirstOrDefault(t => string.Equals(t.InfoHash, incoming.InfoHashHex, StringComparison.OrdinalIgnoreCase));
            if (added is not null) added.Status = DownloadStatus.Paused;
        };
        delayedStorage!.BlockNextSave = true;
        var removal = service.RemoveTorrentAsync(selected, deleteFiles: true);
        Task<TorrentItem?>? import = null;
        try
        {
            await delayedStorage.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            import = service.AddTorrentFileAsync(incoming);
            await CoreServiceFixture.WaitUntilAsync(() => service.Torrents.Any(t => string.Equals(t.InfoHash, incoming.InfoHashHex, StringComparison.OrdinalIgnoreCase)));
            Assert.False(removal.IsCompleted);
            Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
        }
        finally { delayedStorage.ReleaseSave.TrySetResult(); }
        Assert.True((await removal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Removed);
        var added = await import!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(added!.Id, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).Id);
        Assert.True(File.Exists(added.CachedTorrentFilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileUriSource_UsesSameNormalizationForDeletionAndSharedProtection(bool shared)
    {
        await using var fixture = CreateFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        var source = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var selected = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = source }))!;
        selected.TorrentFilePath = new Uri(source).AbsoluteUri;
        var other = await AddEmptyMagnetAsync(fixture.Service);
        other.TorrentFilePath = shared ? new Uri(source).AbsoluteUri : "content://provider/document/source.torrent";

        var result = await fixture.Service.RemoveTorrentAsync(selected, true, true);

        Assert.Equal(shared, result.TorrentFileLeftInPlace);
        Assert.Equal(shared, File.Exists(source));
        Assert.False(result.DownloadedFilesLeftInPlace);
        Assert.False(File.Exists(Path.Combine(selected.SavePath, "payload.bin")));
    }

    private static CoreServiceFixture CreateFixture() => new(serviceFactory: (storage, notifications, background, clock)
        => new RemovalTestService(storage, notifications, background, clock));

    private static async Task<TorrentItem> AddEmptyMagnetAsync(TorrentService service)
    {
        var torrent = (await service.AddTorrentAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"))!;
        torrent.Status = DownloadStatus.Paused;
        return torrent;
    }

    private sealed class RemovalTestService(IStorageService storage, INotificationService notifications,
        IBackgroundDownloadService background, TimeProvider clock, IDownloadCompletionObserver? observer = null)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, clock, observer)
    {
        public bool FailStop { get; set; }
        public bool FailRemove { get; set; }
        public bool BlockStop { get; set; }
        public string? BlockOwnershipId { get; set; }
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OwnershipEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOwnership { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> OwnershipReads { get; } = new();

        protected override async Task StopManagerAsync(TorrentManager manager)
        {
            if (FailStop) throw new IOException("Injected stop failure");
            if (BlockStop)
            {
                StopEntered.TrySetResult();
                await ReleaseStop.Task;
            }
            await base.StopManagerAsync(manager);
        }
        protected override Task RemoveManagerAsync(TorrentManager manager)
            => FailRemove ? Task.FromException(new IOException("Injected engine removal failure")) : base.RemoveManagerAsync(manager);
        protected override async Task<OwnedDownloadFiles> ResolveOwnedDownloadFilesAsync(TorrentItem torrent, TorrentManager? manager)
        {
            OwnershipReads.Enqueue(torrent.Id);
            if (torrent.Id == BlockOwnershipId)
            {
                OwnershipEntered.TrySetResult();
                await ReleaseOwnership.Task;
            }
            return await base.ResolveOwnedDownloadFilesAsync(torrent, manager);
        }
    }

    private sealed class CompletionObserver : IDownloadCompletionObserver
    {
        public int Calls;
        public Task OnDownloadCompletedAsync(TorrentItem torrent) { Interlocked.Increment(ref Calls); return Task.CompletedTask; }
    }

    private sealed class BlockingSaveStorage(IStorageService inner) : IStorageService
    {
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        public bool BlockNextSave { get; set; }
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
        {
            await _saveLock.WaitAsync();
            try
            {
                if (BlockNextSave)
                {
                    BlockNextSave = false;
                    SaveEntered.TrySetResult();
                    await ReleaseSave.Task;
                }
                await inner.SaveTorrentsAsync(torrents);
            }
            finally { _saveLock.Release(); }
        }
        public Task<List<TorrentItem>> LoadTorrentsAsync() => inner.LoadTorrentsAsync();
        public Task<AppSettings> LoadSettingsAsync() => inner.LoadSettingsAsync();
        public Task SaveSettingsAsync(AppSettings settings) => inner.SaveSettingsAsync(settings);
        public Task UpdateDesktopWindowStateAsync(bool? maximized) => inner.UpdateDesktopWindowStateAsync(maximized);
        public string GetDefaultDownloadPath() => inner.GetDefaultDownloadPath();
        public string GetAppDataPath() => inner.GetAppDataPath();
        public bool SupportsCustomDownloadLocations => inner.SupportsCustomDownloadLocations;
    }
}
