using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Linq;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Interface for torrent management operations.
/// </summary>
public interface ITorrentService : IDisposable
{
    /// <summary>
    /// Collection of all torrent items.
    /// </summary>
    ObservableCollection<TorrentItem> Torrents { get; }

    /// <summary>
    /// Initializes the service and loads existing torrents.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Adds a new torrent from a magnet link.
    /// </summary>
    Task<TorrentItem?> AddTorrentAsync(string magnetLink);

    /// <summary>
    /// Adds a new torrent from a parsed torrent file.
    /// </summary>
    Task<TorrentItem?> AddTorrentFileAsync(TorrentMetadata metadata);

    /// <summary>
    /// Starts or resumes downloading a torrent.
    /// </summary>
    Task StartTorrentAsync(TorrentItem torrent);

    /// <summary>
    /// Pauses a downloading torrent.
    /// </summary>
    Task PauseTorrentAsync(TorrentItem torrent);

    /// <summary>
    /// Stops a torrent download.
    /// </summary>
    Task StopTorrentAsync(TorrentItem torrent);

    /// <summary>
    /// Removes a torrent from the list.
    /// </summary>
    Task RemoveTorrentAsync(TorrentItem torrent, bool deleteFiles = false);

    /// <summary>
    /// Validates if a string is a valid magnet link.
    /// </summary>
    bool IsValidMagnetLink(string link);
}

/// <summary>
/// Service for managing torrent downloads.
/// </summary>
public class TorrentService : ITorrentService
{
    private readonly IStorageService _storageService;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens = new();
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly object _torrentsLock = new();
    private readonly Timer _saveTimer;
    private ClientEngine? _engine;
    private bool _pendingSave;
    private bool _disposed;

    public ObservableCollection<TorrentItem> Torrents { get; } = [];

    public TorrentService(IStorageService storageService)
    {
        _storageService = storageService;
        // Debounced save timer - saves at most every 5 seconds
        _saveTimer = new Timer(async _ => await SaveIfPendingAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var savedTorrents = await _storageService.LoadTorrentsAsync();
        foreach (var torrent in savedTorrents)
        {
            // Reset downloading status to paused on startup
            if (torrent.Status == DownloadStatus.Downloading)
            {
                torrent.Status = DownloadStatus.Paused;
            }
            Torrents.Add(torrent);
        }
    }

    /// <inheritdoc />
    public async Task<TorrentItem?> AddTorrentAsync(string magnetLink)
    {
        if (!IsValidMagnetLink(magnetLink))
        {
            return null;
        }

        MagnetLink magnet;
        try
        {
            magnet = MagnetLink.Parse(magnetLink);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Magnet parse failed: {ex.Message}");
            return null;
        }

        var infoHash = magnet.InfoHashes.V1?.ToHex() ?? magnet.InfoHashes.V2?.ToHex() ?? string.Empty;

        if (IsDuplicate(infoHash, magnetLink))
        {
            throw new DuplicateTorrentException("This torrent is already added.");
        }

        var name = !string.IsNullOrWhiteSpace(magnet.Name)
            ? SanitizeFileName(magnet.Name)
            : SanitizeFileName(ParseTorrentName(magnetLink));

        var torrent = new TorrentItem
        {
            MagnetLink = magnetLink,
            InfoHash = infoHash,
            Name = name,
            Status = DownloadStatus.Queued,
            TotalSize = 0,
            SavePath = _storageService.GetDefaultDownloadPath()
        };

        Torrents.Add(torrent);
        await SaveAsync();

        return torrent;
    }

    /// <inheritdoc />
    public async Task<TorrentItem?> AddTorrentFileAsync(TorrentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (string.IsNullOrWhiteSpace(metadata.InfoHashHex))
        {
            return null;
        }

        var magnet = BuildMagnetLink(metadata.InfoHashHex, metadata.Name, metadata.Trackers);
        return await AddTorrentAsync(magnet);
    }

    private static string BuildMagnetLink(string infoHashHex, string? displayName, IEnumerable<string> trackers)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("magnet:?");
        sb.Append("xt=urn:btih:");
        sb.Append(infoHashHex.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            sb.Append("&dn=");
            sb.Append(Uri.EscapeDataString(displayName));
        }

        foreach (var tr in trackers.Where(static t => !string.IsNullOrWhiteSpace(t)))
        {
            sb.Append("&tr=");
            sb.Append(Uri.EscapeDataString(tr));
        }

        return sb.ToString();
    }

    private bool IsDuplicate(string infoHash, string magnetLink)
    {
        foreach (var existing in Torrents)
        {
            if (!string.IsNullOrWhiteSpace(infoHash) && infoHash.Equals(existing.InfoHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (magnetLink.Equals(existing.MagnetLink, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task StartTorrentAsync(TorrentItem torrent)
    {
        if (!torrent.CanStart)
        {
            return;
        }

        torrent.Status = DownloadStatus.Downloading;
        await SaveAsync();

        var manager = await GetOrCreateManagerAsync(torrent);

        // Cancel any existing download for this torrent
        if (_downloadTokens.TryRemove(torrent.Id, out var existingCts))
        {
            await existingCts.CancelAsync();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _downloadTokens[torrent.Id] = cts;

        // Start real download
        await manager.StartAsync();

        _ = MonitorTorrentAsync(torrent, manager, cts.Token);
    }

    /// <inheritdoc />
    public async Task PauseTorrentAsync(TorrentItem torrent)
    {
        if (!torrent.CanPause)
        {
            return;
        }

        // Cancel the download
        if (_downloadTokens.TryRemove(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            await manager.PauseAsync();
        }

        torrent.Status = DownloadStatus.Paused;
        torrent.DownloadSpeed = 0;
        torrent.UploadSpeed = 0;
        await SaveAsync();
    }

    /// <inheritdoc />
    public async Task StopTorrentAsync(TorrentItem torrent)
    {
        if (!torrent.CanStop)
        {
            return;
        }

        // Cancel the download
        if (_downloadTokens.TryRemove(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            await manager.StopAsync();
        }

        torrent.Status = DownloadStatus.Stopped;
        torrent.DownloadSpeed = 0;
        torrent.UploadSpeed = 0;
        torrent.Progress = 0;
        torrent.DownloadedSize = 0;
        await SaveAsync();
    }

    /// <inheritdoc />
    public async Task RemoveTorrentAsync(TorrentItem torrent, bool deleteFiles = false)
    {
        // Cancel any active download
        if (_downloadTokens.TryRemove(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_managers.TryRemove(torrent.Id, out var manager))
        {
            try
            {
                await manager.StopAsync();
            }
            catch
            {
                // ignore stop errors on remove
            }
        }

        Torrents.Remove(torrent);
        await SaveAsync();

        // Optionally delete downloaded files
        if (deleteFiles && !string.IsNullOrEmpty(torrent.SavePath) && !string.IsNullOrEmpty(torrent.Name))
        {
            try
            {
                // Sanitize the name again to ensure safe file path
                var safeName = SanitizeFileName(torrent.Name);
                var filePath = Path.Combine(torrent.SavePath, safeName);
                
                // Verify the path is within the expected directory (prevent path traversal)
                var fullPath = Path.GetFullPath(filePath);
                var basePath = Path.GetFullPath(torrent.SavePath);
                if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine("Attempted path traversal detected, skipping file deletion");
                    return;
                }
                
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting files: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public bool IsValidMagnetLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        try
        {
            _ = MagnetLink.Parse(link);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sanitizes a file name by removing or replacing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "unnamed_torrent";
        }

        // Remove path separators and other dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Where(c => !invalidChars.Contains(c)));
        
        // Also remove directory traversal patterns
        sanitized = sanitized.Replace("..", "");
        
        // Ensure we have a valid name
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "unnamed_torrent";
        }

        // Limit length
        if (sanitized.Length > 200)
        {
            sanitized = sanitized[..200];
        }

        return sanitized.Trim();
    }

    private static string ParseTorrentName(string magnetLink)
    {
        // Try to extract name from magnet link
        var dnMatch = Regex.Match(
            magnetLink,
            @"dn=([^&]+)",
            RegexOptions.IgnoreCase);

        if (dnMatch.Success)
        {
            return Uri.UnescapeDataString(dnMatch.Groups[1].Value);
        }

        // Fallback to a hash-based name
        var hashMatch = Regex.Match(
            magnetLink,
            @"btih:([a-fA-F0-9]{40})",
            RegexOptions.IgnoreCase);

        if (hashMatch.Success)
        {
            return $"Torrent_{hashMatch.Groups[1].Value[..8]}";
        }

        return $"Torrent_{DateTime.Now:yyyyMMddHHmmss}";
    }

    private async Task MonitorTorrentAsync(TorrentItem torrent, TorrentManager manager, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);

                var metadataSize = manager.Torrent?.Size;
                var progress = manager.Progress;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (metadataSize.HasValue && metadataSize.Value > 0)
                    {
                        torrent.TotalSize = metadataSize.Value;
                    }

                    var downloadedBytes = manager.Monitor.DataBytesReceived;
                    if (downloadedBytes > 0)
                    {
                        torrent.DownloadedSize = downloadedBytes;
                        if (torrent.TotalSize > 0)
                        {
                            progress = (double)downloadedBytes / torrent.TotalSize * 100;
                        }
                    }

                    torrent.Progress = progress;
                    torrent.DownloadSpeed = manager.Monitor.DownloadRate;
                    torrent.UploadSpeed = manager.Monitor.UploadRate;
                    var peers = manager.Peers;
                    var seedsProp = peers.GetType().GetProperty("Seeds") ?? peers.GetType().GetProperty("Seeding");
                    var leechProp = peers.GetType().GetProperty("Leeches") ?? peers.GetType().GetProperty("Leeching");
                    var connectedProp = peers.GetType().GetProperty("ConnectedPeers")
                                        ?? peers.GetType().GetProperty("ActivePeers")
                                        ?? peers.GetType().GetProperty("AvailablePeers");

                    var seeds = seedsProp?.GetValue(peers) as int? ?? 0;
                    var leeches = leechProp?.GetValue(peers) as int? ?? 0;

                    // Fallback: infer from connected peers collection if direct counts unavailable
                    if ((seeds == 0 || leeches == 0) && connectedProp?.GetValue(peers) is System.Collections.IEnumerable peerList)
                    {
                        int seedCount = 0;
                        int leechCount = 0;
                        foreach (var peer in peerList)
                        {
                            var isSeederProp = peer?.GetType().GetProperty("IsSeeder") ?? peer?.GetType().GetProperty("AmSeeder");
                            var isSeeder = (bool?)(isSeederProp?.GetValue(peer)) == true;
                            if (isSeeder)
                            {
                                seedCount++;
                            }
                            else
                            {
                                leechCount++;
                            }
                        }

                        if (seedCount > 0 || leechCount > 0)
                        {
                            seeds = seedCount;
                            leeches = leechCount;
                        }
                    }

                    torrent.Seeders = seeds;
                    torrent.Leechers = leeches;

                    if (torrent.DownloadSpeed > 0 && torrent.TotalSize > 0)
                    {
                        var remainingBytes = Math.Max(0, torrent.TotalSize - torrent.DownloadedSize);
                        torrent.EstimatedSecondsRemaining = remainingBytes / Math.Max(1, torrent.DownloadSpeed);
                    }
                    else
                    {
                        torrent.EstimatedSecondsRemaining = 0;
                    }

                    // Map state
                    torrent.Status = manager.State switch
                    {
                        TorrentState.Paused => DownloadStatus.Paused,
                        TorrentState.Seeding => DownloadStatus.Completed,
                        TorrentState.Stopped when progress >= 100 => DownloadStatus.Completed,
                        TorrentState.Stopped => DownloadStatus.Stopped,
                        TorrentState.Downloading => DownloadStatus.Downloading,
                        _ => torrent.Status
                    };

                    if (torrent.Status == DownloadStatus.Completed)
                    {
                        torrent.DateCompleted = DateTime.Now;
                    }
                });

                if (manager.HasMetadata && manager.Torrent != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        torrent.TotalSize = manager.Torrent.Size;
                        torrent.Name = manager.Torrent.Name;
                    });
                }

                _pendingSave = true;

                if (manager.State == TorrentState.Stopped && progress >= 100)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop/pause
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Monitor error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() => torrent.Status = DownloadStatus.Failed);
        }
        finally
        {
            _downloadTokens.TryRemove(torrent.Id, out _);
            _pendingSave = true;
        }
    }

    private async Task SaveIfPendingAsync()
    {
        if (_pendingSave)
        {
            _pendingSave = false;
            await SaveAsync();
        }
    }

    private Task SaveAsync()
    {
        List<TorrentItem> snapshot;
        lock (_torrentsLock)
        {
            snapshot = [.. Torrents];
        }
        return _storageService.SaveTorrentsAsync(snapshot);
    }

    private async Task<ClientEngine> EnsureEngineAsync()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        var engineSettings = new EngineSettingsBuilder
        {
            CacheDirectory = _storageService.GetDefaultDownloadPath(),
            // Enable UPnP/NAT-PMP port forwarding so peers can connect to us
            AllowPortForwarding = true,
            // Enable DHT for peer discovery (critical for finding more peers)
            DhtEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0),
            // Enable Local Peer Discovery to find peers on the same network
            AllowLocalPeerDiscovery = true,
            // Increase maximum connections for better download speeds
            MaximumConnections = 200,
            MaximumHalfOpenConnections = 50,
            // Set a listen endpoint to accept incoming connections
            ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>
            {
                { "ipv4", new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0) }
            }
        }.ToSettings();

        _engine = new ClientEngine(engineSettings);

        return _engine;
    }


    private async Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent)
    {
        if (_managers.TryGetValue(torrent.Id, out var existing))
        {
            return existing;
        }

        var engine = await EnsureEngineAsync();
        var downloadPath = string.IsNullOrWhiteSpace(torrent.SavePath) ? _storageService.GetDefaultDownloadPath() : torrent.SavePath;
        var magnet = MagnetLink.Parse(torrent.MagnetLink);

        var torrentSettings = new TorrentSettingsBuilder
        {
            // Increase maximum connections per torrent
            MaximumConnections = 100,
            // Limit upload slots to prioritize downloads
            UploadSlots = 4,
        }.ToSettings();

        var manager = await engine.AddAsync(magnet, downloadPath, torrentSettings);

        _managers[torrent.Id] = manager;
        return manager;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Dispose();

        // Cancel and dispose all active download tokens
        // Note: Using synchronous Cancel() here as Dispose should be synchronous
        foreach (var kvp in _downloadTokens)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        _downloadTokens.Clear();

        GC.SuppressFinalize(this);
    }
}
