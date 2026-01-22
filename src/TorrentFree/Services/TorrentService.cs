using System.Collections.ObjectModel;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Interface for torrent management operations.
/// </summary>
public interface ITorrentService
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
    private readonly Dictionary<string, CancellationTokenSource> _downloadTokens = [];
    private readonly Random _random = new(); // For demo simulation

    public ObservableCollection<TorrentItem> Torrents { get; } = [];

    public TorrentService(IStorageService storageService)
    {
        _storageService = storageService;
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

        // Parse torrent name from magnet link
        var name = ParseTorrentName(magnetLink);

        var torrent = new TorrentItem
        {
            MagnetLink = magnetLink,
            Name = name,
            Status = DownloadStatus.Queued,
            TotalSize = _random.NextInt64(100_000_000, 5_000_000_000), // Demo: random size
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
        if (_downloadTokens.TryGetValue(torrent.Id, out var existingCts))
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
        if (_downloadTokens.TryGetValue(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
            _downloadTokens.Remove(torrent.Id);
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
        if (_downloadTokens.TryGetValue(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
            _downloadTokens.Remove(torrent.Id);
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
        if (_downloadTokens.TryGetValue(torrent.Id, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
            _downloadTokens.Remove(torrent.Id);
        }

        Torrents.Remove(torrent);
        await SaveAsync();

        // Optionally delete downloaded files
        if (deleteFiles && !string.IsNullOrEmpty(torrent.SavePath))
        {
            try
            {
                var filePath = Path.Combine(torrent.SavePath, torrent.Name);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                else if (Directory.Exists(filePath))
                {
                    Directory.Delete(filePath, true);
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

        // Basic magnet link validation
        return link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
    }

    private static string ParseTorrentName(string magnetLink)
    {
        // Try to extract name from magnet link
        var dnMatch = System.Text.RegularExpressions.Regex.Match(
            magnetLink,
            @"dn=([^&]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (dnMatch.Success)
        {
            return Uri.UnescapeDataString(dnMatch.Groups[1].Value);
        }

        // Fallback to a hash-based name
        var hashMatch = System.Text.RegularExpressions.Regex.Match(
            magnetLink,
            @"btih:([a-fA-F0-9]{40})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

                // Simulate progress
                var increment = _random.NextDouble() * 2.0; // 0-2% per tick
                torrent.Progress = Math.Min(100, torrent.Progress + increment);
                torrent.DownloadedSize = (long)(torrent.TotalSize * torrent.Progress / 100);
                
                // Simulate speeds
                torrent.DownloadSpeed = _random.NextInt64(100_000, 10_000_000); // 100KB/s - 10MB/s
                torrent.UploadSpeed = _random.NextInt64(10_000, 1_000_000); // 10KB/s - 1MB/s
                
                // Simulate peers
                torrent.Seeders = _random.Next(1, 100);
                torrent.Leechers = _random.Next(0, 50);

                // Periodic save
                if (_random.Next(10) == 0)
                {
                    await SaveAsync();
                }
            }

            if (torrent.Progress >= 100 && torrent.Status == DownloadStatus.Downloading)
            {
                torrent.Status = DownloadStatus.Completed;
                torrent.DateCompleted = DateTime.Now;
                torrent.DownloadSpeed = 0;
                torrent.UploadSpeed = 0;
                torrent.Progress = 100;
                torrent.DownloadedSize = torrent.TotalSize;
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
            torrent.Status = DownloadStatus.Failed;
            await SaveAsync();
        }
    }

    private Task SaveAsync()
    {
        return _storageService.SaveTorrentsAsync(Torrents);
    }
}
