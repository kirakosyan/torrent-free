using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
    private readonly object _torrentsLock = new();
    private readonly Timer _saveTimer;
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

        // Parse torrent name from magnet link and sanitize it
        var name = SanitizeFileName(ParseTorrentName(magnetLink));

        var torrent = new TorrentItem
        {
            MagnetLink = magnetLink,
            Name = name,
            Status = DownloadStatus.Queued,
            TotalSize = Random.Shared.NextInt64(100_000_000, 5_000_000_000), // Demo: random size
            SavePath = _storageService.GetDefaultDownloadPath()
        };

        Torrents.Add(torrent);
        await SaveAsync();

        return torrent;
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

        // Cancel any existing download for this torrent
        if (_downloadTokens.TryRemove(torrent.Id, out var existingCts))
        {
            await existingCts.CancelAsync();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _downloadTokens[torrent.Id] = cts;

        // Start simulated download in background
        _ = SimulateDownloadAsync(torrent, cts.Token);
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

        // Validate magnet link format: must start with magnet:? and contain an info hash (xt parameter)
        if (!link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check for presence of xt parameter (required for BitTorrent magnet links)
        return link.Contains("xt=", StringComparison.OrdinalIgnoreCase);
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

    private async Task SimulateDownloadAsync(TorrentItem torrent, CancellationToken cancellationToken)
    {
        // This is a simulation of download progress for demonstration purposes
        // In a real implementation, this would interface with a BitTorrent library

        try
        {
            while (!cancellationToken.IsCancellationRequested && torrent.Progress < 100)
            {
                await Task.Delay(500, cancellationToken);

                if (torrent.Status != DownloadStatus.Downloading)
                {
                    break;
                }

                // Update properties on UI thread to avoid cross-thread issues
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // Simulate progress
                    var increment = Random.Shared.NextDouble() * 2.0; // 0-2% per tick
                    torrent.Progress = Math.Min(100, torrent.Progress + increment);
                    torrent.DownloadedSize = (long)(torrent.TotalSize * torrent.Progress / 100);
                    
                    // Simulate speeds
                    torrent.DownloadSpeed = Random.Shared.NextInt64(100_000, 10_000_000); // 100KB/s - 10MB/s
                    torrent.UploadSpeed = Random.Shared.NextInt64(10_000, 1_000_000); // 10KB/s - 1MB/s
                    
                    // Simulate peers
                    torrent.Seeders = Random.Shared.Next(1, 100);
                    torrent.Leechers = Random.Shared.Next(0, 50);
                });

                // Mark save as pending (debounced)
                _pendingSave = true;
            }

            if (torrent.Progress >= 100 && torrent.Status == DownloadStatus.Downloading)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    torrent.Status = DownloadStatus.Completed;
                    torrent.DateCompleted = DateTime.Now;
                    torrent.DownloadSpeed = 0;
                    torrent.UploadSpeed = 0;
                    torrent.Progress = 100;
                    torrent.DownloadedSize = torrent.TotalSize;
                });
                await SaveAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Download was cancelled, this is expected
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                torrent.Status = DownloadStatus.Failed;
            });
            await SaveAsync();
        }
        finally
        {
            // Clean up the token from the dictionary
            _downloadTokens.TryRemove(torrent.Id, out _);
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
