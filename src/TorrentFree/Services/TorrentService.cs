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
}

/// <summary>
/// Service for managing torrent downloads.
/// </summary>
public class TorrentService : ITorrentService
{
    private readonly IStorageService _storageService;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundDownloadService _backgroundDownloadService;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens = new();
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly object _torrentsLock = new();
    private readonly Timer _saveTimer;
    private ClientEngine? _engine;
    private bool _initialized;
    private bool _pendingSave;
    private bool _disposed;
    private bool _backgroundTransferActive;

    private int _maxActiveDownloads = 2;
    private int _maxActiveSeeds = 2;
    private long _globalDownloadLimitBytesPerSec;
    private long _globalUploadLimitBytesPerSec;
    private double _globalMaxSeedRatio;
    private int _globalMaxSeedMinutes;

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
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            var savedTorrents = await _storageService.LoadTorrentsAsync();
            foreach (var torrent in savedTorrents)
            {
                // Reset downloading status to paused on startup
                if (torrent.Status == DownloadStatus.Downloading)
                {
                    torrent.Status = DownloadStatus.Paused;
                }
                AttachTorrentSettingsHandlers(torrent);
                Torrents.Add(torrent);
            }
        }
        catch
        {
            _initialized = false;
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

        var torrent = new TorrentItem
        {
            MagnetLink = magnetLink,
            InfoHash = infoHash,
            Name = name,
            Status = DownloadStatus.Queued,
            TotalSize = 0,
            SavePath = _storageService.GetDefaultDownloadPath()
        };

        AttachTorrentSettingsHandlers(torrent);
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

        if (!CanStartAnotherDownload())
        {
            torrent.Status = DownloadStatus.Queued;
            await SaveAsync();
            return;
        }

        torrent.Status = DownloadStatus.Downloading;
        await SaveAsync();
        UpdateBackgroundTransferState();

        var manager = await GetOrCreateManagerAsync(torrent);
        ApplySpeedLimitsToManager(manager, torrent);

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
        UpdateBackgroundTransferState();

        await TryStartQueuedTorrentsAsync();
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
        UpdateBackgroundTransferState();

        await TryStartQueuedTorrentsAsync();
    }

    /// <inheritdoc />
    public async Task RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false)
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

        DetachTorrentSettingsHandlers(torrent);
        Torrents.Remove(torrent);
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

        if (_engine is not null)
        {
            ApplySpeedLimitsToEngine(_engine, _globalDownloadLimitBytesPerSec, _globalUploadLimitBytesPerSec);
        }

        foreach (var kvp in _managers)
        {
            if (TryGetTorrentById(kvp.Key, out var torrent) && torrent is not null)
            {
                ApplySpeedLimitsToManager(kvp.Value, torrent);
            }
        }
    }

    /// <inheritdoc />
    public void UpdateQueueLimits(int maxActiveDownloads, int maxActiveSeeds)
    {
        _maxActiveDownloads = Math.Max(0, maxActiveDownloads);
        _maxActiveSeeds = Math.Max(0, maxActiveSeeds);

        _ = TryStartQueuedTorrentsAsync();

        if (_maxActiveSeeds > 0)
        {
            foreach (var kvp in _managers)
            {
                if (TryGetTorrentById(kvp.Key, out var torrent) && torrent is not null && torrent.Status == DownloadStatus.Seeding)
                {
                    _ = EnforceSeedingLimitsAsync(torrent, kvp.Value);
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
                _ = EnforceSeedingLimitsAsync(torrent, kvp.Value);
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
        torrent = Torrents.FirstOrDefault(t => t.Id == id);
        return torrent is not null;
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
            _ = UpdateTorrentManagerSettingsAsync(torrent);
        }
    }

    private async Task UpdateTorrentManagerSettingsAsync(TorrentItem torrent)
    {
        if (_managers.TryGetValue(torrent.Id, out var manager))
        {
            try
            {
                ApplySpeedLimitsToManager(manager, torrent);
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

        var activeDownloads = Torrents.Count(t => t.Status == DownloadStatus.Downloading);
        return activeDownloads < _maxActiveDownloads;
    }

    private bool CanStartAnotherSeed()
    {
        if (_maxActiveSeeds <= 0)
        {
            return true;
        }

        var activeSeeds = Torrents.Count(t => t.Status == DownloadStatus.Seeding);
        return activeSeeds < _maxActiveSeeds;
    }

    private async Task TryStartQueuedTorrentsAsync()
    {
        var availableSlots = _maxActiveDownloads <= 0
            ? int.MaxValue
            : Math.Max(0, _maxActiveDownloads - Torrents.Count(t => t.Status == DownloadStatus.Downloading));

        if (availableSlots <= 0)
        {
            return;
        }

        var queued = Torrents
            .Where(t => t.Status == DownloadStatus.Queued)
            .OrderBy(t => t.DateAdded)
            .Take(availableSlots)
            .ToList();

        foreach (var torrent in queued)
        {
            await StartTorrentAsync(torrent);
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
            var activeSeeds = Torrents.Count(t => t.Status == DownloadStatus.Seeding);
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
            var uploadedBytes = manager.Monitor.DataBytesSent;
            var ratio = uploadedBytes / (double)torrent.TotalSize;
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

    private void ApplySpeedLimitsToEngine(ClientEngine engine, long downloadLimitBytesPerSec, long uploadLimitBytesPerSec)
    {
        _ = TryApplySpeedLimitsToSettings(engine.Settings, downloadLimitBytesPerSec, uploadLimitBytesPerSec);
    }

    private void ApplySpeedLimitsToManager(TorrentManager manager, TorrentItem torrent)
    {
        var downloadLimit = torrent.DownloadLimitKbps > 0
            ? KbpsToBytes(torrent.DownloadLimitKbps)
            : _globalDownloadLimitBytesPerSec;

        var uploadLimit = torrent.UploadLimitKbps > 0
            ? KbpsToBytes(torrent.UploadLimitKbps)
            : _globalUploadLimitBytesPerSec;

        _ = TryApplySpeedLimitsToSettings(manager.Settings, downloadLimit, uploadLimit);
    }

    private static object? TryApplySpeedLimitsToSettings(object settings, long downloadLimitBytesPerSec, long uploadLimitBytesPerSec)
    {
        var working = settings;

        var downloadNames = new[] { "MaximumDownloadSpeed", "MaximumDownloadRate", "DownloadRateLimit", "DownloadSpeedLimit" };
        var uploadNames = new[] { "MaximumUploadSpeed", "MaximumUploadRate", "UploadRateLimit", "UploadSpeedLimit" };

        working = TryApplySetting(working, downloadNames, downloadLimitBytesPerSec) ?? working;
        working = TryApplySetting(working, uploadNames, uploadLimitBytesPerSec) ?? working;

        return working;
    }

    private static object? TryApplySetting(object settings, string[] names, long value)
    {
        foreach (var name in names)
        {
            if (TrySetNumericProperty(settings, name, value))
            {
                return settings;
            }

            var updated = TryInvokeWithNumeric(settings, name, value);
            if (updated is not null)
            {
                return updated;
            }
        }

        return null;
    }

    private static bool TrySetNumericProperty(object target, string propertyName, long value)
    {
        var prop = target.GetType().GetProperty(propertyName);
        if (prop is null || !prop.CanWrite)
        {
            return false;
        }

        try
        {
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? TryInvokeWithNumeric(object target, string baseName, long value)
    {
        var methodName = $"With{baseName}";
        var method = target.GetType().GetMethod(methodName, new[] { typeof(int) })
                     ?? target.GetType().GetMethod(methodName, new[] { typeof(long) });

        if (method is null)
        {
            return null;
        }

        try
        {
            var parameter = method.GetParameters()[0].ParameterType == typeof(int)
                ? (object)Math.Clamp(value, int.MinValue, int.MaxValue)
                : value;
            return method.Invoke(target, new[] { parameter });
        }
        catch
        {
            return null;
        }
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
                var previousStatus = torrent.Status;
                var currentStatus = previousStatus;

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

                    var availabilityInfo = GetAvailabilityInfo(manager, seeds, leeches);
                    torrent.AvailabilityPercent = availabilityInfo.Percent;
                    torrent.AvailabilityLabel = availabilityInfo.Label;
                    torrent.HealthScore = ComputeHealthScore(seeds, leeches, availabilityInfo.Percent);

                    torrent.AddSpeedSample(torrent.DownloadSpeed, torrent.UploadSpeed);

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
                        TorrentState.Seeding => DownloadStatus.Seeding,
                        TorrentState.Stopped when progress >= 100 => DownloadStatus.Completed,
                        TorrentState.Stopped => DownloadStatus.Stopped,
                        TorrentState.Downloading => DownloadStatus.Downloading,
                        TorrentState.Error => DownloadStatus.Failed,
                        _ => torrent.Status
                    };

                    if (manager.State == TorrentState.Error)
                    {
                        torrent.ErrorMessage = manager.Error?.Exception?.Message ?? "Unknown error occurred";
                    }
                    else
                    {
                        torrent.ErrorMessage = null;
                    }

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
                });

                var wasComplete = previousStatus is DownloadStatus.Completed or DownloadStatus.Seeding;
                var isComplete = currentStatus is DownloadStatus.Completed or DownloadStatus.Seeding;

                if (previousStatus != currentStatus)
                {
                    UpdateBackgroundTransferState();
                }

                if (!wasComplete && isComplete)
                {
                    await _notificationService.ShowDownloadCompletedAsync(torrent);
                }

                if (manager.HasMetadata && manager.Torrent != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        torrent.TotalSize = manager.Torrent.Size;
                        torrent.Name = manager.Torrent.Name;
                    });
                }

                _pendingSave = true;

                if (previousStatus == DownloadStatus.Downloading && currentStatus != DownloadStatus.Downloading)
                {
                    await TryStartQueuedTorrentsAsync();
                }

                if (currentStatus == DownloadStatus.Seeding)
                {
                    await EnforceSeedingLimitsAsync(torrent, manager);
                }

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
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                torrent.Status = DownloadStatus.Failed;
                torrent.ErrorMessage = ex.Message;
            });
        }
        finally
        {
            _downloadTokens.TryRemove(torrent.Id, out _);
            _pendingSave = true;
            UpdateBackgroundTransferState();
        }
    }

    private readonly record struct AvailabilityInfo(double Percent, string Label);

    private static AvailabilityInfo GetAvailabilityInfo(TorrentManager manager, int seeds, int leeches)
    {
        if (TryComputePieceAvailability(manager, out var percent))
        {
            return new AvailabilityInfo(percent, $"{percent:0}%");
        }

        if (TryGetAvailabilityCopies(manager, out var copies))
        {
            var meterPercent = Math.Clamp(copies / 2d, 0, 1) * 100;
            return new AvailabilityInfo(meterPercent, $"{copies:0.0}x");
        }

        if (seeds + leeches > 0)
        {
            var swarmPercent = Math.Clamp((seeds + leeches) / 20d, 0, 1) * 100;
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

    private static bool TryGetAvailabilityCopies(TorrentManager manager, out double copies)
    {
        copies = 0;

        if (TryGetNumericProperty(manager, "Availability", out copies))
        {
            return true;
        }

        if (manager.Peers is not null && TryGetNumericProperty(manager.Peers, "Availability", out copies))
        {
            return true;
        }

        if (manager.Peers is not null && TryGetNumericProperty(manager.Peers, "Available", out copies))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetNumericProperty(object target, string propertyName, out double value)
    {
        value = 0;
        var prop = target.GetType().GetProperty(propertyName);
        if (prop is null)
        {
            return false;
        }

        var raw = prop.GetValue(target);
        if (raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryComputePieceAvailability(TorrentManager manager, out double percent)
    {
        percent = 0;

        if (manager.Torrent is null)
        {
            return false;
        }

        var pieceCount = TryGetPieceCount(manager.Torrent);
        if (pieceCount <= 0)
        {
            return false;
        }

        var sampleStep = pieceCount > 2000 ? (int)Math.Ceiling(pieceCount / 2000d) : 1;
        var sampleCount = (int)Math.Ceiling(pieceCount / (double)sampleStep);
        var availableSamples = new bool[sampleCount];

        MarkAvailablePieces(manager, availableSamples, sampleStep, pieceCount);

        if (manager.Peers is not null)
        {
            foreach (var peer in GetConnectedPeers(manager.Peers))
            {
                MarkAvailablePieces(peer, availableSamples, sampleStep, pieceCount);
            }
        }

        var availableCount = availableSamples.Count(static x => x);
        if (availableCount == 0)
        {
            return false;
        }

        percent = availableCount / (double)sampleCount * 100d;
        return true;
    }

    private static void MarkAvailablePieces(object? source, bool[] availableSamples, int sampleStep, int pieceCount)
    {
        if (source is null)
        {
            return;
        }

        var bitfield = source.GetType().GetProperty("BitField")?.GetValue(source)
                       ?? source.GetType().GetProperty("Bitfield")?.GetValue(source);

        if (bitfield is null)
        {
            return;
        }

        var indexer = bitfield.GetType().GetProperty("Item");
        if (indexer is null)
        {
            return;
        }

        var sampleIndex = 0;
        for (var i = 0; i < pieceCount; i += sampleStep)
        {
            if (!availableSamples[sampleIndex])
            {
                var hasPiece = (bool?)(indexer.GetValue(bitfield, new object[] { i })) == true;
                if (hasPiece)
                {
                    availableSamples[sampleIndex] = true;
                }
            }
            sampleIndex++;
            if (sampleIndex >= availableSamples.Length)
            {
                break;
            }
        }
    }

    private static int TryGetPieceCount(object torrent)
    {
        var pieceCountProp = torrent.GetType().GetProperty("PieceCount");
        if (pieceCountProp?.GetValue(torrent) is int count && count > 0)
        {
            return count;
        }

        var piecesProp = torrent.GetType().GetProperty("Pieces") ?? torrent.GetType().GetProperty("PieceHashes");
        var pieces = piecesProp?.GetValue(torrent);
        if (pieces is null)
        {
            return 0;
        }

        var countProp = pieces.GetType().GetProperty("Count");
        if (countProp?.GetValue(pieces) is int piecesCount)
        {
            return piecesCount;
        }

        return 0;
    }

    private static IEnumerable<object> GetConnectedPeers(object peers)
    {
        var connectedProp = peers.GetType().GetProperty("ConnectedPeers")
                             ?? peers.GetType().GetProperty("ActivePeers")
                             ?? peers.GetType().GetProperty("AvailablePeers");

        if (connectedProp?.GetValue(peers) is System.Collections.IEnumerable peerList)
        {
            foreach (var peer in peerList)
            {
                if (peer is not null)
                {
                    yield return peer;
                }
            }
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

        ApplySpeedLimitsToEngine(_engine, _globalDownloadLimitBytesPerSec, _globalUploadLimitBytesPerSec);

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

        ApplySpeedLimitsToManager(manager, torrent);

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
        _backgroundTransferActive = false;

        try
        {
            _backgroundDownloadService.Stop();
        }
        catch
        {
            // Ignore shutdown errors
        }

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
        _pendingSave = false;

        foreach (var manager in _managers.Values)
        {
            try
            {
                manager.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown errors
            }
        }
        _managers.Clear();

        if (_engine is not null)
        {
            try
            {
                _engine.StopAllAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown errors
            }

            try
            {
                if (_engine is IAsyncDisposable asyncDisposableEngine)
                {
                    asyncDisposableEngine.DisposeAsync().GetAwaiter().GetResult();
                }
                else if (_engine is IDisposable disposableEngine)
                {
                    disposableEngine.Dispose();
                }
            }
            catch
            {
                // Ignore shutdown errors
            }

            _engine = null;
        }

        GC.SuppressFinalize(this);
    }
}
