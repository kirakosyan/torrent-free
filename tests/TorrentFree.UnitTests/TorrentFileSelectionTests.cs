using MonoTorrent;
using MonoTorrent.Client;
using System.Collections.Concurrent;
using System.Reflection;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentFileSelectionTests
{
    [Fact]
    public void NullSearch_RestoresAllFilesWithoutChangingSelection()
    {
        var picker = new TorrentFileSelectionViewModel();
        picker.Load([new TorrentFileChoice("one.bin", 1), new TorrentFileChoice("two.bin", 2, false)]);
        picker.SearchText = "one";
        picker.SearchText = null;
        Assert.Equal(2, picker.VisibleFiles.Count);
        Assert.Equal(["one.bin"], picker.SelectedPaths);
    }

    [Fact]
    public async Task NarrowingCompletedSelection_KeepsCompletionAndUsesSeedCapacity()
    {
        var observer = new CompletionObserver();
        await using var fixture = new CoreServiceFixture(completionObserver: observer);
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var source = Path.Combine(fixture.Directory.Path, "source", "File selection sample");
        var destination = Path.Combine(torrent.SavePath, "File selection sample");
        foreach (var name in new[] { "chapter-two.bin", "cover.bin" })
            File.Copy(Path.Combine(source, name), Path.Combine(destination, name));
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Notifications.Calls == 1 && observer.Calls == 1);
        var completed = torrent.DateCompleted;
        fixture.Service.UpdateQueueLimits(1, 2);
        var busy = new TorrentItem { Name = "Occupies download capacity", Status = DownloadStatus.Downloading };
        fixture.Service.Torrents.Add(busy);
        var wanted = (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]);

        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        Assert.Equal(100, torrent.Progress);
        Assert.Equal(completed, torrent.DateCompleted);
        Assert.Equal(wanted.Length, torrent.DownloadedSize);
        var samples = torrent.DownloadSpeedHistory.Count;
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DownloadSpeedHistory.Count > samples);
        Assert.Equal(1, fixture.Notifications.Calls);
        Assert.Equal(1, observer.Calls);
        Assert.Equal(completed, torrent.DateCompleted);
        fixture.Service.Torrents.Remove(busy);
    }

    [Theory]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Completed)]
    public async Task InactiveSelection_PreservesStatusAndError(DownloadStatus status)
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var wanted = (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        torrent.Status = status;
        torrent.ErrorMessage = status == DownloadStatus.Failed ? "Previous transfer error" : null;
        var error = torrent.ErrorMessage;
        if (status == DownloadStatus.Completed)
        {
            torrent.Progress = 100;
            torrent.DateCompleted = fixture.Clock.GetLocalNow().DateTime;
        }
        var completed = torrent.DateCompleted;
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]);
        Assert.Equal(status, torrent.Status);
        Assert.Equal(error, torrent.ErrorMessage);
        Assert.Equal(completed, torrent.DateCompleted);
        if (status == DownloadStatus.Completed) Assert.Equal(100, torrent.Progress);
        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        var saved = Assert.Single(await reopened.LoadTorrentsAsync());
        Assert.Equal(status, saved.Status);
        Assert.Equal(error, saved.ErrorMessage);
    }

    [Fact]
    public async Task ActiveSaveFailure_RestoresManagerMonitorAndPersistedSelection()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await fixture.Service.StartTorrentAsync(torrent);
        var manager = Assert.Single(fixture.Engine.Torrents);
        await CoreServiceFixture.WaitUntilAsync(() => manager.State == TorrentState.Downloading && torrent.Progress > 0);
        var total = torrent.TotalSize;
        var progress = torrent.Progress;
        var wanted = (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken)).Single(file => file.Path.EndsWith("cover.bin"));
        using (File.Open(Path.Combine(fixture.Directory.Path, "torrents.json.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.Service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]));
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Null(torrent.SelectedFilePaths);
        Assert.Equal(total, torrent.TotalSize);
        Assert.Equal(progress, torrent.Progress);
        Assert.All(manager.Files, file => Assert.Equal(Priority.Normal, file.Priority));
        var samples = torrent.DownloadSpeedHistory.Count;
        await CoreServiceFixture.WaitUntilAsync(() => manager.State == TorrentState.Downloading && torrent.DownloadSpeedHistory.Count > samples);
        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        var saved = Assert.Single(await reopened.LoadTorrentsAsync());
        Assert.Null(saved.SelectedFilePaths);
        Assert.Equal(DownloadStatus.Downloading, saved.Status);
    }

    [Fact]
    public async Task StopFailure_RestoresMonitoringWithoutChangingSelection()
    {
        await using var fixture = CreateLifecycleFixture();
        var service = (SelectionLifecycleService)fixture.Service;
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single().State == TorrentState.Downloading);
        var wanted = (await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        service.FailStop = true;
        try { await Assert.ThrowsAsync<IOException>(() => service.SetTorrentFileSelectionAsync(torrent, [wanted.Path])); }
        finally { service.FailStop = false; }
        Assert.Null(torrent.SelectedFilePaths);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        var samples = torrent.DownloadSpeedHistory.Count;
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DownloadSpeedHistory.Count > samples);
    }

    [Fact]
    public async Task ChangingSelection_CancelsMonitorBeforeStopCanPublishCompletion()
    {
        await using var fixture = CreateLifecycleFixture();
        var service = (SelectionLifecycleService)fixture.Service;
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var choices = await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        var complete = choices.Single(file => file.Path.EndsWith("chapter-one.bin"));
        await service.SetTorrentFileSelectionAsync(torrent, [complete.Path]);
        await service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single().State == TorrentState.Seeding);
        torrent.DateCompleted = null;
        torrent.Status = DownloadStatus.Downloading;
        service.BlockStop = true;
        var change = service.SetTorrentFileSelectionAsync(torrent, choices.Select(file => file.Path).ToArray());
        try
        {
            await service.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var tokens = (ConcurrentDictionary<string, CancellationTokenSource>)typeof(TorrentService)
                .GetField("_downloadTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            Assert.False(tokens.ContainsKey(torrent.Id));
            var notifications = fixture.Notifications.Calls;
            await Task.Delay(1200, TestContext.Current.CancellationToken);
            Assert.Null(torrent.DateCompleted);
            Assert.Equal(DownloadStatus.Downloading, torrent.Status);
            Assert.Equal(notifications, fixture.Notifications.Calls);
        }
        finally { service.ReleaseStop.TrySetResult(); service.BlockStop = false; }
        await change;
    }

    [Fact]
    public async Task RestartFailure_DoesNotReportTheSavedSelectionAsFailed()
    {
        await using var fixture = CreateLifecycleFixture();
        var service = (SelectionLifecycleService)fixture.Service;
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Engine.Torrents.Single().State == TorrentState.Downloading);
        var wanted = (await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        service.FailStart = true;
        await service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]);
        Assert.Equal(DownloadStatus.Failed, torrent.Status);
        Assert.Equal("Injected restart failure", torrent.ErrorMessage);
        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        Assert.Equal([wanted.Path], Assert.Single(await reopened.LoadTorrentsAsync()).SelectedFilePaths!);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BadCachedMetadata_IsRefetchedForPickerAndSelectedStart(bool mismatched, bool start)
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var wanted = (await service!.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        await service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]);
        service.Metadata = await File.ReadAllBytesAsync(torrent.CachedTorrentFilePath!, TestContext.Current.CancellationToken);
        var bad = "invalid torrent"u8.ToArray();
        if (mismatched)
        {
            await fixture.PrepareTorrentAsync("different.bin");
            bad = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory.Path, "different.bin.torrent"), TestContext.Current.CancellationToken);
        }
        await File.WriteAllBytesAsync(torrent.CachedTorrentFilePath!, bad, TestContext.Current.CancellationToken);
        if (start)
        {
            await service.StartTorrentAsync(torrent);
            Assert.Single(Assert.Single(fixture.Engine.Torrents).Files, file => file.Priority == Priority.Normal);
        }
        else Assert.Single(await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken), file => file.IsSelected);
        Assert.Equal(1, service.PreviewCalls);
        Assert.Equal(service.Metadata, await File.ReadAllBytesAsync(torrent.CachedTorrentFilePath!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BackgroundSuspensionBeforePreviewRegistration_RequeuesSelectedStart()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await service!.SetTorrentFileSelectionAsync(torrent, [(await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0].Path]);
        File.Delete(torrent.CachedTorrentFilePath!);
        service.SuspendBeforeMetadata = true;
        await service.StartTorrentAsync(torrent);
        Assert.Equal(DownloadStatus.Queued, torrent.Status);
        Assert.Null(torrent.ErrorMessage);
        Assert.Equal(0, service.PreviewCalls);
    }

    [Fact]
    public async Task PreviewDeadline_DoesNotApplyToPayloadMetadataStarts()
    {
        var clock = new PreviewDeadlineClock();
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, _) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await service!.SetTorrentFileSelectionAsync(torrent, [(await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0].Path]);
        File.Delete(torrent.CachedTorrentFilePath!);
        var picker = service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        await CoreServiceFixture.WaitUntilAsync(() => service.PreviewCalls == 1);
        clock.ExpirePreviewDeadline();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => picker);
        var start = service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => service.PreviewCalls == 2);
        clock.ExpirePreviewDeadline();
        Assert.False(service.PreviewToken.IsCancellationRequested);
        Assert.False(start.IsCompleted);
        await service.PauseAllForBackgroundTimeoutAsync();
        await start;
        Assert.Equal(DownloadStatus.Queued, torrent.Status);
        Assert.Null(torrent.ErrorMessage);
    }

    private static CoreServiceFixture CreateLifecycleFixture() => new(serviceFactory: (storage, notifications, background, clock) =>
        new SelectionLifecycleService(storage, notifications, background, clock));

    private sealed class CompletionObserver : IDownloadCompletionObserver
    {
        public int Calls;
        public Task OnDownloadCompletedAsync(TorrentItem torrent) { Interlocked.Increment(ref Calls); return Task.CompletedTask; }
    }

    private sealed class SelectionLifecycleService(IStorageService storage, INotificationService notifications, IBackgroundDownloadService background, TimeProvider clock)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, clock)
    {
        public bool FailStop;
        public bool FailStart;
        public bool BlockStop;
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task StopManagerAsync(TorrentManager manager)
        {
            if (FailStop) throw new IOException("Injected stop failure");
            if (BlockStop) { StopEntered.TrySetResult(); await ReleaseStop.Task; }
            await base.StopManagerAsync(manager);
        }
        protected override Task StartManagerAsync(TorrentManager manager)
            => FailStart ? Task.FromException(new IOException("Injected restart failure")) : base.StartManagerAsync(manager);
    }

    private sealed class PreviewDeadlineClock : TimeProvider
    {
        private readonly List<DeadlineTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DeadlineTimer(callback, state);
            lock (_timers) _timers.Add(timer);
            return timer;
        }
        public void ExpirePreviewDeadline()
        {
            DeadlineTimer[] timers;
            lock (_timers) timers = _timers.ToArray();
            foreach (var timer in timers) timer.Fire();
        }
        private sealed class DeadlineTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public void SearchAndBulkSelection_PreserveHiddenChoicesAndRejectEmptySelection()
    {
        var files = new[] { new TorrentFileChoice("music/one.flac", 100), new TorrentFileChoice("music/two.flac", 200), new TorrentFileChoice("cover.jpg", 50) };
        var picker = new TorrentFileSelectionViewModel();
        picker.Load(files);
        picker.SearchText = "MUSIC/";
        Assert.Equal(2, picker.VisibleFiles.Count);
        picker.SelectNoneCommand.Execute(null);
        Assert.Equal(["cover.jpg"], picker.SelectedPaths);
        Assert.True(picker.CanConfirm);
        picker.SearchText = "";
        picker.SelectNoneCommand.Execute(null);
        Assert.False(picker.CanConfirm);
        picker.ToggleFileCommand.Execute(files[0]);
        Assert.Equal(["music/one.flac"], picker.SelectedPaths);
        Assert.Contains("100 B", picker.Summary);
    }

    [Fact]
    public async Task NewImports_StayPausedAndPreviewDoesNotCreateAPayloadManager()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        await fixture.Service.StartQueuedTorrentsAsync();
        var files = await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        Assert.Equal(3, files.Count);
        Assert.All(files, file => Assert.True(file.IsSelected));
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        Assert.Empty(fixture.Engine.Torrents);
        files[0].IsSelected = false;
        Assert.Null(torrent.SelectedFilePaths!); // A cancelled dialog is a detached draft.
    }

    [Fact]
    public async Task Selection_PersistsAndIsAppliedBeforeStartingMonoTorrent()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var files = await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        var wanted = files.First(file => file.Path.EndsWith("chapter-one.bin"));
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [wanted.Path]);
        using var reopened = new StorageService(fixture.Directory.StoragePaths);
        var saved = Assert.Single(await reopened.LoadTorrentsAsync());
        Assert.Equal([wanted.Path], saved.SelectedFilePaths!);
        Assert.Equal(wanted.Length, saved.TotalSize);
        Assert.Equal(DownloadStatus.Paused, saved.Status);
        Assert.False((await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken)).Single(file => file.Path.EndsWith("cover.bin")).IsSelected);

        // Start the deserialized item with no in-memory manager or selection draft.
        fixture.Service.Torrents.Clear();
        fixture.Service.Torrents.Add(saved);
        await fixture.Service.StartTorrentAsync(saved);
        var manager = Assert.Single(fixture.Engine.Torrents);
        Assert.All(manager.Files, file => Assert.Equal(file.Path.Replace('\\', '/') == wanted.Path ? Priority.Normal : Priority.DoNotDownload, file.Priority));
    }

    [Fact]
    public async Task SelectedFilesComplete_ReportsSelectedSizeAndNotifiesOnce()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var selected = (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken)).Single(file => file.Path.EndsWith("chapter-one.bin"));
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [selected.Path]);
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateCompleted is not null && fixture.Notifications.Calls == 1);
        var manager = Assert.Single(fixture.Engine.Torrents);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        Assert.Equal(100, torrent.Progress);
        Assert.True(manager.Progress < 100);
        Assert.Equal(selected.Length, torrent.TotalSize);
        Assert.Equal(selected.Length, torrent.DownloadedSize);
        Assert.False(File.Exists(manager.Files.Single(file => file.Path.EndsWith("cover.bin")).FullPath));

        // Expanding the selection restarts the active transfer and resets completion.
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken)).Select(file => file.Path).ToArray());
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Downloading);
        Assert.Null(torrent.SelectedFilePaths!);
        Assert.Null(torrent.DateCompleted);
        Assert.All(manager.Files, file => Assert.Equal(Priority.Normal, file.Priority));
        Assert.Equal(1, fixture.Notifications.Calls);
    }

    [Fact]
    public async Task InvalidSelections_DoNotChangeTheSavedChoice()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var file = (await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0];
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [file.Path]);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SetTorrentFileSelectionAsync(torrent, []));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SetTorrentFileSelectionAsync(torrent, ["../unknown"]));
        Assert.Equal([file.Path], torrent.SelectedFilePaths!);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
    }

    [Fact]
    public async Task SaveFailure_RetainsPreviousSelectionAndTransferTotals()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var files = await fixture.Service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        await fixture.Service.SetTorrentFileSelectionAsync(torrent, [files[0].Path]);
        var previousTotal = torrent.TotalSize;
        using (File.Open(Path.Combine(fixture.Directory.Path, "torrents.json.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.Service.SetTorrentFileSelectionAsync(torrent, files.Select(file => file.Path).ToArray()));
        Assert.Equal([files[0].Path], torrent.SelectedFilePaths!);
        Assert.Equal(previousTotal, torrent.TotalSize);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
    }

    [Fact]
    public async Task MissingMetadata_RefetchesBeforeStartingSavedPartialSelection()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var selected = (await service!.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0].Path;
        await service.SetTorrentFileSelectionAsync(torrent, [selected]);
        service.Metadata = await File.ReadAllBytesAsync(torrent.CachedTorrentFilePath!, TestContext.Current.CancellationToken);
        File.Delete(torrent.CachedTorrentFilePath!);
        await service.StartTorrentAsync(torrent);
        Assert.Equal(1, service.PreviewCalls);
        var manager = Assert.Single(fixture.Engine.Torrents);
        Assert.Single(manager.Files, file => file.Priority == Priority.Normal);
        Assert.All(manager.Files.Where(file => file.Path.Replace('\\', '/') != selected), file => Assert.Equal(Priority.DoNotDownload, file.Priority));
    }

    [Fact]
    public async Task MagnetPreview_DownloadsOnlyMetadataAndCachesItForOfflineUse()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var metadata = await fixture.PrepareTorrentAsync();
        service!.Metadata = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory.Path, "payload.bin.torrent"), TestContext.Current.CancellationToken);
        var torrent = (await service.AddTorrentAsync($"magnet:?xt=urn:btih:{metadata.InfoHashHex}", startPaused: true))!;
        Assert.Single(await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken));
        Assert.Equal(1, service.PreviewCalls);
        Assert.Empty(fixture.Engine.Torrents);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        Assert.True(File.Exists(torrent.CachedTorrentFilePath));
        Assert.Single(await service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken));
        Assert.Equal(1, service.PreviewCalls);
    }

    [Fact]
    public async Task CancelledMagnetPreview_LeavesPausedTorrentAndCanRetry()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = (await service!.AddTorrentAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", startPaused: true))!;
        using var cancel = new CancellationTokenSource();
        var preview = service.GetTorrentFilesAsync(torrent, cancel.Token);
        await CoreServiceFixture.WaitUntilAsync(() => service.PreviewCalls == 1);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
        Assert.Null(torrent.CachedTorrentFilePath);
        Assert.Empty(fixture.Engine.Torrents);
        // The keyed operation lock was released, so removal remains responsive.
        Assert.True((await service.RemoveTorrentAsync(torrent)).Removed);
    }

    [Fact]
    public async Task WifiLossDuringSelectedRestore_PreservesAutomaticResumeIntent()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = await AddMultiFileTorrentAsync(fixture);
        var selected = (await service!.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken))[0].Path;
        await service.SetTorrentFileSelectionAsync(torrent, [selected]);
        File.Delete(torrent.CachedTorrentFilePath!);
        var start = service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => service.PreviewCalls == 1);
        await service.UpdateWifiOnlyAsync(true).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await start;
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Equal([selected], torrent.SelectedFilePaths!);
        Assert.Null(torrent.ErrorMessage);
    }

    [Fact]
    public async Task WifiPolicy_CancelsMetadataPreviewWithoutStartingPayload()
    {
        PreviewService? service = null;
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, clock) =>
            service = new PreviewService(storage, notifications, background, clock));
        var torrent = (await service!.AddTorrentAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", startPaused: true))!;
        var preview = service.GetTorrentFilesAsync(torrent, TestContext.Current.CancellationToken);
        await CoreServiceFixture.WaitUntilAsync(() => service.PreviewCalls == 1);
        await service.UpdateWifiOnlyAsync(true).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview);
        Assert.Equal(DownloadStatus.Paused, torrent.Status);
    }

    private static async Task<TorrentItem> AddMultiFileTorrentAsync(CoreServiceFixture fixture)
    {
        var source = Path.Combine(fixture.Directory.Path, "source", "File selection sample");
        Directory.CreateDirectory(source);
        // Piece-aligned lengths make it possible to complete one file without its neighbour.
        await File.WriteAllBytesAsync(Path.Combine(source, "chapter-one.bin"), new byte[32768], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(source, "chapter-two.bin"), new byte[32768], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(source, "cover.bin"), new byte[32768], TestContext.Current.CancellationToken);
        var path = Path.Combine(fixture.Directory.Path, "selection.torrent");
        await new TorrentCreator(TorrentType.V1Only) { PieceLength = 16384 }.CreateAsync(new TorrentFileSource(source), path);
        var metadata = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser()).PrepareAsync(
            new TorrentPickedFile("selection.torrent", null, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata, startPaused: true))!;
        var destination = Path.Combine(torrent.SavePath, "File selection sample");
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(source, "chapter-one.bin"), Path.Combine(destination, "chapter-one.bin"));
        return torrent;
    }

    private sealed class PreviewService(IStorageService storage, INotificationService notifications, IBackgroundDownloadService background, TimeProvider clock)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, clock)
    {
        public byte[]? Metadata;
        public int PreviewCalls;
        public CancellationToken PreviewToken;
        public bool SuspendBeforeMetadata;
        protected override Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent, CancellationToken cancellationToken = default, bool rebuildRestart = false)
        {
            if (SuspendBeforeMetadata)
                typeof(TorrentService).GetField("_backgroundExecutionSuspended", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, true);
            return base.GetOrCreateManagerAsync(torrent, cancellationToken, rebuildRestart);
        }
        protected override async Task<ReadOnlyMemory<byte>> DownloadFileSelectionMetadataAsync(ClientEngine engine, MagnetLink magnet, CancellationToken cancellationToken)
        {
            PreviewToken = cancellationToken;
            Interlocked.Increment(ref PreviewCalls);
            if (Metadata is not null) return Metadata;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}
