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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1), _timeProvider);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalToken, timeout.Token);
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
        {
            try { return await LoadTorrentFileBoundedAsync(path, torrent); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or TorrentException)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid cached metadata for '{torrent.Name}': {ex.Message}. Fetching it again.");
            }
        }

        using var preview = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalToken);
        var session = new MetadataPreview(preview);
        lock (_engineRebuildStateGate)
        {
            if (_activeEngineRebuild is not null && !rebuildRestart) throw new MetadataPreviewInterruptedException();
            _metadataPreviews[torrent.Id] = session;
        }
        try
        {
            ThrowIfNetworkBlocked();
            if (_backgroundExecutionSuspended) throw new MetadataPreviewInterruptedException();
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
                var previousStatus = torrent.Status;
                var previousError = torrent.ErrorMessage;
                var remainsComplete = previousProgress >= 100 && requested.IsSubsetOf(previous is null ? known : previous.ToHashSet(StringComparer.Ordinal));

                resume = previousStatus is DownloadStatus.Downloading or DownloadStatus.Seeding or DownloadStatus.Queued or DownloadStatus.WaitingForWifi;
                _managers.TryGetValue(torrent.Id, out var manager);
                // Cancel monitoring before teardown can publish completion or release a queue slot.
                var wasMonitored = _downloadTokens.TryRemove(torrent.Id, out var monitor);
                if (monitor is not null)
                {
                    await monitor.CancelAsync();
                    monitor.Dispose();
                }
                try
                {
                    if (manager is not null) await StopManagerAsync(manager);
                }
                catch
                {
                    if (wasMonitored && manager is not null) ResumeFileSelectionMonitor(torrent, manager);
                    throw;
                }
                var selection = requested.SetEquals(known) ? null : requested.Order(StringComparer.Ordinal).ToArray();
                try
                {
                    if (manager is not null) await ApplyFileSelectionAsync(manager, selection);
                    await _dispatcher.InvokeAsync(() =>
                    {
                        UpdateSeedingTime(torrent, active: false);
                        torrent.Status = resume || (previousStatus == DownloadStatus.Completed && !remainsComplete)
                            ? DownloadStatus.Paused : previousStatus;
                        torrent.DownloadSpeed = 0;
                        torrent.UploadSpeed = 0;
                        torrent.EstimatedSecondsRemaining = 0;
                        torrent.SelectedFilePaths = selection;
                        torrent.TotalSize = metadata.Files.Where(file => requested.Contains(NormalizeFilePath(file.Path))).Sum(file => file.Length);
                        // Narrowing a completed target must retain its completion event and seed admission.
                        torrent.Progress = remainsComplete ? 100 : manager?.PartialProgress ?? 0;
                        torrent.DownloadedSize = (long)(torrent.TotalSize * torrent.Progress / 100);
                        torrent.DateCompleted = remainsComplete ? previousCompleted : null;
                    });
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
                        torrent.Status = previousStatus;
                        torrent.ErrorMessage = previousError;
                    });
                    if (manager is not null)
                    {
                        try
                        {
                            await ApplyFileSelectionAsync(manager, previous);
                            if (previousStatus is DownloadStatus.Downloading or DownloadStatus.Seeding)
                            {
                                ThrowIfNetworkBlocked();
                                _disposalToken.ThrowIfCancellationRequested();
                                if (_backgroundExecutionSuspended) throw new MetadataPreviewInterruptedException();
                                // The failed save may still be blocked. Restore the existing transfer
                                // directly, without saving again or competing for its own queue slot.
                                await StartManagerAsync(manager);
                            }
                            if (wasMonitored) ResumeFileSelectionMonitor(torrent, manager);
                        }
                        catch (Exception rollbackError)
                        {
                            System.Diagnostics.Debug.WriteLine($"File selection transfer rollback failed: {rollbackError}");
                            await _dispatcher.InvokeAsync(() =>
                            {
                                torrent.Status = NetworkBlocked ? DownloadStatus.WaitingForWifi
                                    : _backgroundExecutionSuspended ? DownloadStatus.Queued : DownloadStatus.Failed;
                                torrent.ErrorMessage = torrent.Status == DownloadStatus.Failed ? rollbackError.Message : null;
                            });
                        }
                    }
                    throw;
                }
                break;
            }
            finally { ExitStart(); UpdateBackgroundTransferState(); }
        }
        if (resume)
        {
            try { await StartTorrentAsync(torrent); }
            catch (Exception ex)
            {
                // The selection was saved. Start rollback already exposes the transfer failure.
                System.Diagnostics.Debug.WriteLine($"Resuming saved file selection failed: {ex}");
            }
        }
    }

    private void ResumeFileSelectionMonitor(TorrentItem torrent, TorrentManager manager)
    {
        var resumed = new CancellationTokenSource();
        _downloadTokens[torrent.Id] = resumed;
        SafeFireAndForget(MonitorTorrentAsync(torrent, manager, resumed.Token));
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
