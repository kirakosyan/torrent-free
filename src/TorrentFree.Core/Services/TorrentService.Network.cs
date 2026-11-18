using TorrentFree.Models;

namespace TorrentFree.Services;

public partial class TorrentService
{
    private readonly ITransferNetworkMonitor? _networkMonitor;
    private readonly SemaphoreSlim _networkPolicyLock = new(1, 1);
    private readonly SemaphoreSlim _networkResumeLock = new(1, 1);
    private volatile bool _wifiOnly;
    private volatile bool _networkPolicyReady;
    private volatile bool _networkSuspending;
    private long _networkSettingsRevision;

    private bool NetworkBlocked => _networkSuspending || (_wifiOnly && _networkMonitor?.IsWifiConnected != true);

    public async Task UpdateWifiOnlyAsync(bool enabled)
    {
        var revision = Interlocked.Increment(ref _networkSettingsRevision);
        await InitializeAsync();
        if (revision != Interlocked.Read(ref _networkSettingsRevision) || _disposed) return;
        _wifiOnly = enabled;
        await ReconcileNetworkPolicyAsync();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_networkPolicyReady && !_disposed)
            SafeFireAndForget(ReconcileNetworkPolicyAsync());
    }

    private async Task ReconcileNetworkPolicyAsync(bool resumeInBackground = false)
    {
        if (NetworkBlocked)
        {
            lock (_engineRebuildStateGate)
                foreach (var preview in _metadataPreviews.Values) preview.Interrupt();
        }
        await _networkPolicyLock.WaitAsync(_disposalToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            if (NetworkBlocked)
            {
                // Pausing a manager alone leaves the engine's discovery sockets alive.
                // Use the existing rebuild barrier to drain starts and close the engine,
                // preserving manual Pause/Stop/Remove decisions made during teardown.
                await _engineRebuildLock.WaitAsync(_disposalToken).ConfigureAwait(false);
                try
                {
                    _networkSuspending = true;
                    await RebuildEngineAsync().ConfigureAwait(false);
                }
                finally
                {
                    _networkSuspending = false;
                    try { _engineRebuildLock.Release(); }
                    catch (ObjectDisposedException) when (_disposed) { }
                }

                foreach (var torrent in GetNetworkPolicySnapshot())
                {
                    await using var operation = await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken);
                    if (NetworkBlocked && torrent.Status == DownloadStatus.Queued && IsTracked(torrent))
                        await MarkWaitingForWifiAsync(torrent);
                }
                await SaveAsync();
            }
        }
        finally
        {
            _networkPolicyLock.Release();
        }

        if (NetworkBlocked) return;
        if (resumeInBackground) SafeFireAndForget(ResumeWifiTransfersAsync());
        else await ResumeWifiTransfersAsync();
    }

    private async Task ResumeWifiTransfersAsync()
    {
        // Repeated reconnects can share the completed result; a loss never waits for this lock.
        await _networkResumeLock.WaitAsync(_disposalToken).ConfigureAwait(false);
        try
        {
            if (NetworkBlocked || _disposed) return;
            var waiting = GetNetworkPolicySnapshot().Where(t => t.Status == DownloadStatus.WaitingForWifi);
            await Task.WhenAll(waiting.Select(ResumeWaitingTorrentAsync));
        }
        finally { _networkResumeLock.Release(); }
    }

    private async Task ResumeWaitingTorrentAsync(TorrentItem torrent)
    {
        // Admission checks capacity and manual control under the torrent lock. A transfer
        // searching for metadata must not prevent admission of the next available slot.
        try { await StartTorrentIfStatusAsync(torrent, DownloadStatus.WaitingForWifi); }
        catch (Exception ex) when (!_disposed)
        {
            System.Diagnostics.Debug.WriteLine($"Wi-Fi resume failed for '{torrent.Name}': {ex.Message}");
        }
    }

    private List<TorrentItem> GetNetworkPolicySnapshot()
    {
        lock (_torrentsLock)
            return Torrents.OrderBy(t => t.DateAdded).ToList();
    }

    private bool IsTracked(TorrentItem torrent) =>
        TryGetTorrentById(torrent.Id, out var tracked) && ReferenceEquals(torrent, tracked);

    private Task MarkWaitingForWifiAsync(TorrentItem torrent) => _dispatcher.InvokeAsync(() =>
    {
        UpdateSeedingTime(torrent, active: false);
        torrent.Status = DownloadStatus.WaitingForWifi;
        torrent.DownloadSpeed = 0;
        torrent.UploadSpeed = 0;
        torrent.EstimatedSecondsRemaining = 0;
        torrent.ErrorMessage = null;
    });

    private void ThrowIfNetworkBlocked()
    {
        if (NetworkBlocked) throw new WifiUnavailableException();
    }

    private sealed class WifiUnavailableException : Exception;
}
