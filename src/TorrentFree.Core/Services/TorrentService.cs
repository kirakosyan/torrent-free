using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Linq;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections.Peer;
using MonoTorrent.Connections.Tracker;
using MonoTorrent.Trackers;
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

    /// <summary>Applies Wi-Fi-only transfers and resumes only torrents waiting for Wi-Fi.</summary>
    Task UpdateWifiOnlyAsync(bool enabled);

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
    /// Pauses every active transfer before the operating system revokes background execution.
    /// Interrupted transfers are requeued rather than paused, so they resume automatically once
    /// the application returns to the foreground (or on the next launch). Nothing is started
    /// by this bulk operation.
    /// </summary>
    Task PauseAllForBackgroundTimeoutAsync();

    /// <summary>
    /// Allows transfers to be started again after the application has returned to the foreground.
    /// </summary>
    void ResumeAfterBackgroundTimeout();

    /// <summary>
    /// Starts queued torrents while capacity allows. Hosts call this once the app is ready for
    /// network activity, for example after <see cref="InitializeAsync"/> restored transfers
    /// which were active when the app last closed.
    /// </summary>
    Task StartQueuedTorrentsAsync();

    /// <summary>
    /// Removes a torrent from the list.
    /// </summary>
    Task<TorrentRemovalResult> RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false);

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

    /// <summary>
    /// Keeps the device CPU awake while transfers run in the background, where the platform supports it.
    /// </summary>
    void UpdateKeepDeviceAwake(bool enabled);
}

/// <summary>
/// Service for managing torrent downloads.
/// </summary>
public partial class TorrentService : ITorrentService
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
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationService _notificationService;
    private readonly IDownloadCompletionObserver? _completionObserver;
    private readonly IBackgroundDownloadService _backgroundDownloadService;
    private readonly ISleepPreventionService? _sleepPreventionService;
    private bool _keepDeviceAwake;
    private readonly AsyncKeyedLocker _torrentOperationLock = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens = new();
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly Dictionary<string, long> _seedingSessions = new();
    private readonly ConcurrentDictionary<string, byte> _proxyRebuildPendingResumeIds = new();
    private readonly object _torrentsLock = new();
    private readonly Timer _saveTimer;
    // Read on several threads outside _engineLock (fast-path checks); volatile guarantees
    // each reader sees the latest write (e.g. after RebuildEngineAsync nulls it).
    private volatile ClientEngine? _engine;
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    // Serializes publication and payload deletion, including torrents with different hashes
    // which map to the same files. Never hold this gate while acquiring a metadata cache lock.
    private readonly SemaphoreSlim _payloadOwnershipLock = new(1, 1);
    private Task? _initTask;
    private readonly object _initGate = new();
    private volatile bool _pendingSave;
    // Progress-only changes (bytes, speeds, seeding time) are persisted at most every
    // ProgressSaveInterval; status and settings changes still use the 5-second debounce.
    private volatile bool _pendingProgressSave;
    private long _lastSaveTimestamp;
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(30);
    private bool _disposed;
    private volatile bool _backgroundTransferActive;
    private bool _backgroundStartRefused;
    private readonly object _backgroundStateGate = new();
    private volatile bool _backgroundExecutionSuspended;
    // Cancelled first thing on Dispose so any pending lock/semaphore wait unblocks via
    // OperationCanceledException instead of hanging on a primitive that Dispose then tears down.
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly CancellationToken _disposalToken;

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
    private readonly object _engineRebuildStateGate = new();
    private TaskCompletionSource? _activeEngineRebuild;
    private TaskCompletionSource? _activeStartsDrained;
    private int _activeStarts;
    private int _startBarrierHolders;
    private static readonly TimeSpan ProxyRebuildDebounce = TimeSpan.FromMilliseconds(800);

    // A SOCKS5 proxy can only tunnel outbound TCP, so when it is active we disable every
    // channel that would otherwise expose the user's real IP (DHT, LPD, UPnP, the inbound
    // listener, and UDP trackers). Centralised here so engine creation and tracker
    // bootstrap stay in agreement.
    // Proxy mode must fail closed. An enabled-but-incomplete configuration is still proxy
    // mode: direct DHT/tracker/peer traffic must never resume merely because the host is blank.
    private bool ProxyRequested => _proxyEnabled;

    public ObservableCollection<TorrentItem> Torrents { get; } = [];

    public TorrentService(IStorageService storageService, INotificationService notificationService, IBackgroundDownloadService backgroundDownloadService, IUiDispatcher dispatcher, TimeProvider? timeProvider = null, IDownloadCompletionObserver? completionObserver = null, ITransferNetworkMonitor? networkMonitor = null, ISleepPreventionService? sleepPreventionService = null)
    {
        _storageService = storageService;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _notificationService = notificationService;
        _completionObserver = completionObserver;
        _backgroundDownloadService = backgroundDownloadService;
        _sleepPreventionService = sleepPreventionService;
        _disposalToken = _disposalCts.Token;
        _networkMonitor = networkMonitor;
        if (_networkMonitor is not null)
            _networkMonitor.Changed += OnNetworkChanged;
        // Debounced save timer - saves at most every 5 seconds
        _saveTimer = new Timer(async _ => await SaveIfPendingAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        lock (_initGate)
        {
            if (_initTask is null || _initTask.IsFaulted || _initTask.IsCanceled)
                _initTask = InitializeCoreAsync();
            return _initTask;
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            _wifiOnly = (await _storageService.LoadSettingsAsync()).WifiOnly;
            var savedTorrents = await _storageService.LoadTorrentsAsync();
            var hadStateChanges = false;

            foreach (var torrent in savedTorrents)
            {
                var hadMissingTorrentFile = TorrentRestoreRules.HasMissingTorrentFile(torrent.TorrentFilePath);
                var restoreDecision = TorrentRestoreRules.Evaluate(
                    new TorrentIdentity(torrent.Id, torrent.InfoHash, torrent.MagnetLink),
                    GetMetadataPath(torrent) ?? torrent.TorrentFilePath,
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

                // No MonoTorrent manager is recreated during restore. Persisted active states
                // become Queued: truthful (no network session exists yet) and resumed
                // automatically by StartQueuedTorrentsAsync once the host is ready. Manual
                // pauses were persisted as Paused and stay paused.
                if (torrent.DateSeedingStarted is not null)
                {
                    // A persisted session start cannot distinguish app downtime from active seeding.
                    torrent.DateSeedingStarted = null;
                    hadStateChanges = true;
                }
                if (torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding)
                {
                    torrent.Status = DownloadStatus.Queued;
                    torrent.DateSeedingStarted = null;
                    hadStateChanges = true;
                }

                AttachTorrentSettingsHandlers(torrent);
                await _dispatcher.InvokeAsync(() =>
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

            _networkPolicyReady = true;
            await ReconcileNetworkPolicyAsync();
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
    public async Task StartQueuedTorrentsAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<TorrentItem?> AddTorrentAsync(string magnetLink) => AddTorrentCoreAsync(magnetLink);

    private async Task<TorrentItem?> AddTorrentCoreAsync(string magnetLink, TorrentMetadata? metadata = null)
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

        await InitializeAsync();
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
            TorrentFilePath = metadata?.SourceFilePath,
            TorrentFileName = metadata?.SourceFileName,
            CachedTorrentFilePath = metadata?.CachedFilePath,
            SavePath = _storageService.SupportsCustomDownloadLocations
                ? DownloadLocationResolver.ResolveSavePath(settings, metadata?.DownloadSourcePath, fallbackDownloadPath)
                : fallbackDownloadPath
        };

        await _payloadOwnershipLock.WaitAsync(_disposalToken);
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                // The early duplicate check is only a fast path. Another import can pass it
                // while this call is loading settings, so check and add under one lock.
                lock (_torrentsLock)
                {
                    if (IsDuplicate(infoHash, magnetLink))
                        throw new DuplicateTorrentException("This torrent is already added.");

                    AttachTorrentSettingsHandlers(torrent);
                    Torrents.Add(torrent);
                }
            });
        }
        finally { _payloadOwnershipLock.Release(); }
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
        if (!IsValidMagnetLink(magnet)) return null;
        await InitializeAsync();
        var cachePath = TorrentImportService.GetCachePath(_storageService.GetAppDataPath(), metadata.InfoHashHex);
        await using var cacheLock = await TorrentImportService.LockCacheAsync(cachePath, _disposalToken);
        if (IsDuplicate(metadata.InfoHashHex, magnet))
            throw new DuplicateTorrentException("This torrent is already added.");

        var cacheCreated = false;
        TorrentItem? addedTorrent = null;
        try
        {
            // Commit prepared bytes only when importing. Hold the cache lock through
            // publication so removal of an older item cannot delete the new item's data.
            if (metadata.CachedContent is not null && PathsEqual(metadata.CachedFilePath, cachePath) && !File.Exists(cachePath))
            {
                await TorrentImportService.WriteCacheAsync(cachePath, metadata.CachedContent, _disposalToken);
                cacheCreated = true;
            }
            addedTorrent = await AddTorrentCoreAsync(magnet, metadata);
            return addedTorrent;
        }
        finally
        {
            if (cacheCreated && addedTorrent is null)
            {
                try
                {
                    // A competing magnet import can win the duplicate check, while a
                    // failed state save can leave this item tracked. Preserve any cache
                    // still owned by a tracked item, as well as all pre-existing files.
                    lock (_torrentsLock)
                    {
                        if (!Torrents.Any(t => PathsEqual(t.CachedTorrentFilePath, cachePath)))
                            File.Delete(cachePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed import cache cleanup: {ex.Message}");
                }
            }
        }
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
    public Task StartTorrentAsync(TorrentItem torrent)
    {
        if (_backgroundExecutionSuspended) ResumeAfterBackgroundTimeout();
        return StartTorrentIfStatusAsync(torrent);
    }

    private async Task StartTorrentIfStatusAsync(TorrentItem torrent, DownloadStatus? expectedStatus = null)
    {
        while (true)
        {
            if (_backgroundExecutionSuspended)
            {
                if (expectedStatus is not null) return;
                await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
                {
                    await SuppressStartForBackgroundTimeoutAsync(torrent);
                }
                return;
            }

            await WaitForEngineRebuildAsync().ConfigureAwait(false);

            Task? rebuildWhichWonTheRace = null;
            Exception? startException = null;
            var startSuppressed = false;
            await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
            {
                if (expectedStatus is not null && torrent.Status != expectedStatus) return;
                // A rebuild can begin after the wait above but before this keyed lock is
                // acquired. Re-check while holding the key and release/loop if it did.
                if (_backgroundExecutionSuspended)
                {
                    if (expectedStatus is not null) return;
                    await SuppressStartForBackgroundTimeoutAsync(torrent);
                    startSuppressed = true;
                }
                else if (!TryEnterStart(out rebuildWhichWonTheRace))
                {
                    // Leave this scope before awaiting the rebuild to avoid lock inversion.
                }
                else
                {
                    try
                    {
                        await StartTorrentCoreAsync(torrent);
                    }
                    catch (Exception ex)
                    {
                        startException = ex;
                    }
                    finally
                    {
                        ExitStart();
                    }
                }
            }

            if (startSuppressed)
            {
                return;
            }

            if (rebuildWhichWonTheRace is not null)
            {
                await rebuildWhichWonTheRace.ConfigureAwait(false);
                continue;
            }

            if (startException is not null)
            {
                // Drain the queue only after releasing this torrent's keyed lock.
                await TryStartQueuedTorrentsAsync();
                ExceptionDispatchInfo.Capture(startException).Throw();
            }

            return;
        }
    }

    private async Task SuppressStartForBackgroundTimeoutAsync(TorrentItem torrent)
    {
        if (!TryGetTorrentById(torrent.Id, out var trackedTorrent) || !ReferenceEquals(torrent, trackedTorrent))
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            torrent.DownloadSpeed = 0;
            torrent.UploadSpeed = 0;
            // Queued, not Paused: the start resumes automatically in the foreground.
            torrent.Status = DownloadStatus.Queued;
            torrent.ErrorMessage = null;
        });
        await SaveAsync();
        UpdateBackgroundTransferState();
    }

    private async Task StartTorrentCoreAsync(
        TorrentItem torrent,
        CancellationToken proxyRebuildToken = default)
    {
        proxyRebuildToken.ThrowIfCancellationRequested();

        if (!torrent.CanStart)
        {
            return;
        }

        if (!TryGetTorrentById(torrent.Id, out var trackedTorrent) || !ReferenceEquals(torrent, trackedTorrent)) return;
        if (NetworkBlocked)
        {
            await MarkWaitingForWifiAsync(torrent);
            await SaveAsync();
            return;
        }

        var admitted = false;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_torrentsLock)
            {
                if (!Torrents.Contains(torrent)) return;
                var seed = IsSeedCandidate(torrent);
                admitted = seed ? CanStartAnotherSeed() : CanStartAnotherDownload();
                torrent.Status = admitted
                    ? (seed ? DownloadStatus.Seeding : DownloadStatus.Downloading)
                    : DownloadStatus.Queued;
                torrent.ErrorMessage = null;
            }
        });
        if (!admitted)
        {
            await SaveAsync();
            return;
        }

        TorrentManager? manager = null;
        try
        {
            await SaveAsync();
            UpdateBackgroundTransferState();
            ThrowIfNetworkBlocked();
            manager = await GetOrCreateManagerAsync(torrent);
            proxyRebuildToken.ThrowIfCancellationRequested();
            await ApplySpeedLimitsToManagerAsync(manager, torrent);
            proxyRebuildToken.ThrowIfCancellationRequested();

            // Cancel any existing download for this torrent
            if (_downloadTokens.TryRemove(torrent.Id, out var existingCts))
            {
                await existingCts.CancelAsync();
                existingCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _downloadTokens[torrent.Id] = cts;

            // Start real download
            proxyRebuildToken.ThrowIfCancellationRequested();
            ThrowIfNetworkBlocked();
            await StartManagerAsync(manager);
            proxyRebuildToken.ThrowIfCancellationRequested();

            SafeFireAndForget(MonitorTorrentAsync(torrent, manager, cts.Token));
        }
        catch (Exception ex)
        {
            // Shutdown owns manager cleanup and already saved the active state for restore.
            // A start interrupted by engine disposal must not replace it with Failed.
            _disposalToken.ThrowIfCancellationRequested();
            var proxyRebuildWasSuperseded = ex is OperationCanceledException
                && proxyRebuildToken.IsCancellationRequested;

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
                        // Keep fast-resume and cached magnet metadata for the next attempt.
                        await _engine.RemoveAsync(manager, RemoveMode.KeepAllData);
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

            await _dispatcher.InvokeAsync(() =>
            {
                // Disposal may have begun while rollback was stopping the manager or
                // waiting for the UI dispatcher.
                _disposalToken.ThrowIfCancellationRequested();
                UpdateSeedingTime(torrent, active: false);
                torrent.Status = ex is WifiUnavailableException
                    ? DownloadStatus.WaitingForWifi
                    : proxyRebuildWasSuperseded
                    ? DownloadStatus.Queued
                    : DownloadStatus.Failed;
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
                torrent.ErrorMessage = proxyRebuildWasSuperseded || ex is WifiUnavailableException ? null : ex.Message;
            });

            await SaveAsync();
            UpdateBackgroundTransferState();

            if (ex is WifiUnavailableException) return;

            // The download slot this torrent was occupying is now free — let queued
            // torrents take it instead of waiting for the next user action.
            throw;
        }
    }

    private Task WaitForEngineRebuildAsync()
    {
        lock (_engineRebuildStateGate)
        {
            return _activeEngineRebuild?.Task ?? Task.CompletedTask;
        }
    }

    private bool IsEngineRebuildActive()
    {
        lock (_engineRebuildStateGate)
        {
            return _activeEngineRebuild is not null;
        }
    }

    private bool TryEnterStart(out Task? activeRebuild)
    {
        lock (_engineRebuildStateGate)
        {
            if (_activeEngineRebuild is not null)
            {
                activeRebuild = _activeEngineRebuild.Task;
                return false;
            }

            _activeStarts++;
            activeRebuild = null;
            return true;
        }
    }

    private void ExitStart()
    {
        TaskCompletionSource? startsDrained = null;
        lock (_engineRebuildStateGate)
        {
            _activeStarts--;
            if (_activeStarts == 0)
            {
                startsDrained = _activeStartsDrained;
                _activeStartsDrained = null;
            }
        }

        startsDrained?.TrySetResult();
    }

    private TaskCompletionSource BeginEngineRebuild(out Task activeStartsDrained)
    {
        lock (_engineRebuildStateGate)
        {
            if (_activeEngineRebuild is not null)
            {
                _startBarrierHolders++;
                activeStartsDrained = _activeStartsDrained?.Task ?? Task.CompletedTask;
                return _activeEngineRebuild;
            }

            _activeEngineRebuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startBarrierHolders = 1;
            if (_activeStarts == 0)
            {
                activeStartsDrained = Task.CompletedTask;
            }
            else
            {
                _activeStartsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                activeStartsDrained = _activeStartsDrained.Task;
            }

            return _activeEngineRebuild;
        }
    }

    private void EndEngineRebuild(TaskCompletionSource rebuildCompletion)
    {
        var releaseWaiters = false;
        lock (_engineRebuildStateGate)
        {
            if (ReferenceEquals(_activeEngineRebuild, rebuildCompletion))
            {
                _startBarrierHolders--;
                if (_startBarrierHolders == 0)
                {
                    _activeEngineRebuild = null;
                    releaseWaiters = true;
                }
            }
        }

        if (releaseWaiters)
        {
            rebuildCompletion.TrySetResult();
        }
    }

    /// <inheritdoc />
    public async Task PauseTorrentAsync(TorrentItem torrent)
    {
        await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
        {
            // A rebuild temporarily marks an active torrent Queued between teardown and
            // restart. Preserve a Pause click which lands in that narrow window.
            var isQueuedForRebuild = torrent.Status == DownloadStatus.Queued && IsEngineRebuildActive();
            if (!torrent.CanPause && !isQueuedForRebuild)
            {
                return;
            }

            // Cancel the download
            if (_downloadTokens.TryRemove(torrent.Id, out var cts))
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            await _dispatcher.InvokeAsync(() =>
            {
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
            });

            if (_managers.TryGetValue(torrent.Id, out var manager))
            {
                await PauseManagerAsync(manager);
            }

            await _dispatcher.InvokeAsync(() =>
            {
                torrent.Status = DownloadStatus.Paused;
            });

            await SaveAsync();
            UpdateBackgroundTransferState();
        }

        await TryStartQueuedTorrentsAsync();
    }

    /// <inheritdoc />
    public async Task StopTorrentAsync(TorrentItem torrent)
    {
        await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
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

            await _dispatcher.InvokeAsync(() =>
            {
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
            });

            if (_managers.TryGetValue(torrent.Id, out var manager))
            {
                await StopManagerAsync(manager);
            }

            await _dispatcher.InvokeAsync(() =>
            {
                torrent.Status = DownloadStatus.Stopped;
            });

            await SaveAsync();
            UpdateBackgroundTransferState();
        }

        await TryStartQueuedTorrentsAsync();
    }

    /// <inheritdoc />
    public async Task PauseAllForBackgroundTimeoutAsync()
    {
        _backgroundExecutionSuspended = true;

        // Install the start barrier before taking the snapshot. This prevents a queued or
        // user-initiated Start from escaping after the Android foreground-service timeout.
        var pauseBarrier = BeginEngineRebuild(out var activeStartsDrained);
        try
        {
            List<TorrentItem> activeTorrents;
            lock (_torrentsLock)
            {
                activeTorrents = Torrents
                    .Where(torrent => torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding
                        || (torrent.Status == DownloadStatus.Queued && _proxyRebuildPendingResumeIds.ContainsKey(torrent.Id)))
                    .ToList();
            }

            var activeIds = activeTorrents
                .Select(static torrent => torrent.Id)
                .ToHashSet(StringComparer.Ordinal);

            // Persist the requeued intent before any manager call. Android demotes the
            // foreground service synchronously, so the process can be killed while the
            // best-effort network cleanup below is still running. Queued (not Paused) lets
            // the transfers resume on the next foreground resume or launch.
            foreach (var id in activeIds)
            {
                if (_downloadTokens.TryRemove(id, out var cts))
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch
                    {
                        // The persisted queued state remains authoritative.
                    }
                    finally
                    {
                        cts.Dispose();
                    }
                }
            }

            if (activeTorrents.Count > 0)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var torrent in activeTorrents)
                    {
                        if (!ShouldPauseForBackgroundTimeout(torrent))
                        {
                            continue;
                        }

                        torrent.DownloadSpeed = 0;
                        torrent.UploadSpeed = 0;
                        torrent.Status = DownloadStatus.Queued;
                        torrent.ErrorMessage = null;
                    }
                });
                await SaveAsync();
            }
            UpdateBackgroundTransferState();

            // A Start which had already passed the barrier check is allowed to finish,
            // then each backend is paused under its normal per-torrent lock. Run these in
            // parallel so one slow manager does not delay every other torrent.
            await activeStartsDrained.ConfigureAwait(false);

            // Reconcile a Start which entered immediately before the barrier and had not
            // changed its TorrentItem to Downloading when the first snapshot was taken.
            List<TorrentItem> lateActiveTorrents;
            lock (_torrentsLock)
            {
                lateActiveTorrents = Torrents
                    .Where(torrent => torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding
                        || (torrent.Status == DownloadStatus.Queued && _proxyRebuildPendingResumeIds.ContainsKey(torrent.Id)))
                    .ToList();
            }

            if (lateActiveTorrents.Count > 0)
            {
                foreach (var torrent in lateActiveTorrents)
                {
                    activeIds.Add(torrent.Id);
                    if (_downloadTokens.TryRemove(torrent.Id, out var lateCts))
                    {
                        try
                        {
                            lateCts.Cancel();
                        }
                        catch
                        {
                            // best-effort; the manager pause below is authoritative
                        }
                        finally
                        {
                            lateCts.Dispose();
                        }
                    }
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var torrent in lateActiveTorrents)
                    {
                        if (!ShouldPauseForBackgroundTimeout(torrent))
                        {
                            continue;
                        }

                        torrent.DownloadSpeed = 0;
                        torrent.UploadSpeed = 0;
                        torrent.Status = DownloadStatus.Queued;
                        torrent.ErrorMessage = null;
                    }
                });
                await SaveAsync();
                UpdateBackgroundTransferState();
            }

            await Task.WhenAll(activeIds.Select(PauseManagerAfterBackgroundTimeoutAsync)).ConfigureAwait(false);
        }
        finally
        {
            EndEngineRebuild(pauseBarrier);
        }
    }

    /// <inheritdoc />
    public void ResumeAfterBackgroundTimeout()
    {
        _backgroundExecutionSuspended = false;
        // The app is in the foreground: re-request background execution the platform refused
        // earlier (for example a start requested while the app was in the background).
        UpdateBackgroundTransferState(reassert: true);
        if (_networkPolicyReady && !_disposed)
            SafeFireAndForget(ResumeNetworkAndQueueAsync());
    }

    private async Task ResumeNetworkAndQueueAsync()
    {
        await ReconcileNetworkPolicyAsync().ConfigureAwait(false);
        await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
    }

    private bool ShouldPauseForBackgroundTimeout(TorrentItem torrent)
    {
        return TryGetTorrentById(torrent.Id, out var trackedTorrent)
            && ReferenceEquals(torrent, trackedTorrent)
            && (torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding
                || (torrent.Status == DownloadStatus.Queued
                    && _proxyRebuildPendingResumeIds.ContainsKey(torrent.Id)));
    }

    private async Task PauseManagerAfterBackgroundTimeoutAsync(string id)
    {
        await using var operationLock = await _torrentOperationLock.AcquireAsync(id, _disposalToken);

        if (!TryGetTorrentById(id, out var torrent)
            || torrent is null
            || torrent.Status is DownloadStatus.Stopped or DownloadStatus.Completed or DownloadStatus.Failed)
        {
            return;
        }

        if (_downloadTokens.TryRemove(id, out var cts))
        {
            try
            {
                await cts.CancelAsync();
            }
            finally
            {
                cts.Dispose();
            }
        }

        if (!_managers.TryGetValue(id, out var manager))
        {
            return;
        }

        try
        {
            await PauseManagerAsync(manager).WaitAsync(ManagerStopTimeout);
        }
        catch (Exception pauseException)
        {
            System.Diagnostics.Debug.WriteLine($"Background timeout: pause manager error for {id}: {pauseException.Message}");
            try
            {
                await StopManagerAsync(manager).WaitAsync(ManagerStopTimeout);
            }
            catch (Exception stopException)
            {
                // The process-safe paused state was already persisted. Keep this observed;
                // Android may terminate the process now that foreground execution ended.
                System.Diagnostics.Debug.WriteLine($"Background timeout: stop manager error for {id}: {stopException.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<TorrentRemovalResult> RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        TorrentRemovalResult result;
        await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
        {
            if (!IsTracked(torrent)) return TorrentRemovalResult.NotRemoved;

            // Capture the selected manager's file list before engine removal clears its caches.
            _managers.TryGetValue(torrent.Id, out var manager);
            var ownedDownloadFiles = deleteFiles
                ? await ResolveOwnedDownloadFilesAsync(torrent, manager)
                : OwnedDownloadFiles.Empty;

            // Stop monitoring before teardown can turn a last piece into a completion event.
            if (_downloadTokens.TryRemove(torrent.Id, out var cts))
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            var removedFromEngine = false;
            if (manager is not null)
            {
                try
                {
                    // This can wait on tracker/network work. Unrelated imports remain available.
                    await StopManagerAsync(manager);
                    if (manager.Engine is not null)
                    {
                        await RemoveManagerAsync(manager);
                        removedFromEngine = true;
                    }
                    _managers.TryRemove(torrent.Id, out _);
                }
                catch
                {
                    // Retain files, settings handlers and the registered manager for a retry.
                    if (manager.State is TorrentState.Stopped or TorrentState.Error)
                    {
                        await _dispatcher.InvokeAsync(() =>
                        {
                            UpdateSeedingTime(torrent, active: false);
                            torrent.DownloadSpeed = 0;
                            torrent.UploadSpeed = 0;
                            torrent.Status = manager.State == TorrentState.Error
                                ? DownloadStatus.Failed : DownloadStatus.Stopped;
                        });
                    }
                    else
                    {
                        var resumedCts = new CancellationTokenSource();
                        _downloadTokens[torrent.Id] = resumedCts;
                        SafeFireAndForget(MonitorTorrentAsync(torrent, manager, resumedCts.Token));
                    }
                    UpdateBackgroundTransferState();
                    await SaveAsync();
                    throw;
                }
            }

            await _dispatcher.InvokeAsync(() => UpdateSeedingTime(torrent, active: false));
            await _payloadOwnershipLock.WaitAsync(_disposalToken);
            try
            {
                // Publication and deletion share this gate: no import can become an owner
                // between the snapshot of other torrents and deleting the selected files.
                var protectedFiles = deleteFiles || deleteTorrentFile
                    ? await ResolveOtherTorrentFilesAsync(torrent, ownedDownloadFiles, deleteFiles, deleteTorrentFile)
                    : new ProtectedDownloadFiles();
                var torrentFileLeftInPlace = deleteTorrentFile
                    && !await TryDeleteTorrentFileAsync(torrent, protectedFiles);

                var filesLeftInPlace = false;
                if (deleteFiles)
                {
                    filesLeftInPlace = ownedDownloadFiles.Paths.Count == 0
                        ? MayHaveDownloadedData(torrent)
                        : !DeleteOwnedDownloadFiles(ownedDownloadFiles, torrent.TorrentFilePath,
                            deleteTorrentFile && !torrentFileLeftInPlace, protectedFiles);
                }

                if (!removedFromEngine)
                {
                    // Torrents restored after a restart have no manager, so the engine never
                    // removed their fast-resume and magnet metadata cache entries.
                    TryDeleteEngineCacheFiles(torrent);
                }

                result = new TorrentRemovalResult(Removed: true, DownloadedFilesLeftInPlace: filesLeftInPlace,
                    TorrentFileLeftInPlace: torrentFileLeftInPlace);
                DetachTorrentSettingsHandlers(torrent);
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_torrentsLock) Torrents.Remove(torrent);
                });
            }
            finally { _payloadOwnershipLock.Release(); }

            await SaveAsync();
            UpdateBackgroundTransferState();
            // Imports acquire cache locks before publication; do not invert that order.
            await TryDeleteCachedTorrentFileAsync(torrent);
        }

        await TryStartQueuedTorrentsAsync();
        return result;
    }

    protected virtual Task RemoveManagerAsync(TorrentManager manager)
        => manager.Engine is { } engine ? engine.RemoveAsync(manager) : Task.CompletedTask;

    // A torrent which never received metadata or data has nothing on disk to delete.
    private static bool MayHaveDownloadedData(TorrentItem torrent)
        => torrent.Progress > 0 || torrent.DownloadedSize > 0 || torrent.DateCompleted is not null;

    private EngineSettings GetEngineCacheSettings()
        => _engine?.Settings ?? new EngineSettingsBuilder { CacheDirectory = EngineCacheDirectory }.ToSettings();

    private static bool TryGetInfoHashes(TorrentItem torrent, out InfoHashes infoHashes)
    {
        infoHashes = null!;
        try
        {
            if (!string.IsNullOrWhiteSpace(torrent.MagnetLink))
            {
                infoHashes = MagnetLink.Parse(torrent.MagnetLink).InfoHashes;
                return true;
            }

            var hex = torrent.InfoHash?.Trim();
            if (hex is { Length: 40 })
            {
                infoHashes = InfoHashes.FromV1(InfoHash.FromHex(hex));
                return true;
            }

            if (hex is { Length: 64 })
            {
                infoHashes = InfoHashes.FromV2(InfoHash.FromHex(hex));
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not resolve info hashes for '{torrent.Name}': {ex.Message}");
        }

        return false;
    }

    // Mirrors MonoTorrent 3.0.2's EngineSettings.GetMetadataPath/GetV2HashesPath, which are internal.
    private static string GetEngineMetadataCachePath(EngineSettings settings, InfoHashes infoHashes)
        => Path.Combine(settings.MetadataCacheDirectory, $"{infoHashes.V1OrV2.ToHex()}.torrent");

    private string? FindEngineMetadataCachePath(TorrentItem torrent)
    {
        if (!TryGetInfoHashes(torrent, out var infoHashes))
        {
            return null;
        }

        try
        {
            var path = GetEngineMetadataCachePath(GetEngineCacheSettings(), infoHashes);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Engine metadata cache lookup failed for '{torrent.Name}': {ex.Message}");
            return null;
        }
    }

    private void TryDeleteEngineCacheFiles(TorrentItem torrent)
    {
        if (!TryGetInfoHashes(torrent, out var infoHashes))
        {
            return;
        }

        try
        {
            var settings = GetEngineCacheSettings();
            var paths = new List<string>
            {
                settings.GetFastResumePath(infoHashes),
                GetEngineMetadataCachePath(settings, infoHashes)
            };
            if (infoHashes.V2 is { } v2)
            {
                paths.Add(Path.Combine(settings.MetadataCacheDirectory, $"{v2.ToHex()}.v2hashes"));
            }

            foreach (var candidate in paths)
            {
                if (TryGetSafeOwnedFilePath(candidate, settings.CacheDirectory, out var path)
                    && File.Exists(path)
                    && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Engine cache cleanup failed for '{torrent.Name}': {ex.Message}");
        }
    }

    private async Task TryDeleteCachedTorrentFileAsync(TorrentItem torrent)
    {
        try
        {
            var appDataPath = _storageService.GetAppDataPath();
            var expectedPath = TorrentImportService.GetCachePath(appDataPath, torrent.InfoHash);
            await using var cacheLock = await TorrentImportService.LockCacheAsync(expectedPath, _disposalToken);
            if (!PathsEqual(torrent.CachedTorrentFilePath, expectedPath)
                || !TryGetSafeOwnedFilePath(expectedPath, appDataPath, out var path)) return;

            lock (_torrentsLock)
            {
                if (Torrents.Any(t => PathsEqual(t.CachedTorrentFilePath, path))) return;
            }
            if (File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cached metadata cleanup failed: {ex.Message}");
        }
    }

    protected virtual async Task<OwnedDownloadFiles> ResolveOwnedDownloadFilesAsync(TorrentItem torrent, TorrentManager? manager)
    {
        var snapshot = SnapshotManagerOwnedFiles(torrent, manager);
        if (snapshot.Paths.Count > 0) return snapshot;

        var metadataPath = GetMetadataPath(torrent) ?? FindEngineMetadataCachePath(torrent);
        if (string.IsNullOrWhiteSpace(torrent.SavePath) || metadataPath is null)
            return OwnedDownloadFiles.Empty;

        try
        {
            var metadata = await LoadTorrentFileBoundedAsync(metadataPath, torrent);
            var basePath = Path.GetFullPath(torrent.SavePath);
            var containingDirectory = metadata.Files.Count == 1
                ? basePath
                : Path.Combine(basePath, EscapeTorrentPath(metadata.Name));

            var paths = new HashSet<string>(GetPathComparer());
            foreach (var file in metadata.Files)
                paths.Add(Path.Combine(containingDirectory, EscapeTorrentFilePath(file.Path)));

            return new OwnedDownloadFiles(basePath, paths.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load download ownership metadata for '{torrent.Name}': {ex.Message}");
            return OwnedDownloadFiles.Empty;
        }
    }

    private static OwnedDownloadFiles SnapshotManagerOwnedFiles(TorrentItem torrent, TorrentManager? manager)
    {
        if (manager is { HasMetadata: true }
            && manager.Files.Count > 0
            && TorrentIdentityMatches(torrent, manager.InfoHashes))
        {
            try
            {
                return new OwnedDownloadFiles(
                    manager.SavePath,
                    manager.Files
                        .SelectMany(static file => new[]
                        {
                            file.FullPath,
                            file.DownloadCompleteFullPath,
                            file.DownloadIncompleteFullPath
                        })
                        .Where(static path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(GetPathComparer())
                        .ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not snapshot manager file ownership for '{torrent.Name}': {ex.Message}");
            }
        }

        return OwnedDownloadFiles.Empty;
    }

    private async Task<ProtectedDownloadFiles> ResolveOtherTorrentFilesAsync(TorrentItem removedTorrent,
        OwnedDownloadFiles removedFiles, bool deleteFiles, bool deleteTorrentFile)
    {
        TorrentItem[] others;
        lock (_torrentsLock) others = Torrents.Where(t => t.Id != removedTorrent.Id).ToArray();
        var protectedFiles = new ProtectedDownloadFiles();
        var candidates = removedFiles.Paths.ToList();
        if (deleteTorrentFile && TryGetLocalTorrentPath(removedTorrent.TorrentFilePath) is { } source)
            candidates.Add(source);
        foreach (var other in others)
        {
            foreach (var path in new[] { other.TorrentFilePath, other.CachedTorrentFilePath })
            {
                if (TryGetLocalTorrentPath(path) is { } localPath) protectedFiles.Paths.Add(localPath);
            }
            _managers.TryGetValue(other.Id, out var manager);
            var basePath = TryGetLocalTorrentPath(manager?.SavePath ?? other.SavePath);
            if (basePath is null || !candidates.Any(path => PathGuard.IsPathWithinDirectory(path, basePath)))
                continue;

            // Source-only removal uses manager file lists without reading metadata. When
            // only persisted metadata or progress is available, protect the save directory.
            if (!deleteFiles)
            {
                var snapshot = SnapshotManagerOwnedFiles(other, manager);
                foreach (var path in snapshot.Paths)
                {
                    if (TryGetLocalTorrentPath(path) is { } localPath) protectedFiles.Paths.Add(localPath);
                }
                if (snapshot.Paths.Count == 0 && (MayHaveDownloadedData(other) || GetMetadataPath(other) is not null))
                    protectedFiles.Directories.Add(basePath);
                continue;
            }
            var owned = await ResolveOwnedDownloadFilesAsync(other, manager);
            foreach (var path in owned.Paths)
            {
                if (TryGetLocalTorrentPath(path) is { } localPath) protectedFiles.Paths.Add(localPath);
            }
            // An empty magnet has no files to protect; one with progress may have lost metadata.
            if (owned.Paths.Count == 0 && MayHaveDownloadedData(other))
                protectedFiles.Directories.Add(basePath);
        }
        return protectedFiles;
    }

    private static string? TryGetLocalTorrentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = path.Trim();
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) return null;
                path = uri.LocalPath;
            }
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Ignoring invalid torrent path: {ex.Message}");
            return null;
        }
    }

    private sealed class ProtectedDownloadFiles
    {
        public HashSet<string> Paths { get; } = new(GetPathComparer());
        public List<string> Directories { get; } = [];
        public bool Contains(string path) => Paths.Contains(Path.GetFullPath(path))
            || Directories.Any(directory => PathGuard.IsPathWithinDirectory(path, directory));
    }

    /// <returns><see langword="false"/> when an owned file remains on disk.</returns>
    private static bool DeleteOwnedDownloadFiles(OwnedDownloadFiles ownedFiles, string? torrentFilePath,
        bool deleteTorrentFile, ProtectedDownloadFiles protectedFiles)
    {
        if (string.IsNullOrWhiteSpace(ownedFiles.BaseDirectory) || ownedFiles.Paths.Count == 0)
        {
            return true;
        }

        var allDeleted = true;
        try
        {
            var basePath = Path.GetFullPath(ownedFiles.BaseDirectory);
            var protectedTorrentPath = TryGetLocalTorrentPath(torrentFilePath);

            foreach (var candidate in ownedFiles.Paths)
            {
                if (!TryGetSafeOwnedFilePath(candidate, basePath, out var fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping unsafe owned-file path '{candidate}'.");
                    allDeleted &= !File.Exists(candidate);
                    continue;
                }

                if (!deleteTorrentFile && PathsEqual(fullPath, protectedTorrentPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                if (protectedFiles.Contains(fullPath))
                {
                    allDeleted = false;
                    continue;
                }

                try
                {
                    var attributes = File.GetAttributes(fullPath);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping reparse-point payload '{fullPath}'.");
                        allDeleted = false;
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
                    }

                    File.Delete(fullPath);
                    PruneEmptyOwnedDirectories(Path.GetDirectoryName(fullPath), basePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting owned payload '{fullPath}': {ex.Message}");
                    allDeleted = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error resolving owned download paths: {ex.Message}");
            return false;
        }

        return allDeleted;
    }

    private static async Task<MonoTorrent.Torrent> LoadTorrentFileBoundedAsync(
        string torrentFilePath,
        TorrentItem expectedTorrent)
    {
        var content = await TorrentFileContentReader.ReadFromFileAsync(torrentFilePath);

        // MonoTorrent's loader is still needed for its canonical payload path mapping, but
        // first run the bounded decoder so legacy or externally replaced metadata cannot
        // bypass the import parser's depth/node/container limits.
        _ = new TorrentFileParser().Parse(content);
        var metadata = await MonoTorrent.Torrent.LoadAsync(content.AsMemory());
        if (!TorrentIdentityMatches(expectedTorrent, metadata.InfoHashes))
        {
            throw new InvalidDataException(
                $"The .torrent metadata no longer matches torrent '{expectedTorrent.Name}'.");
        }

        return metadata;
    }

    private static bool TorrentIdentityMatches(TorrentItem torrent, InfoHashes actualInfoHashes)
    {
        var expectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            expectedHashes.Add(torrent.InfoHash.Trim());
        }

        if (!string.IsNullOrWhiteSpace(torrent.MagnetLink))
        {
            try
            {
                var magnet = MagnetLink.Parse(torrent.MagnetLink);
                if (magnet.InfoHashes.V1 is { } v1)
                {
                    expectedHashes.Add(v1.ToHex());
                }
                if (magnet.InfoHashes.V2 is { } v2)
                {
                    expectedHashes.Add(v2.ToHex());
                }
            }
            catch
            {
                // The stored hexadecimal identity can still be authoritative.
            }
        }

        if (expectedHashes.Count == 0)
        {
            return false;
        }

        return (actualInfoHashes.V1 is { } actualV1 && expectedHashes.Contains(actualV1.ToHex()))
            || (actualInfoHashes.V2 is { } actualV2 && expectedHashes.Contains(actualV2.ToHex()));
    }

    private static bool TryGetSafeOwnedFilePath(string candidate, string basePath, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            fullPath = Path.GetFullPath(candidate);
            return PathGuard.IsPathWithinDirectory(fullPath, basePath)
                && !HasReparsePointInPath(basePath, Path.GetDirectoryName(fullPath));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasReparsePointInPath(string basePath, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return true;
        }

        var fullBase = Path.GetFullPath(basePath);
        var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (PathsEqual(current.FullName, fullBase))
            {
                return false;
            }

            if (!PathGuard.IsPathWithinDirectory(current.FullName, fullBase))
            {
                return true;
            }

            current = current.Parent;
        }

        return true;
    }

    private static void PruneEmptyOwnedDirectories(string? directoryPath, string basePath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var current = new DirectoryInfo(directoryPath);
        while (current.Exists
            && !PathsEqual(current.FullName, basePath)
            && PathGuard.IsPathWithinDirectory(current.FullName, basePath))
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || current.EnumerateFileSystemInfos().Any())
            {
                return;
            }

            var parent = current.Parent;
            current.Delete(recursive: false);
            if (parent is null)
            {
                return;
            }

            current = parent;
        }
    }

    // These two routines intentionally mirror MonoTorrent 3.0.2's internal
    // TorrentFileInfo path mapping so metadata-only removal targets the same files.
    private static string EscapeTorrentPath(string path)
    {
        foreach (var invalidCharacter in Path.GetInvalidPathChars())
        {
            path = path.Replace(invalidCharacter.ToString(), Convert.ToString(invalidCharacter, 16));
        }

        return path;
    }

    private static string EscapeTorrentFilePath(string path)
    {
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separatorIndex = path.LastIndexOf(Path.DirectorySeparatorChar);
        var directory = separatorIndex < 0 ? string.Empty : path[..separatorIndex];
        var fileName = separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
        directory = EscapeTorrentPath(directory);

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter.ToString(), $"_{Convert.ToString(invalidCharacter, 16)}_");
        }

        return Path.Combine(directory, fileName);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool PathsEqual(string? left, string? right)
        => TryGetLocalTorrentPath(left) is { } leftPath
            && TryGetLocalTorrentPath(right) is { } rightPath
            && GetPathComparer().Equals(leftPath, rightPath);

    protected sealed record OwnedDownloadFiles(string BaseDirectory, IReadOnlyCollection<string> Paths)
    {
        public static OwnedDownloadFiles Empty { get; } = new(string.Empty, Array.Empty<string>());
    }

    private static async Task<bool> TryDeleteTorrentFileAsync(TorrentItem torrent, ProtectedDownloadFiles protectedFiles)
    {
        try
        {
            // Only the exact source path is known to be the imported metadata file.
            // TorrentFileName + SavePath is merely a guess and can name unrelated data.
            if (string.IsNullOrWhiteSpace(torrent.TorrentFilePath))
            {
                return true;
            }

            var fullPath = TryGetLocalTorrentPath(torrent.TorrentFilePath);
            if (fullPath is null) return false;
            if (!fullPath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                return !File.Exists(fullPath);
            }

            var attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            if (protectedFiles.Contains(fullPath)) return false;
            // The source path may have been reused since import. Only delete matching metadata.
            await LoadTorrentFileBoundedAsync(fullPath, torrent);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(fullPath);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting .torrent file: {ex.Message}");
            return false;
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
        lock (_torrentsLock)
        {
            _maxActiveDownloads = Math.Max(0, maxActiveDownloads);
            _maxActiveSeeds = Math.Max(0, maxActiveSeeds);
        }

        SafeFireAndForget(ReconcileQueueLimitsAsync());
    }

    /// <inheritdoc />
    public void UpdateSeedingLimits(double maxSeedRatio, int maxSeedMinutes)
    {
        _globalMaxSeedRatio = SeedRatioLimits.Normalize(maxSeedRatio);
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
    public void UpdateKeepDeviceAwake(bool enabled)
    {
        _backgroundDownloadService.SetKeepDeviceAwake(enabled);
        lock (_torrentsLock)
        {
            _keepDeviceAwake = enabled;
            UpdateSleepPreventionState();
        }
    }

    private void UpdateSleepPreventionState()
    {
        lock (_torrentsLock)
        {
            if (_disposed) return;
            _sleepPreventionService?.SetPreventSleep(_keepDeviceAwake
                && Torrents.Any(t => t.Status == DownloadStatus.Downloading && t.Progress < 100));
        }
    }

    /// <inheritdoc />
    public void UpdateProxySettings(bool enabled, string host, int port, string username, string password)
    {
        var newHost = NormalizeProxyHost(host);
        var newPort = port is > 0 and <= 65535 ? port : 1080;
        var newUsername = username ?? string.Empty;
        var newPassword = password ?? string.Empty;

        var proxyModeChanged = _proxyEnabled != enabled;
        var changed = proxyModeChanged || (enabled
                      && (!string.Equals(_proxyHost, newHost, StringComparison.Ordinal)
                      || _proxyPort != newPort
                      || !string.Equals(_proxyUsername, newUsername, StringComparison.Ordinal)
                      || !string.Equals(_proxyPassword, newPassword, StringComparison.Ordinal)));

        TaskCompletionSource? rebuildReservation = null;
        Task? activeStartsDrained = null;
        var rebuildToken = CancellationToken.None;
        if (changed)
        {
            // Reserve the start barrier before publishing the new configuration. If this
            // update supersedes an in-flight rebuild, both holders share the same barrier,
            // so no external Start can escape during the handoff.
            rebuildReservation = BeginEngineRebuild(out activeStartsDrained);
            rebuildToken = ReplaceProxyRebuildToken();
        }

        _proxyEnabled = enabled;
        _proxyHost = newHost;
        _proxyPort = newPort;
        _proxyUsername = newUsername;
        _proxyPassword = newPassword;

        System.Diagnostics.Debug.WriteLine(_proxyEnabled
            ? $"Proxy settings updated: {_proxyHost}:{_proxyPort}"
            : "Proxy disabled");

        if (changed)
        {
            ScheduleEngineRebuild(
                rebuildToken,
                immediate: proxyModeChanged,
                rebuildReservation!,
                activeStartsDrained!);
        }
    }

    /// <summary>
    /// Debounces proxy-triggered engine rebuilds: each call cancels the previously scheduled
    /// rebuild and restarts the timer, so a burst of setting changes (e.g. typing a hostname
    /// one character at a time) collapses into a single rebuild once the user stops.
    /// </summary>
    private void ScheduleEngineRebuild(
        CancellationToken token,
        bool immediate,
        TaskCompletionSource rebuildReservation,
        Task activeStartsDrained)
    {
        SafeFireAndForget(DebouncedEngineRebuildAsync(
            token,
            immediate,
            rebuildReservation,
            activeStartsDrained));
    }

    private CancellationToken ReplaceProxyRebuildToken()
    {
        lock (_proxyRebuildGate)
        {
            _proxyRebuildCts?.Cancel();
            _proxyRebuildCts?.Dispose();
            _proxyRebuildCts = new CancellationTokenSource();
            return _proxyRebuildCts.Token;
        }
    }

    private async Task DebouncedEngineRebuildAsync(
        CancellationToken token,
        bool immediate,
        TaskCompletionSource rebuildReservation,
        Task activeStartsDrained)
    {
        var rebuildLockTaken = false;
        try
        {
            if (!immediate)
            {
                await Task.Delay(ProxyRebuildDebounce, token).ConfigureAwait(false);
            }

            // Cancellation remains meaningful after the debounce. A newer proxy update
            // must obsolete a rebuild which is waiting behind another rebuild as well.
            await _engineRebuildLock.WaitAsync(token).ConfigureAwait(false);
            rebuildLockTaken = true;

            // Serialize rebuilds: a rebuild stops every manager, disposes the engine, and
            // restarts torrents, which takes seconds. A second rebuild starting meanwhile
            // would race on _engine and double-stop the same managers.
            if (_disposed || token.IsCancellationRequested)
            {
                return;
            }

            await RebuildEngineCoreAsync(token, activeStartsDrained).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer reservation keeps the shared start barrier active.
        }
        finally
        {
            try
            {
                if (rebuildLockTaken)
                {
                    _engineRebuildLock.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Disposed during shutdown while this rebuild still held the lock.
            }
            finally
            {
                EndEngineRebuild(rebuildReservation);
            }
        }

        if (!_disposed && !token.IsCancellationRequested && !NetworkBlocked)
            await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Tears down the current engine and its managers so that a fresh engine is created
    /// on the next torrent start (picking up the new proxy settings). Torrents that were
    /// actively downloading or seeding are restarted automatically.
    /// </summary>
    private async Task RebuildEngineAsync()
    {
        var rebuildCompletion = BeginEngineRebuild(out var activeStartsDrained);
        try
        {
            await RebuildEngineCoreAsync(CancellationToken.None, activeStartsDrained)
                .ConfigureAwait(false);
        }
        finally
        {
            EndEngineRebuild(rebuildCompletion);
        }
    }

    private async Task RebuildEngineCoreAsync(
        CancellationToken proxyRebuildToken,
        Task activeStartsDrained)
    {
        // Starts which passed the second gate check before this rebuild was reserved are
        // allowed to finish. All later starts remain parked across superseded handoffs.
        await activeStartsDrained.ConfigureAwait(false);

        var pendingResumeAtSnapshot = _proxyRebuildPendingResumeIds.Keys
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> activeIdsAtSnapshot;
        lock (_torrentsLock)
        {
            activeIdsAtSnapshot = Torrents
                .Where(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Seeding)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        // Detach the old engine first. Any concurrent start will create a new engine with
        // the new proxy settings instead of registering another manager with the engine
        // being torn down.
        ClientEngine? engineToDispose;
        await _engineLock.WaitAsync(_disposalToken).ConfigureAwait(false);
        try
        {
            engineToDispose = _engine;
            _engine = null;
        }
        finally
        {
            try
            {
                _engineLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed during shutdown while this rebuild still held the lock.
            }
        }

        var managersToRemove = _managers.ToArray();
        var idsToTearDown = managersToRemove.Select(static entry => entry.Key)
            .Concat(_downloadTokens.Keys)
            .Concat(activeIdsAtSnapshot)
            .Concat(pendingResumeAtSnapshot)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var idsPendingResume = pendingResumeAtSnapshot;

        // Independent managers stop in parallel so slow tracker announces cost one stop
        // timeout, not one per torrent. Keep each torrent's operation lock: a manual state
        // change which wins that lock first is still observed and is not overwritten.
        await Task.WhenAll(idsToTearDown.Select(async id =>
        {
            await using var operationLock = await _torrentOperationLock.AcquireAsync(id, _disposalToken);

            if (_downloadTokens.TryRemove(id, out var cts))
            {
                try
                {
                    await cts.CancelAsync();
                    cts.Dispose();
                }
                catch
                {
                    // best-effort monitor cancellation
                }
            }

            if (_managers.TryGetValue(id, out var currentManager)
                && managersToRemove.Any(entry => entry.Key == id && ReferenceEquals(entry.Value, currentManager))
                && _managers.TryRemove(id, out var manager))
            {
                try
                {
                    await StopManagerAsync(manager);
                    if (engineToDispose is not null)
                    {
                        // The default RemoveMode deletes the fast-resume data StopAsync just
                        // wrote, forcing a full re-hash (and magnet metadata re-fetch) on resume.
                        await engineToDispose.RemoveAsync(manager, RemoveMode.KeepAllData);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Proxy rebuild: stop manager error for {id}: {ex.Message}");
                }
            }

            if (activeIdsAtSnapshot.Contains(id)
                && TryGetTorrentById(id, out var torrent)
                && torrent is not null
                && torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    // While background execution is suspended the item stays Queued for the
                    // foreground resume instead of being restarted by this rebuild.
                    torrent.Status = DownloadStatus.Queued;
                    torrent.DownloadSpeed = 0;
                    torrent.UploadSpeed = 0;
                });
                _pendingSave = true;
                if (!_backgroundExecutionSuspended)
                {
                    lock (idsPendingResume)
                        idsPendingResume.Add(id);
                    _proxyRebuildPendingResumeIds[id] = 0;
                }
            }
        })).ConfigureAwait(false);

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

        // Re-check membership and state while holding the same per-torrent operation lock.
        // Stop/Remove which happened after the initial snapshot therefore wins and cannot
        // be undone by an unconditional restart.
        foreach (var id in idsPendingResume)
        {
            if (proxyRebuildToken.IsCancellationRequested)
            {
                break;
            }

            await using var operationLock = await _torrentOperationLock.AcquireAsync(id, _disposalToken);

            if (proxyRebuildToken.IsCancellationRequested)
            {
                break;
            }

            if (!TryGetTorrentById(id, out var torrent) || torrent is null)
            {
                _proxyRebuildPendingResumeIds.TryRemove(id, out _);
                continue;
            }

            var keepPendingForNewerRebuild = false;
            try
            {
                // A suspended app leaves the item Queued; ResumeAfterBackgroundTimeout (or the
                // next launch) restarts it once the app is allowed to run transfers again.
                if (!_backgroundExecutionSuspended && torrent.Status == DownloadStatus.Queued)
                {
                    await StartTorrentCoreAsync(torrent, proxyRebuildToken);
                    proxyRebuildToken.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException) when (proxyRebuildToken.IsCancellationRequested)
            {
                // A newer configuration owns the resume. Leave this and every remaining
                // ID in the shared pending set so its rebuild can restart them safely.
                keepPendingForNewerRebuild = true;
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Proxy rebuild: restart error for '{torrent.Name}': {ex.Message}");
            }
            finally
            {
                if (!keepPendingForNewerRebuild)
                {
                    _proxyRebuildPendingResumeIds.TryRemove(id, out _);
                }
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

    /// <param name="reassert">
    /// Request background execution again even if it was requested before. The platform may
    /// have refused it (Android refuses foreground-service starts from the background) or
    /// stopped the service since. Only the foreground resume path passes this: repeating the
    /// request from the background would be refused again.
    /// </param>
    private void UpdateBackgroundTransferState(bool reassert = false)
    {
        if (_disposed) return;
        UpdateSleepPreventionState();
        bool hasActiveTransfers;
        lock (_torrentsLock)
        {
            hasActiveTransfers = Torrents.Any(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Seeding);
        }

        // Serialized so concurrent callers cannot interleave Start/Stop out of order.
        lock (_backgroundStateGate)
        {
            if (_disposed) return;
            if (!hasActiveTransfers)
            {
                _backgroundStartRefused = false;
                if (!_backgroundTransferActive) return;
                _backgroundTransferActive = false;
                _backgroundDownloadService.Stop();
                return;
            }

            if (!reassert && (_backgroundTransferActive || _backgroundStartRefused))
            {
                return;
            }

            _backgroundTransferActive = _backgroundDownloadService.Start();
            _backgroundStartRefused = !_backgroundTransferActive;
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

        if (e.PropertyName is nameof(TorrentItem.Status) or nameof(TorrentItem.Progress))
            UpdateSleepPreventionState();

        if (e.PropertyName == nameof(TorrentItem.Status) && torrent.Status != DownloadStatus.Seeding)
            UpdateSeedingTime(torrent, active: false);

        if (e.PropertyName is nameof(TorrentItem.DownloadLimitKbps) or nameof(TorrentItem.UploadLimitKbps))
        {
            SafeFireAndForget(UpdateTorrentManagerSettingsAsync(torrent));
        }

        if (e.PropertyName is nameof(TorrentItem.TorrentFilePath) or nameof(TorrentItem.CachedTorrentFilePath)
            or nameof(TorrentItem.TorrentFileName) or nameof(TorrentItem.SavePath)
            or nameof(TorrentItem.ResolvedDownloadPath)
            or nameof(TorrentItem.DownloadLimitKbps) or nameof(TorrentItem.UploadLimitKbps)
            or nameof(TorrentItem.MaxSeedRatio) or nameof(TorrentItem.MaxSeedMinutes))
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

    private static bool IsSeedCandidate(TorrentItem torrent) => torrent.Progress >= 100;

    private void UpdateSeedingTime(TorrentItem torrent, bool active)
    {
        lock (_seedingSessions)
        {
            var now = _timeProvider.GetTimestamp();
            if (_seedingSessions.Remove(torrent.Id, out var previous))
            {
                torrent.SeededSeconds += Math.Max(0, _timeProvider.GetElapsedTime(previous, now).TotalSeconds);
                // Every monitor tick of a seeding torrent adds time: that is progress, saved with
                // ProgressSaveInterval. The final update when seeding ends is saved promptly.
                if (active)
                    _pendingProgressSave = true;
                else
                    _pendingSave = true;
            }

            if (active)
            {
                _seedingSessions[torrent.Id] = now;
                torrent.DateSeedingStarted ??= _timeProvider.GetLocalNow().DateTime;
            }
            else
                torrent.DateSeedingStarted = null;
        }
    }

    private double GetSeededSeconds(TorrentItem torrent)
    {
        lock (_seedingSessions)
            return torrent.SeededSeconds + (_seedingSessions.TryGetValue(torrent.Id, out var start)
                ? Math.Max(0, _timeProvider.GetElapsedTime(start).TotalSeconds) : 0);
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
        if (_disposed || _backgroundExecutionSuspended) return;

        List<TorrentItem> queued;
        lock (_torrentsLock)
        {
            queued = Torrents
                .Where(t => t.Status == DownloadStatus.Queued)
                .OrderBy(t => t.DateAdded)
                .ToList();
        }

        foreach (var torrent in queued)
        {
            if (IsSeedCandidate(torrent) ? !CanStartAnotherSeed() : !CanStartAnotherDownload())
                continue;
            try
            {
                await StartTorrentIfStatusAsync(torrent, DownloadStatus.Queued);
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

        if (await QueueIfOverCapacityAsync(torrent))
        {
            await TryStartQueuedTorrentsAsync();
            return;
        }
        if (torrent.Status != DownloadStatus.Seeding) return;

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

        if (maxMinutes > 0 && GetSeededSeconds(torrent) >= maxMinutes * 60d)
        {
            await PauseTorrentAsync(torrent);
        }
    }

    private async Task ReconcileQueueLimitsAsync()
    {
        List<TorrentItem> active;
        lock (_torrentsLock)
            active = Torrents.Where(t => t.Status is DownloadStatus.Downloading or DownloadStatus.Seeding).ToList();

        foreach (var torrent in active)
            await QueueIfOverCapacityAsync(torrent);

        await TryStartQueuedTorrentsAsync();
    }

    private async Task<bool> QueueIfOverCapacityAsync(TorrentItem torrent)
    {
        lock (_torrentsLock)
            if (!IsOverCapacity(torrent)) return false;

        await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken);
        var queued = false;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_torrentsLock)
            {
                if (!IsOverCapacity(torrent)) return;

                if (_downloadTokens.TryRemove(torrent.Id, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                torrent.Status = DownloadStatus.Queued;
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
                torrent.EstimatedSecondsRemaining = 0;
                queued = true;
            }
        });
        if (!queued) return false;

        if (_managers.TryGetValue(torrent.Id, out var manager))
            await StopManagerAsync(manager);
        await SaveAsync();
        return true;
    }

    // Called under _torrentsLock. Count predecessors instead of sorting every seed on every tick.
    private bool IsOverCapacity(TorrentItem torrent)
    {
        var limit = torrent.Status switch
        {
            DownloadStatus.Downloading => _maxActiveDownloads,
            DownloadStatus.Seeding => _maxActiveSeeds,
            _ => 0
        };
        if (limit <= 0 || !Torrents.Contains(torrent)) return false;

        var predecessors = 0;
        foreach (var candidate in Torrents)
        {
            if (candidate.Status != torrent.Status || ReferenceEquals(candidate, torrent)) continue;
            if (candidate.DateAdded < torrent.DateAdded
                || (candidate.DateAdded == torrent.DateAdded && StringComparer.Ordinal.Compare(candidate.Id, torrent.Id) < 0))
                if (++predecessors >= limit) return true;
        }
        return false;
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
        _downloadTokens.TryGetValue(torrent.Id, out var monitorCts);
        try
        {
            long previousDataBytesSent = manager.Monitor.DataBytesSent;
            var nextTrackerScrape = DateTimeOffset.MinValue;
            Task trackerScrapeTask = Task.CompletedTask;

            while (!cancellationToken.IsCancellationRequested)
            {
                // ConfigureAwait(false) keeps the loop body off the UI thread. The only
                // work that must touch the UI thread is the explicit
                // dispatcher block below; everything else
                // (reflection, peer/piece scanning) runs on the thread pool.
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                // Tracker scrape results are the only swarm-wide source for seed/peer counts.
                // Refresh them in the background so a slow tracker never stalls the monitor loop.
                if (trackerScrapeTask.IsCompleted && DateTimeOffset.UtcNow >= nextTrackerScrape)
                {
                    trackerScrapeTask = RefreshTrackerScrapeAsync(manager, cancellationToken);
                    nextTrackerScrape = DateTimeOffset.UtcNow.AddMinutes(30);
                }

                // ---- Collect all data on the background thread ----
                var metadataSize = manager.Torrent?.Size;
                var progress = manager.Progress;
                var previousStatus = torrent.Status;
                var alreadyCompleted = torrent.DateCompleted is not null;
                var currentDataBytesSent = manager.Monitor.DataBytesSent;
                var uploadedDelta = currentDataBytesSent - previousDataBytesSent;
                previousDataBytesSent = currentDataBytesSent;

                var downloadRate = manager.Monitor.DownloadRate;
                var uploadRate = manager.Monitor.UploadRate;
                var managerState = manager.State;
                var errorMessage = managerState == TorrentState.Error
                    ? (manager.Error?.Exception?.Message ?? "Unknown error occurred")
                    : null;

                // Availability is piece coverage among connected peers, not a restatement of
                // the seed/leecher counts. Partial peers can collectively provide 100% even
                // when no complete seeder is currently connected.
                var connectedPeers = await manager.GetPeersAsync().ConfigureAwait(false);
                var connectedSeeds = connectedPeers.Count(peer => peer.IsSeeder);
                var connectedLeeches = connectedPeers.Count - connectedSeeds;

                // Prefer tracker scrape totals, which describe the whole swarm. If a tracker
                // cannot scrape, fall back to the connected peers inspected above.
                var (seeds, leeches) = GetSwarmPeerCounts(manager, connectedSeeds, connectedLeeches);

                var availabilityInfo = GetAvailabilityInfo(
                    connectedPeers.Select(peer => peer.BitField),
                    manager.HasMetadata ? manager.Bitfield.Length : 0);
                var healthScore = ComputeHealthScore(seeds, leeches, availabilityInfo.Percent);

                // Metadata from torrent file (if available)
                var torrentSize = (manager.HasMetadata && manager.Torrent != null) ? manager.Torrent.Size : (long?)null;
                var torrentName = (manager.HasMetadata && manager.Torrent != null) ? manager.Torrent.Name : null;
                var resolvedDownloadPath = GetResolvedDownloadPath(manager);

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
                var queueForCapacity = false;
                await _dispatcher.InvokeAsync(() =>
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
                    if (resolvedDownloadPath is not null)
                        torrent.ResolvedDownloadPath = resolvedDownloadPath;

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

                    lock (_torrentsLock)
                    {
                        queueForCapacity = (mappedStatus == DownloadStatus.Seeding && torrent.Status != DownloadStatus.Seeding && !CanStartAnotherSeed())
                            || (mappedStatus == DownloadStatus.Downloading && torrent.Status != DownloadStatus.Downloading && !CanStartAnotherDownload());
                        torrent.Status = queueForCapacity ? DownloadStatus.Queued : mappedStatus;
                    }
                    torrent.ErrorMessage = errorMessage;

                    UpdateSeedingTime(torrent, active: !queueForCapacity && managerState == TorrentState.Seeding);

                    if (managerState == TorrentState.Seeding || (managerState == TorrentState.Stopped && progress >= 100))
                    {
                        torrent.DateCompleted ??= _timeProvider.GetLocalNow().DateTime;
                    }
                    else if (managerState == TorrentState.Downloading && progress < 100)
                    {
                        torrent.DateCompleted = null;
                    }

                    currentStatus = torrent.Status;
                }).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                if (queueForCapacity)
                {
                    await using (await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await StopManagerAsync(manager).ConfigureAwait(false);
                        await _dispatcher.InvokeAsync(() => { torrent.DownloadSpeed = 0; torrent.UploadSpeed = 0; });
                        await SaveAsync().ConfigureAwait(false);
                    }
                    await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
                    if (!alreadyCompleted && torrent.DateCompleted is not null)
                        await NotifyCompletionSafelyAsync(torrent).ConfigureAwait(false);
                    break;
                }

                if (previousStatus != currentStatus)
                {
                    UpdateBackgroundTransferState();
                }

                if (!alreadyCompleted && torrent.DateCompleted is not null)
                {
                    await NotifyCompletionSafelyAsync(torrent).ConfigureAwait(false);
                }

                if (previousStatus != currentStatus || (!alreadyCompleted && torrent.DateCompleted is not null))
                    _pendingSave = true;
                else
                    _pendingProgressSave = true;

                if (previousStatus == DownloadStatus.Downloading && currentStatus != DownloadStatus.Downloading)
                {
                    try
                    {
                        await TryStartQueuedTorrentsAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Queue drain error after '{torrent.Name}' left Downloading: {ex.Message}");
                    }
                }

                if (currentStatus == DownloadStatus.Seeding)
                {
                    try
                    {
                        await EnforceSeedingLimitsAsync(torrent, manager).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Seeding limit enforcement error for '{torrent.Name}': {ex.Message}");
                    }
                }

                // A stopped download is finished, and an engine error stays until the user
                // restarts the torrent. Polling either state only rewrites unchanged data.
                if ((managerState == TorrentState.Stopped && progress >= 100) || managerState == TorrentState.Error)
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
            try
            {
                await using var operationLock = await _torrentOperationLock.AcquireAsync(torrent.Id, _disposalToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    var stopped = false;
                    try { await StopManagerAsync(manager).ConfigureAwait(false); stopped = true; }
                    catch (Exception stopError) { System.Diagnostics.Debug.WriteLine($"Monitor cleanup failed: {stopError}"); }
                    await _dispatcher.InvokeAsync(() =>
                    {
                        if (stopped)
                        {
                            UpdateSeedingTime(torrent, active: false);
                            torrent.DownloadSpeed = 0;
                            torrent.UploadSpeed = 0;
                            torrent.Status = DownloadStatus.Failed;
                        }
                        // If stopping failed, retain active controls so the user can retry.
                        torrent.ErrorMessage = ex.Message;
                    }).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_disposalToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_disposed) { }
        }
        finally
        {
            // Only remove our CTS – a newer Start may have already replaced it.
            if (monitorCts is not null)
            {
                if (((ICollection<KeyValuePair<string, CancellationTokenSource>>)_downloadTokens)
                    .Remove(new(torrent.Id, monitorCts)))
                    monitorCts.Dispose();
            }

            _pendingSave = true;
            UpdateBackgroundTransferState();
        }
    }

    internal readonly record struct AvailabilityInfo(double Percent, string Label);

    private async Task NotifyCompletionSafelyAsync(TorrentItem torrent)
    {
        // Completion tracking is independent of notification permission and notification failures.
        try
        {
            if (_completionObserver is not null)
                await _completionObserver.OnDownloadCompletedAsync(torrent).ConfigureAwait(false);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Completion tracking failed: {ex.Message}"); }
        try { await _notificationService.ShowDownloadCompletedAsync(torrent).ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Completion notification failed: {ex}"); }
    }

    /// <summary>
    /// Reports the percentage of torrent pieces currently available from connected peers.
    /// A collection of partial peers can therefore report 100% without any one peer being a seed.
    /// </summary>
    internal static AvailabilityInfo GetAvailabilityInfo(IEnumerable<ReadOnlyBitField> peerBitFields, int pieceCount)
    {
        if (pieceCount <= 0)
        {
            return new AvailabilityInfo(0, "—");
        }

        var availablePieces = new BitField(pieceCount);
        foreach (var bitField in peerBitFields)
        {
            if (bitField.Length == pieceCount)
            {
                availablePieces.Or(bitField);
                if (availablePieces.AllTrue)
                {
                    break;
                }
            }
        }

        var percent = availablePieces.PercentComplete;
        return new AvailabilityInfo(percent, $"{percent:0.#}%");
    }

    private static (int Seeds, int Leeches) GetSwarmPeerCounts(
        TorrentManager manager,
        int connectedSeeds,
        int connectedLeeches)
    {
        int? scrapedSeeds = null;
        int? scrapedLeeches = null;

        foreach (var tier in manager.TrackerManager.Tiers)
        {
            foreach (var infoHash in EnumerateInfoHashes(manager.InfoHashes))
            {
                if (!tier.ScrapeInfo.TryGetValue(infoHash, out var scrapeInfo))
                {
                    continue;
                }

                // The same peers can be returned by multiple trackers, so use the largest
                // reported swarm rather than incorrectly adding duplicate tracker totals.
                scrapedSeeds = Math.Max(scrapedSeeds ?? 0, scrapeInfo.Complete);
                scrapedLeeches = Math.Max(scrapedLeeches ?? 0, scrapeInfo.Incomplete);
            }
        }

        return (
            Math.Max(connectedSeeds, scrapedSeeds ?? 0),
            Math.Max(connectedLeeches, scrapedLeeches ?? 0));
    }

    private static IEnumerable<InfoHash> EnumerateInfoHashes(InfoHashes infoHashes)
    {
        if (infoHashes.V1 is not null)
        {
            yield return infoHashes.V1;
        }

        if (infoHashes.V2 is not null)
        {
            yield return infoHashes.V2;
        }
    }

    private static async Task RefreshTrackerScrapeAsync(TorrentManager manager, CancellationToken cancellationToken)
    {
        try
        {
            await manager.TrackerManager.ScrapeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the torrent is paused or stopped.
        }
        catch (Exception ex)
        {
            // Scraping is optional; connected-peer counts remain a valid fallback.
            System.Diagnostics.Debug.WriteLine($"Tracker scrape error: {ex.Message}");
        }
    }

    private static int ComputeHealthScore(int seeds, int leeches, double availabilityPercent)
    {
        var availabilityScore = Math.Clamp(availabilityPercent, 0, 100) * 0.5; // up to 50 points
        var seedScore = Math.Min(1, seeds / 10d) * 30; // up to 30 points
        var peerScore = Math.Min(1, (seeds + leeches) / 20d) * 20; // up to 20 points
        return (int)Math.Round(availabilityScore + seedScore + peerScore, MidpointRounding.AwayFromZero);
    }

    private Task SaveIfPendingAsync() => SaveIfPendingCoreAsync(includeProgress: false);

    private async Task SaveIfPendingCoreAsync(bool includeProgress)
    {
        try
        {
            var progressDue = _pendingProgressSave
                && (includeProgress
                    || _timeProvider.GetElapsedTime(Interlocked.Read(ref _lastSaveTimestamp)) >= ProgressSaveInterval);
            if (_pendingSave || progressDue)
            {
                _pendingSave = false;
                await SaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Debounced save error: {ex.Message}");
            _pendingSave = true;
        }
    }

    private async Task SaveAsync()
    {
        List<TorrentItem> snapshot;
        // Every save includes current progress; a tick after this snapshot sets it again.
        _pendingProgressSave = false;
        lock (_torrentsLock)
        {
            snapshot = [.. Torrents];
        }
        try
        {
            await _storageService.SaveTorrentsAsync(snapshot).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastSaveTimestamp, _timeProvider.GetTimestamp());
        }
        catch { _pendingSave = true; throw; }
        finally { UpdateBackgroundTransferState(); }
    }

    private async Task<ClientEngine> EnsureEngineAsync()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        await _engineLock.WaitAsync(_disposalToken).ConfigureAwait(false);
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
            try
            {
                _engineLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed during shutdown while this call still held the lock.
            }
        }
    }

    /// <summary>Fast-resume, magnet metadata, and DHT cache used by engines this service creates.</summary>
    protected virtual string EngineCacheDirectory => Path.Combine(_storageService.GetAppDataPath(), "EngineCache");

    protected virtual ClientEngine CreateEngine()
    {
        // When a SOCKS5 proxy is active we can only tunnel outbound TCP (peer connections and
        // HTTP/HTTPS tracker + web-seed requests). Every other discovery channel below either
        // uses UDP (DHT) or exposes a directly reachable endpoint (inbound listener,
        // UPnP/NAT-PMP, local peer discovery), none of which the proxy covers — leaving them
        // enabled would broadcast the user's real IP and defeat the purpose of the proxy. So
        // in proxy mode we shut them all down and rely on proxied peer + HTTP-tracker traffic
        // only. UDP bootstrap trackers are likewise skipped (see GetOrCreateManagerAsync).
        var useProxy = ProxyRequested;

        var cacheDirectory = EngineCacheDirectory;
        try
        {
            EngineCacheMigration.Migrate(_storageService.GetDefaultDownloadPath(), cacheDirectory);
        }
        catch (Exception ex)
        {
            // The legacy cache only saves a re-hash. It must never prevent every transfer from starting.
            System.Diagnostics.Debug.WriteLine($"Legacy engine cache migration skipped: {ex.Message}");
        }

        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDirectory,
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

        return new ClientEngine(
            engineSettings,
            CreateProxyFactories(_proxyHost, _proxyPort, _proxyUsername, _proxyPassword));
    }

    internal static Factories CreateProxyFactories(string host, int port, string username, string password)
    {
        Func<AddressFamily, HttpClient> httpClientCreator =
            _ => CreateProxiedHttpClient(host, port, username, password);

        // In MonoTorrent 3.0.2 the default peer-connection creators construct their own
        // SocketConnector, so WithSocketConnectorCreator alone never affects peer TCP.
        // Likewise, the default HTTP tracker creators close over Factories.Default's
        // original HttpClient factory. Override both routing layers explicitly.
        return Factories.Default
            .WithSocketConnectorCreator(() => new Socks5SocketConnector(host, port, username, password))
            .WithPeerConnectionCreator(
                "ipv4",
                uri => new SocketPeerConnection(uri, new Socks5SocketConnector(host, port, username, password)))
            .WithPeerConnectionCreator(
                "ipv6",
                uri => new SocketPeerConnection(uri, new Socks5SocketConnector(host, port, username, password)))
            .WithHttpClientCreator(family => httpClientCreator(family))
            .WithTrackerCreator("http", uri => CreateHttpTracker(uri, httpClientCreator))
            .WithTrackerCreator("https", uri => CreateHttpTracker(uri, httpClientCreator))
            // Passing null restores MonoTorrent's default UDP creator. Throwing makes
            // Factories.CreateTracker return null, so UDP announces cannot bypass SOCKS5.
            .WithTrackerCreator("udp", _ => throw new NotSupportedException("UDP trackers are disabled while SOCKS5 proxy mode is active."));
    }

    private static ITracker CreateHttpTracker(Uri uri, Func<AddressFamily, HttpClient> httpClientCreator)
        => new Tracker(
            new HttpTrackerConnection(uri, httpClientCreator, AddressFamily.InterNetwork),
            new HttpTrackerConnection(uri, httpClientCreator, AddressFamily.InterNetworkV6));

    /// <summary>Trims the host and removes URI brackets from an IPv6 literal.</summary>
    internal static string NormalizeProxyHost(string? host)
    {
        var trimmed = host?.Trim() ?? string.Empty;
        return trimmed.Length > 2
            && trimmed[0] == '['
            && trimmed[^1] == ']'
            && System.Net.IPAddress.TryParse(trimmed[1..^1], out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6
                ? trimmed[1..^1]
                : trimmed;
    }

    internal static Uri BuildProxyUri(string host, int port)
    {
        // An IPv6 literal must be bracketed in a URI authority.
        var authorityHost = System.Net.IPAddress.TryParse(host, out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{host}]"
                : host;
        return new Uri($"socks5://{authorityHost}:{port}");
    }

    private static HttpClient CreateProxiedHttpClient(string host, int port, string username, string password)
    {
        var proxy = new System.Net.WebProxy(BuildProxyUri(host, port));
        if (!string.IsNullOrEmpty(username))
        {
            proxy.Credentials = new System.Net.NetworkCredential(username, password);
        }

        var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = true
        };

        return new HttpClient(handler, disposeHandler: true);
    }


    private static string? GetMetadataPath(TorrentItem torrent)
        => !string.IsNullOrWhiteSpace(torrent.CachedTorrentFilePath) && File.Exists(torrent.CachedTorrentFilePath)
            ? torrent.CachedTorrentFilePath
            : (!string.IsNullOrWhiteSpace(torrent.TorrentFilePath) && File.Exists(torrent.TorrentFilePath) ? torrent.TorrentFilePath : null);

    protected virtual async Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent)
    {
        if (_managers.TryGetValue(torrent.Id, out var existing))
        {
            return existing;
        }

        var engine = await EnsureEngineAsync();
        var downloadPath = string.IsNullOrWhiteSpace(torrent.SavePath) ? _storageService.GetDefaultDownloadPath() : torrent.SavePath;
        Directory.CreateDirectory(downloadPath);
        // EnsureEngineAsync can resume on a thread-pool thread (ConfigureAwait(false) inside),
        // and this property drives live UI bindings (CanOpenDownloadedFile), so it must be
        // set on the UI thread like every other TorrentItem mutation in this service.
        await _dispatcher.InvokeAsync(() =>
        {
            torrent.SavePath = downloadPath;
        });

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
        var metadataPath = GetMetadataPath(torrent);
        if (metadataPath is not null)
        {
            try
            {
                var monoTorrent = await LoadTorrentFileBoundedAsync(metadataPath, torrent);
                manager = await engine.AddAsync(monoTorrent, downloadPath, torrentSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load .torrent file: {ex.Message}. Falling back to magnet link.");
                var magnet = MagnetLink.Parse(torrent.MagnetLink);
                manager = await engine.AddAsync(magnet, downloadPath, torrentSettings);
                if (!ProxyRequested)
                {
                    await AddPublicTrackersIfNeededAsync(manager, torrent.MagnetLink);
                }
            }
        }
        else
        {
            var magnet = MagnetLink.Parse(torrent.MagnetLink);
            manager = await engine.AddAsync(magnet, downloadPath, torrentSettings);
            if (!ProxyRequested)
            {
                // The bootstrap trackers are all UDP, which the SOCKS5 proxy cannot tunnel;
                // adding them in proxy mode would leak the real IP via tracker announces.
                await AddPublicTrackersIfNeededAsync(manager, torrent.MagnetLink);
            }
        }

        _managers[torrent.Id] = manager;
        var resolvedDownloadPath = GetResolvedDownloadPath(manager);
        if (resolvedDownloadPath is not null)
            await _dispatcher.InvokeAsync(() => torrent.ResolvedDownloadPath = resolvedDownloadPath);
        return manager;
    }

    private static string? GetResolvedDownloadPath(TorrentManager manager) => !manager.HasMetadata || manager.Files.Count == 0
        ? null
        : manager.Files.Count == 1 ? manager.Files[0].FullPath : manager.ContainingDirectory;

    protected virtual Task StartManagerAsync(TorrentManager manager) => manager.StartAsync();

    protected virtual Task StopManagerAsync(TorrentManager manager)
    {
        if (!TorrentManagerStateRules.RequiresFullStop(manager.State))
        {
            return Task.CompletedTask;
        }

        return manager.StopAsync(ManagerStopTimeout);
    }

    private Task PauseManagerAsync(TorrentManager manager)
        => TorrentManagerStateRules.CanPauseInPlace(manager.State)
            ? manager.PauseAsync()
            : StopManagerAsync(manager);

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
        // Release before waiting on persistence or tracker shutdown. Serialize against the
        // state snapshots so an update already in flight cannot reacquire after this release.
        lock (_torrentsLock) _sleepPreventionService?.SetPreventSleep(false);
        if (_networkMonitor is not null)
            _networkMonitor.Changed -= OnNetworkChanged;
        // Cancelled first, before anything is torn down: unblocks any pending
        // _torrentOperationLock/_engineLock wait immediately (via OperationCanceledException)
        // instead of leaving it parked on a primitive this method is about to dispose.
        try
        {
            _disposalCts.Cancel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Disposal cancellation error: {ex.Message}");
        }

        try
        {
            _saveTimer.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save timer dispose error: {ex.Message}");
        }

        lock (_backgroundStateGate)
        {
            _backgroundTransferActive = false;
        }

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
        await SaveIfPendingCoreAsync(includeProgress: true).ConfigureAwait(false);
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
        // Stop in parallel: each stop can wait for tracker announces, and the desktop close
        // handler only allows a bounded time before the window closes.
        await Task.WhenAll(_managers.Select(async kvp =>
        {
            try
            {
                await StopManagerAsync(kvp.Value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A missing .torrent file or network error during shutdown should not crash the app.
                System.Diagnostics.Debug.WriteLine($"Manager stop error for {kvp.Key}: {ex.Message}");
            }
        })).ConfigureAwait(false);
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

        _disposalCts.Dispose();
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
