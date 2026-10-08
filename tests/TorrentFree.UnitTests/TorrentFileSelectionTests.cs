using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentFileSelectionTests
{
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
        protected override async Task<ReadOnlyMemory<byte>> DownloadFileSelectionMetadataAsync(ClientEngine engine, MagnetLink magnet, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PreviewCalls);
            if (Metadata is not null) return Metadata;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}
