using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Linq;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Interface for torrent management operations.
/// </summary>
public interface ITorrentService : IDisposable, IAsyncDisposable
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
    Task RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false);

    /// <summary>
    /// Validates if a string is a valid magnet link.
    /// </summary>
    bool IsValidMagnetLink(string link);

    /// <summary>
    /// Update global speed limits (KB/s). 0 = unlimited.
    /// </summary>
    void UpdateGlobalSpeedLimits(int downloadLimitKbps, int uploadLimitKbps);

    /// <summary>
    /// Update queue limits. 0 = unlimited.
    /// </summary>
    void UpdateQueueLimits(int maxActiveDownloads, int maxActiveSeeds);

    /// <summary>
    /// Update global seeding limits. 0 = unlimited.
    /// </summary>
    void UpdateSeedingLimits(double maxSeedRatio, int maxSeedMinutes);

    /// <summary>
    /// Update SOCKS5 proxy settings. Takes effect on the next engine creation.
    /// </summary>
    void UpdateProxySettings(bool enabled, string host, int port, string username, string password);
}

/// <summary>
/// Service for managing torrent downloads.
/// </summary>
public class TorrentService : ITorrentService
{
    private static readonly TimeSpan ManagerStopTimeout = TimeSpan.FromSeconds(2);
    private static readonly Uri[] PublicTrackers =
    [
        new("udp://tracker.opentrackr.org:1337/announce"),
        new("udp://open.tracker.cl:1337/announce"),
        new("udp://open.demonii.com:1337/announce"),
        new("udp://open.stealth.si:80/announce"),
        new("udp://tracker.torrent.eu.org:451/announce"),
        new("udp://exodus.desync.com:6969/announce"),
        new("udp://tracker.tiny-vps.com:6969/announce"),
        new("udp://tracker.moeking.me:6969/announce"),
        new("udp://explodie.org:6969/announce"),
        new("udp://tracker.openbittorrent.com:6969/announce"),
    ];

    private readonly IStorageService _storageService;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundDownloadService _backgroundDownloadService;
    private readonly AsyncKeyedLocker _torrentOperationLock = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens = new();
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly object _torrentsLock = new();
    private readonly Timer _saveTimer;
    // Read on several threads outside _engineLock (fast-path checks); volatile guarantees
    // each reader sees the latest write (e.g. after RebuildEngineAsync nulls it).
    private volatile ClientEngine? _engine;
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private Task? _initTask;
    private readonly object _initGate = new();
    private volatile bool _pendingSave;
    private bool _disposed;
    private volatile bool _backgroundTransferActive;

    private int _maxActiveDownloads = 2;
    private int _maxActiveSeeds = 2;
    private long _globalDownloadLimitBytesPerSec;
    private long _globalUploadLimitBytesPerSec;
    private double _globalMaxSeedRatio;
    private int _globalMaxSeedMinutes;

    private bool _proxyEnabled;
    private string _proxyHost = string.Empty;
    private int _proxyPort = 1080;
    private string _proxyUsername = string.Empty;
    private string _proxyPassword = string.Empty;

    // Proxy changes (e.g. typing a hostname) arrive one keystroke at a time; debounce the
    // expensive engine rebuild and serialize rebuilds so they cannot overlap.
    private readonly SemaphoreSlim _engineRebuildLock = new(1, 1);
    private readonly object _proxyRebuildGate = new();
    private CancellationTokenSource? _proxyRebuildCts;
    private static readonly TimeSpan ProxyRebuildDebounce = TimeSpan.FromMilliseconds(800);

    // A SOCKS5 proxy can only tunnel outbound TCP, so when it is active we disable every
    // channel that would otherwise expose the user's real IP (DHT, LPD, UPnP, the inbound
    // listener, and UDP trackers). Centralised here so engine creation and tracker
    // bootstrap stay in agreement.
    private bool ProxyActive => _proxyEnabled && !string.IsNullOrWhiteSpace(_proxyHost);

    public ObservableCollection<TorrentItem> Torrents { get; } = [];

    public TorrentService(IStorageService storageService, INotificationService notificationService, IBackgroundDownloadService backgroundDownloadService)
    {
        _storageService = storageService;
        _notificationService = notificationService;
        _backgroundDownloadService = backgroundDownloadService;
        // Debounced save timer - saves at most every 5 seconds
        _saveTimer = new Timer(async _ => await SaveIfPendingAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        lock (_initGate)
        {
            _initTask ??= InitializeCoreAsync();
            return _initTask;
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var savedTorrents = await _storageService.LoadTorrentsAsync();
            var hadStateChanges = false;

            foreach (var torrent in savedTorrents)
            {
                var hadMissingTorrentFile = TorrentRestoreRules.HasMissingTorrentFile(torrent.TorrentFilePath);
                var restoreDecision = TorrentRestoreRules.Evaluate(
                    new TorrentIdentity(torrent.Id, torrent.InfoHash, torrent.MagnetLink),
                    torrent.TorrentFilePath,
                    GetTorrentIdentitySnapshot(),
                    IsValidMagnetLink);
                hadStateChanges |= restoreDecision.ShouldPersistChanges;

                if (!restoreDecision.ShouldAdd)
                {
                    if (hadMissingTorrentFile)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping torrent '{torrent.Name}' because its .torrent file is missing and it will not be restored (e.g., duplicate entry or invalid magnet link).");
                    }

                    continue;
                }

                if (restoreDecision.ClearTorrentFileMetadata)
                {
                    torrent.TorrentFilePath = null;
                    torrent.TorrentFileName = null;
                }

                if (hadMissingTorrentFile)
                {
                    System.Diagnostics.Debug.WriteLine($"Recovered torrent '{torrent.Name}' using its stored magnet link after the original .torrent file went missing.");
                }

                // Reset downloading status to paused on startup
                if (torrent.Status == DownloadStatus.Downloading)
                {
                    torrent.Status = DownloadStatus.Paused;
                }

                AttachTorrentSettingsHandlers(torrent);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    lock (_torrentsLock)
                    {
                        Torrents.Add(torrent);
                    }
                });
            }

            // Persist the cleaned-up list so stale or duplicate entries are not reloaded next time.
            if (hadStateChanges)
            {
                await SaveAsync();
            }
        }
        catch
        {
            // Allow a future caller to retry initialization.
            lock (_initGate)
            {
                _initTask = null;
            }
            throw;
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

        var settings = await _storageService.LoadSettingsAsync();
        var fallbackDownloadPath = _storageService.GetDefaultDownloadPath();

        var torrent = new TorrentItem
        {
            MagnetLink = magnetLink,
            InfoHash = infoHash,
            Name = name,
            Status = DownloadStatus.Queued,
            TotalSize = 0,
            SavePath = DownloadLocationResolver.ResolveSavePath(settings, sourceTorrentFilePath: null, fallbackDownloadPath)
        };

        AttachTorrentSettingsHandlers(torrent);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            lock (_torrentsLock)
            {
                Torrents.Add(torrent);
            }
        });
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

        var normalizedHash = infoHashHex.ToLowerInvariant();
        if (normalizedHash.Length == 64)
        {
            // BitTorrent v2 info-hash: SHA-256 multihash (0x12 = sha2-256, 0x20 = 32 bytes).
            sb.Append("xt=urn:btmh:1220");
            sb.Append(normalizedHash);
        }
        else
        {
            sb.Append("xt=urn:btih:");
            sb.Append(normalizedHash);
        }

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
        lock (_torrentsLock)
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
    }

    /// <inheritdoc />
    public async Task StartTorrentAsync(TorrentItem torrent)
    {
        await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id);

        if (!torrent.CanStart)
        {
            return;
        }

        if (!CanStartAnotherDownload())
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                torrent.Status = DownloadStatus.Queued;
            });
            await SaveAsync();
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            torrent.Status = DownloadStatus.Downloading;
            torrent.ErrorMessage = null;
        });
        await SaveAsync();
        UpdateBackgroundTransferState();

        TorrentManager? manager = null;
        try
        {
            manager = await GetOrCreateManagerAsync(torrent);
            await ApplySpeedLimitsToManagerAsync(manager, torrent);

            // Cancel any existing download for this torrent
            if (_downloadTokens.TryRemove(torrent.Id, out var existingCts))
            {
                await existingCts.CancelAsync();
                existingCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _downloadTokens[torrent.Id] = cts;

            // Start real download
            await StartManagerAsync(manager);

            _ = MonitorTorrentAsync(torrent, manager, cts.Token);
        }
        catch (Exception ex)
        {
            if (_downloadTokens.TryRemove(torrent.Id, out var failedCts))
            {
                failedCts.Dispose();
            }

            if (manager is not null && _managers.TryGetValue(torrent.Id, out var activeManager) && ReferenceEquals(manager, activeManager))
            {
                try
                {
                    await StopManagerAsync(manager);

                    if (_engine is not null)
                    {
                        await _engine.RemoveAsync(manager);
                    }
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Start rollback cleanup error for '{torrent.Name}' ({torrent.Id}): {cleanupEx}");
                }
                finally
                {
                    _managers.TryRemove(torrent.Id, out _);
                }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                torrent.Status = DownloadStatus.Failed;
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
                torrent.ErrorMessage = ex.Message;
            });

            await SaveAsync();
            UpdateBackgroundTransferState();

            // The download slot this torrent was occupying is now free — let queued
            // torrents take it instead of waiting for the next user action.
            await TryStartQueuedTorrentsAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PauseTorrentAsync(TorrentItem torrent)
    {
        await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id);

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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
        });

        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            await manager.PauseAsync();
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            torrent.Status = DownloadStatus.Paused;
        });

        await SaveAsync();
        UpdateBackgroundTransferState();

        await TryStartQueuedTorrentsAsync();
    }

    /// <inheritdoc />
    public async Task StopTorrentAsync(TorrentItem torrent)
    {
        await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id);

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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
        });

        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            await StopManagerAsync(manager);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            torrent.Status = DownloadStatus.Stopped;
        });

        await SaveAsync();
        UpdateBackgroundTransferState();

        await TryStartQueuedTorrentsAsync();
    }

    /// <inheritdoc />
    public async Task RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id);

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
                await StopManagerAsync(manager);

                if (_engine is not null)
                {
                    await _engine.RemoveAsync(manager);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Remove manager cleanup error for '{torrent.Name}' ({torrent.Id}): {ex}");
            }
        }

        DetachTorrentSettingsHandlers(torrent);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            lock (_torrentsLock)
            {
                Torrents.Remove(torrent);
            }
        });
        await SaveAsync();
        UpdateBackgroundTransferState();

        if (deleteTorrentFile)
        {
            TryDeleteTorrentFile(torrent);
        }

        await TryStartQueuedTorrentsAsync();

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
                if (!PathGuard.IsPathWithinDirectory(fullPath, basePath))
                {
                    System.Diagnostics.Debug.WriteLine("Attempted path traversal detected, skipping file deletion");
                    return;
                }

                var torrentFilePath = string.IsNullOrWhiteSpace(torrent.TorrentFilePath)
                    ? null
                    : Path.GetFullPath(torrent.TorrentFilePath);

                if (File.Exists(fullPath))
                {
                    if (!deleteTorrentFile && (string.Equals(fullPath, torrentFilePath, StringComparison.OrdinalIgnoreCase)
                        || fullPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }

                    File.Delete(fullPath);
                }
                else if (Directory.Exists(fullPath))
                {
                    if (!deleteTorrentFile)
                    {
                        DeleteDirectoryPreserveTorrentFiles(fullPath);
                    }
                    else
                    {
                        Directory.Delete(fullPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting files: {ex.Message}");
            }
        }
    }

    private static void DeleteDirectoryPreserveTorrentFiles(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath))
        {
            if (file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(directoryPath))
        {
            DeleteDirectoryPreserveTorrentFiles(dir);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }

    private static void TryDeleteTorrentFile(TorrentItem torrent)
    {
        try
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(torrent.TorrentFilePath))
            {
                candidates.Add(torrent.TorrentFilePath);
            }

            if (!string.IsNullOrWhiteSpace(torrent.TorrentFileName) && !string.IsNullOrWhiteSpace(torrent.SavePath))
            {
                candidates.Add(Path.Combine(torrent.SavePath, torrent.TorrentFileName));
            }

            foreach (var candidate in candidates)
            {
                var path = candidate.Trim();

                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    path = uri.LocalPath;
                }

                var fullPath = Path.GetFullPath(path);
                if (!fullPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var attributes = File.GetAttributes(fullPath);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(fullPath);
                break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting .torrent file: {ex.Message}");
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

    /// <inheritdoc />
    public void UpdateGlobalSpeedLimits(int downloadLimitKbps, int uploadLimitKbps)
    {
        _globalDownloadLimitBytesPerSec = KbpsToBytes(downloadLimitKbps);
        _globalUploadLimitBytesPerSec = KbpsToBytes(uploadLimitKbps);

        SafeFireAndForget(ApplyGlobalSpeedLimitsAsync());
    }

    private async Task ApplyGlobalSpeedLimitsAsync()
    {
        var engine = _engine;
        if (engine is not null)
        {
            await ApplySpeedLimitsToEngineAsync(engine, _globalDownloadLimitBytesPerSec, _globalUploadLimitBytesPerSec);
        }

        foreach (var kvp in _managers)
        {
            if (TryGetTorrentById(kvp.Key, out var torrent) && torrent is not null)
            {
                await ApplySpeedLimitsToManagerAsync(kvp.Value, torrent);
            }
        }
    }

    /// <inheritdoc />
    public void UpdateQueueLimits(int maxActiveDownloads, int maxActiveSeeds)
    {
        _maxActiveDownloads = Math.Max(0, maxActiveDownloads);
        _maxActiveSeeds = Math.Max(0, maxActiveSeeds);

        SafeFireAndForget(TryStartQueuedTorrentsAsync());

        if (_maxActiveSeeds > 0)
        {
            foreach (var kvp in _managers)
            {
                if (TryGetTorrentById(kvp.Key, out var torrent) && torrent is not null && torrent.Status == DownloadStatus.Seeding)
                {
                    SafeFireAndForget(EnforceSeedingLimitsAsync(torrent, kvp.Value));
                }
            }
        }
    }

    /// <inheritdoc />
    public void UpdateSeedingLimits(double maxSeedRatio, int maxSeedMinutes)
    {
        _globalMaxSeedRatio = Math.Max(0, maxSeedRatio);
        _globalMaxSeedMinutes = Math.Max(0, maxSeedMinutes);

        foreach (var kvp in _managers)
        {
            if (TryGetTorrentById(kvp.Key, out var torrent) && torrent is not null && torrent.Status == DownloadStatus.Seeding)
            {
                SafeFireAndForget(EnforceSeedingLimitsAsync(torrent, kvp.Value));
            }
        }
    }

    /// <inheritdoc />
    public void UpdateProxySettings(bool enabled, string host, int port, string username, string password)
    {
        var newHost = host ?? string.Empty;
        var newPort = port is > 0 and <= 65535 ? port : 1080;
        var newUsername = username ?? string.Empty;
        var newPassword = password ?? string.Empty;

        var changed = _proxyEnabled != enabled
                      || !string.Equals(_proxyHost, newHost, StringComparison.Ordinal)
                      || _proxyPort != newPort
                      || !string.Equals(_proxyUsername, newUsername, StringComparison.Ordinal)
                      || !string.Equals(_proxyPassword, newPassword, StringComparison.Ordinal);

        _proxyEnabled = enabled;
        _proxyHost = newHost;
        _proxyPort = newPort;
        _proxyUsername = newUsername;
        _proxyPassword = newPassword;

        System.Diagnostics.Debug.WriteLine(_proxyEnabled
            ? $"Proxy settings updated: {_proxyHost}:{_proxyPort}"
            : "Proxy disabled");

        if (changed && _engine is not null)
        {
            ScheduleEngineRebuild();
        }
    }

    /// <summary>
    /// Debounces proxy-triggered engine rebuilds: each call cancels the previously scheduled
    /// rebuild and restarts the timer, so a burst of setting changes (e.g. typing a hostname
    /// one character at a time) collapses into a single rebuild once the user stops.
    /// </summary>
    private void ScheduleEngineRebuild()
    {
        CancellationToken token;
        lock (_proxyRebuildGate)
        {
            _proxyRebuildCts?.Cancel();
            _proxyRebuildCts?.Dispose();
            _proxyRebuildCts = new CancellationTokenSource();
            token = _proxyRebuildCts.Token;
        }

        SafeFireAndForget(DebouncedEngineRebuildAsync(token));
    }

    private async Task DebouncedEngineRebuildAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ProxyRebuildDebounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Serialize rebuilds: a rebuild stops every manager, disposes the engine, and
        // restarts torrents, which takes seconds. A second rebuild starting meanwhile would
        // race on _engine and double-stop the same managers.
        await _engineRebuildLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await RebuildEngineAsync().ConfigureAwait(false);
        }
        finally
        {
            _engineRebuildLock.Release();
        }
    }

    /// <summary>
    /// Tears down the current engine and its managers so that a fresh engine is created
    /// on the next torrent start (picking up the new proxy settings). Torrents that were
    /// actively downloading or seeding are restarted automatically.
    /// </summary>
    private async Task RebuildEngineAsync()
    {
        List<string> idsToResume;
        lock (_torrentsLock)
        {
            idsToResume = Torrents
                .Where(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Seeding)
                .Select(t => t.Id)
                .ToList();
        }

        // Cancel any active monitors and stop their managers.
        foreach (var kvp in _downloadTokens.ToArray())
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch
            {
                // best-effort
            }
            _downloadTokens.TryRemove(kvp.Key, out _);
        }

        foreach (var kvp in _managers.ToArray())
        {
            try
            {
                await StopManagerAsync(kvp.Value);
                if (_engine is not null)
                {
                    await _engine.RemoveAsync(kvp.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Proxy rebuild: stop manager error for {kvp.Key}: {ex.Message}");
            }
            _managers.TryRemove(kvp.Key, out _);
        }

        // Tear down the engine under the same lock that guards creation so a concurrent
        // start cannot create a fresh engine while we are disposing the old one. The lock
        // is released before the restart loop below, which recreates the engine via
        // EnsureEngineAsync.
        ClientEngine? engineToDispose;
        await _engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            engineToDispose = _engine;
            _engine = null;
        }
        finally
        {
            _engineLock.Release();
        }

        if (engineToDispose is not null)
        {
            try
            {
                await engineToDispose.StopAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Proxy rebuild: engine stop error: {ex.Message}");
            }

            try
            {
                if (engineToDispose is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (engineToDispose is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Proxy rebuild: engine dispose error: {ex.Message}");
            }
        }

        // Move previously-running torrents back to a restartable state and kick them off.
        foreach (var id in idsToResume)
        {
            if (!TryGetTorrentById(id, out var torrent) || torrent is null)
            {
                continue;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding)
                {
                    torrent.Status = DownloadStatus.Queued;
                    torrent.DownloadSpeed = 0;
                    torrent.UploadSpeed = 0;
                }
            });

            try
            {
                await StartTorrentAsync(torrent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Proxy rebuild: restart error for '{torrent.Name}': {ex.Message}");
            }
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

    private static long KbpsToBytes(int kbps) => kbps <= 0 ? 0 : kbps * 1024L;

    private void UpdateBackgroundTransferState()
    {
        bool hasActiveTransfers;
        lock (_torrentsLock)
        {
            hasActiveTransfers = Torrents.Any(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Seeding);
        }
        if (hasActiveTransfers == _backgroundTransferActive)
        {
            return;
        }

        _backgroundTransferActive = hasActiveTransfers;

        if (hasActiveTransfers)
        {
            _backgroundDownloadService.Start();
        }
        else
        {
            _backgroundDownloadService.Stop();
        }
    }

    private bool TryGetTorrentById(string id, out TorrentItem? torrent)
    {
        lock (_torrentsLock)
        {
            torrent = Torrents.FirstOrDefault(t => t.Id == id);
            return torrent is not null;
        }
    }

    private List<TorrentIdentity> GetTorrentIdentitySnapshot()
    {
        lock (_torrentsLock)
        {
            return Torrents
                .Select(static torrent => new TorrentIdentity(torrent.Id, torrent.InfoHash, torrent.MagnetLink))
                .ToList();
        }
    }

    private void AttachTorrentSettingsHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged -= OnTorrentPropertyChanged;
        torrent.PropertyChanged += OnTorrentPropertyChanged;
    }

    private void DetachTorrentSettingsHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged -= OnTorrentPropertyChanged;
    }

    private void OnTorrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TorrentItem torrent)
        {
            return;
        }

        if (e.PropertyName is nameof(TorrentItem.DownloadLimitKbps) or nameof(TorrentItem.UploadLimitKbps))
        {
            SafeFireAndForget(UpdateTorrentManagerSettingsAsync(torrent));
        }

        if (e.PropertyName is nameof(TorrentItem.TorrentFilePath) or nameof(TorrentItem.TorrentFileName) or nameof(TorrentItem.SavePath))
        {
            _pendingSave = true;
        }
    }

    private async Task UpdateTorrentManagerSettingsAsync(TorrentItem torrent)
    {
        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            try
            {
                await ApplySpeedLimitsToManagerAsync(manager, torrent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update settings error: {ex.Message}");
            }
        }
    }

    private bool CanStartAnotherDownload()
    {
        if (_maxActiveDownloads <= 0)
        {
            return true;
        }

        int activeDownloads;
        lock (_torrentsLock)
        {
            activeDownloads = Torrents.Count(t => t.Status == DownloadStatus.Downloading);
        }
        return activeDownloads < _maxActiveDownloads;
    }

    private bool CanStartAnotherSeed()
    {
        if (_maxActiveSeeds <= 0)
        {
            return true;
        }

        int activeSeeds;
        lock (_torrentsLock)
        {
            activeSeeds = Torrents.Count(t => t.Status == DownloadStatus.Seeding);
        }
        return activeSeeds < _maxActiveSeeds;
    }

    private async Task TryStartQueuedTorrentsAsync()
    {
        List<TorrentItem> queued;
        lock (_torrentsLock)
        {
            var availableSlots = _maxActiveDownloads <= 0
                ? int.MaxValue
                : Math.Max(0, _maxActiveDownloads - Torrents.Count(t => t.Status == DownloadStatus.Downloading));

            if (availableSlots <= 0)
            {
                return;
            }

            queued = Torrents
                .Where(t => t.Status == DownloadStatus.Queued)
                .OrderBy(t => t.DateAdded)
                .Take(availableSlots)
                .ToList();
        }

        foreach (var torrent in queued)
        {
            try
            {
                await StartTorrentAsync(torrent);
            }
            catch (Exception ex)
            {
                // StartTorrentAsync already marked this torrent Failed before rethrowing.
                // Swallow here so one bad torrent cannot corrupt the status/error of the
                // operation that drained the queue (a completing download, a pause/stop/
                // remove, or a settings change).
                System.Diagnostics.Debug.WriteLine($"Queued start error for '{torrent.Name}' ({torrent.Id}): {ex}");
            }
        }
    }

    private async Task EnforceSeedingLimitsAsync(TorrentItem torrent, TorrentManager manager)
    {
        if (torrent.Status != DownloadStatus.Seeding)
        {
            return;
        }

        if (_maxActiveSeeds > 0)
        {
            int activeSeeds;
            lock (_torrentsLock)
            {
                activeSeeds = Torrents.Count(t => t.Status == DownloadStatus.Seeding);
            }
            if (activeSeeds > _maxActiveSeeds)
            {
                await PauseTorrentAsync(torrent);
                return;
            }
        }

        var maxRatio = torrent.MaxSeedRatio > 0 ? torrent.MaxSeedRatio : _globalMaxSeedRatio;
        var maxMinutes = torrent.MaxSeedMinutes > 0 ? torrent.MaxSeedMinutes : _globalMaxSeedMinutes;

        if (maxRatio > 0 && torrent.TotalSize > 0)
        {
            var ratio = torrent.UploadedSize / (double)torrent.TotalSize;
            if (ratio >= maxRatio)
            {
                await PauseTorrentAsync(torrent);
                return;
            }
        }

        if (maxMinutes > 0 && torrent.DateSeedingStarted.HasValue)
        {
            var elapsed = DateTime.Now - torrent.DateSeedingStarted.Value;
            if (elapsed.TotalMinutes >= maxMinutes)
            {
                await PauseTorrentAsync(torrent);
            }
        }
    }

    private static async Task ApplySpeedLimitsToEngineAsync(ClientEngine engine, long downloadLimitBytesPerSec, long uploadLimitBytesPerSec)
    {
        var settings = new EngineSettingsBuilder(engine.Settings)
        {
            MaximumDownloadRate = ToRate(downloadLimitBytesPerSec),
            MaximumUploadRate = ToRate(uploadLimitBytesPerSec),
        }.ToSettings();

        await engine.UpdateSettingsAsync(settings);
    }

    private async Task ApplySpeedLimitsToManagerAsync(TorrentManager manager, TorrentItem torrent)
    {
        var (downloadLimit, uploadLimit) = ResolveManagerLimits(torrent);

        var settings = new TorrentSettingsBuilder(manager.Settings)
        {
            MaximumDownloadRate = ToRate(downloadLimit),
            MaximumUploadRate = ToRate(uploadLimit),
        }.ToSettings();

        await manager.UpdateSettingsAsync(settings);
    }

    /// <summary>
    /// Resolves the effective download/upload byte-per-second limits for a torrent,
    /// preferring its per-torrent override and falling back to the global limit (0 = unlimited).
    /// </summary>
    private (long Download, long Upload) ResolveManagerLimits(TorrentItem torrent)
    {
        var downloadLimit = torrent.DownloadLimitKbps > 0
            ? KbpsToBytes(torrent.DownloadLimitKbps)
            : _globalDownloadLimitBytesPerSec;

        var uploadLimit = torrent.UploadLimitKbps > 0
            ? KbpsToBytes(torrent.UploadLimitKbps)
            : _globalUploadLimitBytesPerSec;

        return (downloadLimit, uploadLimit);
    }

    // MonoTorrent expresses rate limits as a 32-bit bytes/second value (0 = unlimited).
    private static int ToRate(long bytesPerSecond) => (int)Math.Clamp(bytesPerSecond, 0, int.MaxValue);

    private async Task MonitorTorrentAsync(TorrentItem torrent, TorrentManager manager, CancellationToken cancellationToken)
    {
        try
        {
            long previousDataBytesSent = manager.Monitor.DataBytesSent;

            while (!cancellationToken.IsCancellationRequested)
            {
                // ConfigureAwait(false) keeps the loop body off the UI thread. The only
                // work that must touch the UI thread is the explicit
                // MainThread.InvokeOnMainThreadAsync block below; everything else
                // (reflection, peer/piece scanning) runs on the thread pool.
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                // ---- Collect all data on the background thread ----
                var metadataSize = manager.Torrent?.Size;
                var progress = manager.Progress;
                var previousStatus = torrent.Status;
                var currentDataBytesSent = manager.Monitor.DataBytesSent;
                var uploadedDelta = currentDataBytesSent - previousDataBytesSent;
                previousDataBytesSent = currentDataBytesSent;

                var downloadRate = manager.Monitor.DownloadRate;
                var uploadRate = manager.Monitor.UploadRate;
                var managerState = manager.State;
                var errorMessage = managerState == TorrentState.Error
                    ? (manager.Error?.Exception?.Message ?? "Unknown error occurred")
                    : null;

                // Peer counts come straight from MonoTorrent's typed API (note it spells the
                // leecher count "Leechs"). The old reflection probed property names that do
                // not exist in MonoTorrent 3.x, so leechers were always reported as zero.
                var peers = manager.Peers;
                var seeds = peers?.Seeds ?? 0;
                var leeches = peers?.Leechs ?? 0;

                var availabilityInfo = GetAvailabilityInfo(seeds, leeches);
                var healthScore = ComputeHealthScore(seeds, leeches, availabilityInfo.Percent);

                // Metadata from torrent file (if available)
                var torrentSize = (manager.HasMetadata && manager.Torrent != null) ? manager.Torrent.Size : (long?)null;
                var torrentName = (manager.HasMetadata && manager.Torrent != null) ? manager.Torrent.Name : null;

                // Map state on background thread
                var mappedStatus = managerState switch
                {
                    TorrentState.Paused => DownloadStatus.Paused,
                    TorrentState.Seeding => DownloadStatus.Seeding,
                    TorrentState.Stopped when progress >= 100 => DownloadStatus.Completed,
                    TorrentState.Stopped => DownloadStatus.Stopped,
                    TorrentState.Downloading => DownloadStatus.Downloading,
                    TorrentState.Error => DownloadStatus.Failed,
                    _ => previousStatus
                };

                // ---- Marshal only lightweight property assignments to the UI thread ----
                var currentStatus = previousStatus;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // If the download was cancelled (pause/stop) while this dispatch was
                    // queued, skip the update to avoid overwriting the status set by
                    // PauseTorrentAsync / StopTorrentAsync.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (metadataSize.HasValue && metadataSize.Value > 0)
                    {
                        torrent.TotalSize = metadataSize.Value;
                    }

                    if (torrentSize.HasValue && torrentSize.Value > 0)
                    {
                        torrent.TotalSize = torrentSize.Value;
                    }

                    if (torrentName is not null)
                    {
                        torrent.Name = torrentName;
                    }

                    if (torrent.TotalSize > 0)
                    {
                        torrent.DownloadedSize = (long)(torrent.TotalSize * (progress / 100.0));
                    }

                    if (uploadedDelta > 0)
                    {
                        torrent.UploadedSize += uploadedDelta;
                    }

                    torrent.Progress = progress;
                    torrent.DownloadSpeed = downloadRate;
                    torrent.UploadSpeed = uploadRate;

                    torrent.Seeders = seeds;
                    torrent.Leechers = leeches;

                    torrent.AvailabilityPercent = availabilityInfo.Percent;
                    torrent.AvailabilityLabel = availabilityInfo.Label;
                    torrent.HealthScore = healthScore;

                    torrent.AddSpeedSample(downloadRate, uploadRate);

                    if (downloadRate > 0 && torrent.TotalSize > 0)
                    {
                        var remainingBytes = Math.Max(0, torrent.TotalSize - torrent.DownloadedSize);
                        torrent.EstimatedSecondsRemaining = remainingBytes / Math.Max(1, downloadRate);
                    }
                    else
                    {
                        torrent.EstimatedSecondsRemaining = 0;
                    }

                    torrent.Status = mappedStatus;
                    torrent.ErrorMessage = errorMessage;

                    if (torrent.Status == DownloadStatus.Seeding)
                    {
                        torrent.DateSeedingStarted ??= DateTime.Now;
                    }
                    else
                    {
                        torrent.DateSeedingStarted = null;
                    }

                    if (torrent.Status is DownloadStatus.Completed or DownloadStatus.Seeding)
                    {
                        torrent.DateCompleted ??= DateTime.Now;
                    }

                    currentStatus = torrent.Status;
                }).ConfigureAwait(false);

                var wasComplete = previousStatus is DownloadStatus.Completed or DownloadStatus.Seeding;
                var isComplete = currentStatus is DownloadStatus.Completed or DownloadStatus.Seeding;

                if (previousStatus != currentStatus)
                {
                    UpdateBackgroundTransferState();
                }

                if (!wasComplete && isComplete)
                {
                    await _notificationService.ShowDownloadCompletedAsync(torrent).ConfigureAwait(false);
                }

                _pendingSave = true;

                if (previousStatus == DownloadStatus.Downloading && currentStatus != DownloadStatus.Downloading)
                {
                    await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
                }

                if (currentStatus == DownloadStatus.Seeding)
                {
                    await EnforceSeedingLimitsAsync(torrent, manager).ConfigureAwait(false);
                }

                if (managerState == TorrentState.Stopped && progress >= 100)
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
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                torrent.Status = DownloadStatus.Failed;
                torrent.ErrorMessage = ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            // Only remove our CTS – a newer Start may have already replaced it.
            if (_downloadTokens.TryGetValue(torrent.Id, out var activeCts)
                && activeCts.Token == cancellationToken)
            {
                _downloadTokens.TryRemove(torrent.Id, out _);
            }

            _pendingSave = true;
            UpdateBackgroundTransferState();
        }
    }

    private readonly record struct AvailabilityInfo(double Percent, string Label);

    /// <summary>
    /// Reports swarm availability from the connected peer counts. Each seed is one full copy
    /// of the torrent; the partial copies held by leechers cannot be measured because peer
    /// bitfields are not exposed by MonoTorrent's public API, so the seed count is the
    /// guaranteed-availability figure and the swarm size is the secondary signal.
    /// </summary>
    private static AvailabilityInfo GetAvailabilityInfo(int seeds, int leeches)
    {
        if (seeds > 0)
        {
            var meterPercent = Math.Clamp(seeds / 2d, 0, 1) * 100;
            return new AvailabilityInfo(meterPercent, $"{seeds:0.0}x");
        }

        if (leeches > 0)
        {
            var swarmPercent = Math.Clamp(leeches / 20d, 0, 1) * 100;
            return new AvailabilityInfo(swarmPercent, $"{seeds}S/{leeches}L");
        }

        return new AvailabilityInfo(0, "—");
    }

    private static int ComputeHealthScore(int seeds, int leeches, double availabilityPercent)
    {
        var availabilityScore = Math.Clamp(availabilityPercent, 0, 100) * 0.5; // up to 50 points
        var seedScore = Math.Min(1, seeds / 10d) * 30; // up to 30 points
        var peerScore = Math.Min(1, (seeds + leeches) / 20d) * 20; // up to 20 points
        return (int)Math.Round(availabilityScore + seedScore + peerScore, MidpointRounding.AwayFromZero);
    }

    private async Task SaveIfPendingAsync()
    {
        try
        {
            if (_pendingSave)
            {
                _pendingSave = false;
                await SaveAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Debounced save error: {ex.Message}");
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

        await _engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock: another caller may have created the
            // engine while we were waiting. Without this, concurrent starts could each
            // build a ClientEngine and orphan all but the last one.
            if (_engine is not null)
            {
                return _engine;
            }

            return _engine = CreateEngine();
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private ClientEngine CreateEngine()
    {
        // When a SOCKS5 proxy is active we can only tunnel outbound TCP (peer connections and
        // HTTP/HTTPS tracker + web-seed requests). Every other discovery channel below either
        // uses UDP (DHT) or exposes a directly reachable endpoint (inbound listener,
        // UPnP/NAT-PMP, local peer discovery), none of which the proxy covers — leaving them
        // enabled would broadcast the user's real IP and defeat the purpose of the proxy. So
        // in proxy mode we shut them all down and rely on proxied peer + HTTP-tracker traffic
        // only. UDP bootstrap trackers are likewise skipped (see GetOrCreateManagerAsync).
        var useProxy = ProxyActive;

        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = _storageService.GetDefaultDownloadPath(),
            // UPnP/NAT-PMP opens an inbound port on the router and advertises our address.
            AllowPortForwarding = !useProxy,
            // DHT is UDP and announces our IP to the swarm; disable it (and its cache) when proxied.
            DhtEndPoint = useProxy ? null : new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0),
            AutoSaveLoadDhtCache = !useProxy,
            // Local Peer Discovery multicasts our LAN address.
            AllowLocalPeerDiscovery = !useProxy,
            // Increase maximum connections for better download speeds
            MaximumConnections = 200,
            MaximumHalfOpenConnections = 50,
            // Apply global speed limits up front (0 = unlimited).
            MaximumDownloadRate = ToRate(_globalDownloadLimitBytesPerSec),
            MaximumUploadRate = ToRate(_globalUploadLimitBytesPerSec),
            // Accept incoming connections only when not proxied; an inbound listener would
            // expose our real address to any peer that dials in.
            ListenEndPoints = useProxy
                ? new Dictionary<string, System.Net.IPEndPoint>()
                : new Dictionary<string, System.Net.IPEndPoint>
                {
                    { "ipv4", new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0) }
                }
        };

        var engineSettings = builder.ToSettings();

        if (!useProxy)
        {
            return new ClientEngine(engineSettings);
        }

        // Route outbound peer (TCP) connections and HTTP(S) tracker / web-seed requests
        // through the SOCKS5 proxy. MonoTorrent exposes no proxy setting, so we swap the
        // socket connector and the HTTP client via Factories.
        var host = _proxyHost;
        var port = _proxyPort;
        var username = _proxyUsername;
        var password = _proxyPassword;
        var factories = Factories.Default
            .WithSocketConnectorCreator(() => new Socks5SocketConnector(host, port, username, password))
            .WithHttpClientCreator(_ => CreateProxiedHttpClient(host, port, username, password));
        return new ClientEngine(engineSettings, factories);
    }

    private static System.Net.Http.HttpClient CreateProxiedHttpClient(string host, int port, string username, string password)
    {
        var proxy = new System.Net.WebProxy($"socks5://{host}:{port}");
        if (!string.IsNullOrEmpty(username))
        {
            proxy.Credentials = new System.Net.NetworkCredential(username, password);
        }

        var handler = new System.Net.Http.SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = true
        };

        return new System.Net.Http.HttpClient(handler, disposeHandler: true);
    }


    protected virtual async Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent)
    {
        if (_managers.TryGetValue(torrent.Id, out var existing))
        {
            return existing;
        }

        var engine = await EnsureEngineAsync();
        var downloadPath = string.IsNullOrWhiteSpace(torrent.SavePath) ? _storageService.GetDefaultDownloadPath() : torrent.SavePath;
        Directory.CreateDirectory(downloadPath);
        torrent.SavePath = downloadPath;

        var (downloadLimit, uploadLimit) = ResolveManagerLimits(torrent);
        var torrentSettings = new TorrentSettingsBuilder
        {
            // Increase maximum connections per torrent
            MaximumConnections = 100,
            // Limit upload slots to prioritize downloads
            UploadSlots = 4,
            // Apply per-torrent (or global) speed limits up front (0 = unlimited).
            MaximumDownloadRate = ToRate(downloadLimit),
            MaximumUploadRate = ToRate(uploadLimit),
        }.ToSettings();

        TorrentManager manager;
        if (!string.IsNullOrWhiteSpace(torrent.TorrentFilePath) && File.Exists(torrent.TorrentFilePath))
        {
            try
            {
                var monoTorrent = await MonoTorrent.Torrent.LoadAsync(torrent.TorrentFilePath);
                manager = await engine.AddAsync(monoTorrent, downloadPath, torrentSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load .torrent file: {ex.Message}. Falling back to magnet link.");
                var magnet = MagnetLink.Parse(torrent.MagnetLink);
                manager = await engine.AddAsync(magnet, downloadPath, torrentSettings);
                if (!ProxyActive)
                {
                    await AddPublicTrackersIfNeededAsync(manager, torrent.MagnetLink);
                }
            }
        }
        else
        {
            var magnet = MagnetLink.Parse(torrent.MagnetLink);
            manager = await engine.AddAsync(magnet, downloadPath, torrentSettings);
            if (!ProxyActive)
            {
                // The bootstrap trackers are all UDP, which the SOCKS5 proxy cannot tunnel;
                // adding them in proxy mode would leak the real IP via tracker announces.
                await AddPublicTrackersIfNeededAsync(manager, torrent.MagnetLink);
            }
        }

        _managers[torrent.Id] = manager;
        return manager;
    }

    protected virtual Task StartManagerAsync(TorrentManager manager) => manager.StartAsync();

    private static Task StopManagerAsync(TorrentManager manager)
    {
        if (!TorrentManagerStateRules.RequiresFullStop(manager.State))
        {
            return Task.CompletedTask;
        }

        return manager.StopAsync(ManagerStopTimeout);
    }

    private static async Task AddPublicTrackersIfNeededAsync(TorrentManager manager, string magnetLink)
    {
        if (!MagnetTrackerBootstrapRules.ShouldAddPublicTrackers(magnetLink) || manager.TrackerManager.Private)
        {
            return;
        }

        foreach (var tracker in PublicTrackers)
        {
            try
            {
                await manager.TrackerManager.AddTrackerAsync(tracker);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to add bootstrap tracker {tracker}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeAsyncCore().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    private async Task DisposeAsyncCore()
    {
        try
        {
            _saveTimer.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save timer dispose error: {ex.Message}");
        }

        _backgroundTransferActive = false;

        try
        {
            _backgroundDownloadService.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Background service stop error: {ex.Message}");
        }

        // Cancel and dispose all active download tokens
        foreach (var kvp in _downloadTokens)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token disposal error for {kvp.Key}: {ex.Message}");
            }
        }
        _downloadTokens.Clear();
        _pendingSave = false;
        _torrentOperationLock.Dispose();

        // Cancel any pending/debounced proxy rebuild so it does not run after disposal.
        lock (_proxyRebuildGate)
        {
            _proxyRebuildCts?.Cancel();
            _proxyRebuildCts?.Dispose();
            _proxyRebuildCts = null;
        }

        // ConfigureAwait(false) is required here: Dispose() blocks on this method via
        // GetAwaiter().GetResult(). If a continuation resumed on the (blocked) UI thread,
        // shutdown would deadlock.
        foreach (var kvp in _managers)
        {
            try
            {
                await kvp.Value.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A missing .torrent file or network error during shutdown should not crash the app.
                System.Diagnostics.Debug.WriteLine($"Manager stop error for {kvp.Key}: {ex.Message}");
            }
        }
        _managers.Clear();

        if (_engine is not null)
        {
            try
            {
                await _engine.StopAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Engine StopAll error: {ex.Message}");
            }

            try
            {
                if (_engine is IAsyncDisposable asyncDisposableEngine)
                {
                    await asyncDisposableEngine.DisposeAsync().ConfigureAwait(false);
                }
                else if (_engine is IDisposable disposableEngine)
                {
                    disposableEngine.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Engine dispose error: {ex.Message}");
            }

            _engine = null;
        }

        _engineLock.Dispose();

        try
        {
            _engineRebuildLock.Dispose();
        }
        catch (Exception ex)
        {
            // A debounced rebuild may still hold the lock during shutdown; ignore.
            System.Diagnostics.Debug.WriteLine($"Engine rebuild lock dispose error: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the task without awaiting, logging any exceptions instead of crashing.
    /// </summary>
    private static async void SafeFireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fire-and-forget error: {ex}");
        }
    }
}
