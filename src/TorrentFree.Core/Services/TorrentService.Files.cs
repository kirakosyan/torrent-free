using System.Collections.Concurrent;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;

namespace TorrentFree.Services;

public partial class TorrentService
{
    // Access to registration/disposal is serialized with the engine rebuild barrier.
    private readonly ConcurrentDictionary<string, MetadataPreview> _metadataPreviews = new();

    private sealed class MetadataPreview(CancellationTokenSource cancellation)
    {
        public bool Interrupted { get; private set; }
        public void Interrupt() { Interrupted = true; cancellation.Cancel(); }
    }

    private sealed class MetadataPreviewInterruptedException : OperationCanceledException;

    public async Task<IReadOnlyList<TorrentFileChoice>> GetTorrentFilesAsync(
        TorrentItem torrent, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalToken);
        while (true)
        {
            await WaitForEngineRebuildAsync().WaitAsync(lifetime.Token);
            await using var operation = await _torrentOperationLock.AcquireAsync(torrent.Id, lifetime.Token);
            if (!IsTracked(torrent)) throw new InvalidOperationException("This torrent has been removed.");
            if (!TryEnterStart(out _)) continue;
            try
            {
                var metadata = await LoadFileSelectionMetadataAsync(torrent, lifetime.Token);
                var selected = torrent.SelectedFilePaths?.ToHashSet(StringComparer.Ordinal);
                await _dispatcher.InvokeAsync(() =>
                {
                    torrent.Name = metadata.Name;
                    torrent.TotalSize = metadata.Files.Where(file => selected is null || selected.Contains(NormalizeFilePath(file.Path))).Sum(file => file.Length);
                });
                return metadata.Files.Select(file => new TorrentFileChoice(
                    NormalizeFilePath(file.Path), file.Length,
                    selected is null || selected.Contains(NormalizeFilePath(file.Path)))).ToArray();
            }
            finally { ExitStart(); }
        }
    }

    private async Task<Torrent> LoadFileSelectionMetadataAsync(TorrentItem torrent, CancellationToken cancellationToken, bool rebuildRestart = false)
    {
        if (_managers.TryGetValue(torrent.Id, out var current) && current.Torrent is { } existing)
            return existing;
        var path = GetMetadataPath(torrent) ?? FindEngineMetadataCachePath(torrent);
        if (path is not null)
            return await LoadTorrentFileBoundedAsync(path, torrent);

        using var preview = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalToken);
        preview.CancelAfter(TimeSpan.FromMinutes(1));
        var session = new MetadataPreview(preview);
        lock (_engineRebuildStateGate)
        {
            if (_activeEngineRebuild is not null && !rebuildRestart) throw new MetadataPreviewInterruptedException();
            _metadataPreviews[torrent.Id] = session;
        }
        try
        {
            ThrowIfNetworkBlocked();
            if (_backgroundExecutionSuspended) throw new OperationCanceledException();
            // An older active magnet may already be fetching metadata. Do not register
            // another manager with the same hash in that case.
            if (current is not null)
            {
                if (current.State is TorrentState.Stopped or TorrentState.Paused or TorrentState.Error)
                {
                    var engine = await EnsureEngineAsync();
                    await StopManagerAsync(current);
                    await engine.RemoveAsync(current, RemoveMode.KeepAllData);
                    _managers.TryRemove(torrent.Id, out _);
                }
                else
                {
                    await current.WaitForMetadataAsync(preview.Token);
                    return current.Torrent!;
                }
            }

            var magnet = MagnetLink.Parse(torrent.MagnetLink);
            if (!ProxyRequested && MagnetTrackerBootstrapRules.ShouldAddPublicTrackers(torrent.MagnetLink))
                magnet = new MagnetLink(magnet.InfoHashes, magnet.Name, PublicTrackers.Select(uri => uri.AbsoluteUri).ToArray());

            var metadataEngine = await EnsureEngineAsync();
            ThrowIfNetworkBlocked();
            preview.Token.ThrowIfCancellationRequested();
            var content = await DownloadFileSelectionMetadataAsync(metadataEngine, magnet, preview.Token);
            _ = new TorrentFileParser().Parse(content.ToArray());
            var metadata = await Torrent.LoadAsync(content.ToArray().AsMemory());
            if (!TorrentIdentityMatches(torrent, metadata.InfoHashes))
                throw new InvalidDataException("The metadata does not match this torrent.");

            var cachePath = TorrentImportService.GetCachePath(_storageService.GetAppDataPath(), torrent.InfoHash);
            await using (await TorrentImportService.LockCacheAsync(cachePath, preview.Token))
                await TorrentImportService.WriteCacheAsync(cachePath, content.ToArray(), preview.Token);
            await _dispatcher.InvokeAsync(() => torrent.CachedTorrentFilePath = cachePath);
            await SaveAsync();
            return metadata;
        }
        catch (OperationCanceledException) when (session.Interrupted)
        {
            throw new MetadataPreviewInterruptedException();
        }
        finally
        {
            lock (_engineRebuildStateGate)
                _metadataPreviews.TryRemove(torrent.Id, out _);
        }
    }

    protected virtual Task<ReadOnlyMemory<byte>> DownloadFileSelectionMetadataAsync(
        ClientEngine engine, MagnetLink magnet, CancellationToken cancellationToken)
        => engine.DownloadMetadataAsync(magnet, cancellationToken);

    public async Task SetTorrentFileSelectionAsync(TorrentItem torrent, IReadOnlyCollection<string> selectedPaths)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        var requested = selectedPaths.Select(NormalizeFilePath).ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0) throw new ArgumentException("Choose at least one file.", nameof(selectedPaths));
        var resume = false;
        while (true)
        {
            await WaitForEngineRebuildAsync();
            await using var operation = await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken);
            if (!IsTracked(torrent)) throw new InvalidOperationException("This torrent has been removed.");
            if (!TryEnterStart(out _)) continue;
            try
            {
                var metadata = await LoadFileSelectionMetadataAsync(torrent, _disposalToken);
                var known = metadata.Files.Select(file => NormalizeFilePath(file.Path)).ToHashSet(StringComparer.Ordinal);
                if (!requested.IsSubsetOf(known)) throw new ArgumentException("The selection contains unknown files.", nameof(selectedPaths));
                var previous = torrent.SelectedFilePaths;
                if (requested.SetEquals(previous is null ? known : previous)) return;

                var previousTotal = torrent.TotalSize;
                var previousDownloaded = torrent.DownloadedSize;
                var previousProgress = torrent.Progress;
                var previousCompleted = torrent.DateCompleted;

                resume = torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding or DownloadStatus.Queued or DownloadStatus.WaitingForWifi;
                _managers.TryGetValue(torrent.Id, out var manager);
                // Stop first. A failed stop leaves the old selection and monitor intact.
                if (manager is not null) await StopManagerAsync(manager);
                if (_downloadTokens.TryRemove(torrent.Id, out var monitor))
                {
                    await monitor.CancelAsync();
                    monitor.Dispose();
                }
                var selection = requested.SetEquals(known) ? null : requested.Order(StringComparer.Ordinal).ToArray();
                await _dispatcher.InvokeAsync(() =>
                {
                    torrent.Status = DownloadStatus.Paused;
                    torrent.DownloadSpeed = 0;
                    torrent.UploadSpeed = 0;
                    torrent.EstimatedSecondsRemaining = 0;
                    torrent.SelectedFilePaths = selection;
                    torrent.TotalSize = metadata.Files.Where(file => requested.Contains(NormalizeFilePath(file.Path))).Sum(file => file.Length);
                    // A changed target must be rechecked before exposing completion or seed admission.
                    torrent.Progress = 0;
                    torrent.DownloadedSize = 0;
                    torrent.DateCompleted = null;
                });
                try
                {
                    if (manager is not null) await ApplyFileSelectionAsync(manager, selection);
                    await SaveAsync();
                }
                catch
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        torrent.SelectedFilePaths = previous;
                        torrent.TotalSize = previousTotal;
                        torrent.DownloadedSize = previousDownloaded;
                        torrent.Progress = previousProgress;
                        torrent.DateCompleted = previousCompleted;
                    });
                    if (manager is not null) await ApplyFileSelectionAsync(manager, previous);
                    throw;
                }
                break;
            }
            finally { ExitStart(); UpdateBackgroundTransferState(); }
        }
        if (resume) await StartTorrentAsync(torrent);
    }

    private static string NormalizeFilePath(string path) => path.Replace('\\', '/');

    private static long GetSelectedSize(TorrentManager manager) =>
        manager.Files.Where(file => file.Priority != Priority.DoNotDownload).Sum(file => file.Length);

    private static async Task ApplyFileSelectionAsync(TorrentManager manager, string[]? selectedPaths)
    {
        if (!manager.HasMetadata) return;
        var selected = selectedPaths?.ToHashSet(StringComparer.Ordinal);
        if (selected is not null && (selected.Count == 0 || !selected.IsSubsetOf(
            manager.Files.Select(file => NormalizeFilePath(file.Path)).ToHashSet(StringComparer.Ordinal))))
            throw new InvalidDataException("The saved file selection does not match this torrent.");
        foreach (var file in manager.Files)
        {
            var priority = selected is null || selected.Contains(NormalizeFilePath(file.Path))
                ? Priority.Normal : Priority.DoNotDownload;
            if (file.Priority != priority) await manager.SetFilePriorityAsync(file, priority);
        }
    }
}
