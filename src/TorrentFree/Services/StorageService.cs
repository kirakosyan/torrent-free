using System.Text.Json;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Interface for storage service operations.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Loads all torrent items from storage.
    /// </summary>
    Task<List<TorrentItem>> LoadTorrentsAsync();

    /// <summary>
    /// Saves all torrent items to storage.
    /// </summary>
    Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents);

    /// <summary>
    /// Gets the default download path.
    /// </summary>
    string GetDefaultDownloadPath();
}

/// <summary>
/// Service for persisting torrent data to JSON file.
/// </summary>
public class StorageService : IStorageService
{
    private const string TorrentsFileName = "torrents.json";
    private readonly string _dataPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private string? _cachedDownloadPath;

    public StorageService()
    {
        _dataPath = Path.Combine(FileSystem.AppDataDirectory, TorrentsFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public async Task<List<TorrentItem>> LoadTorrentsAsync()
    {
        try
        {
            if (!File.Exists(_dataPath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(_dataPath);
            var data = JsonSerializer.Deserialize<TorrentStorageData>(json, _jsonOptions);
            return data?.Torrents ?? [];
        }
        catch (JsonException ex)
        {
            // Log JSON parsing errors - indicates corrupted data
            System.Diagnostics.Debug.WriteLine($"Error parsing torrents data (file may be corrupted): {ex.Message}");
            return [];
        }
        catch (IOException ex)
        {
            // Log I/O errors - disk/permission issues
            System.Diagnostics.Debug.WriteLine($"Error reading torrents file (I/O error): {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            // Log unexpected errors
            System.Diagnostics.Debug.WriteLine($"Unexpected error loading torrents: {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
    {
        await _saveLock.WaitAsync();
        try
        {
            var data = new TorrentStorageData
            {
                Version = "1.0",
                LastUpdated = DateTime.UtcNow,
                Torrents = torrents.ToList()
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_dataPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_dataPath, json);
        }
        catch (IOException ex)
        {
            // Log I/O errors - disk full, permissions, etc.
            System.Diagnostics.Debug.WriteLine($"Error saving torrents (I/O error): {ex.Message}");
            // TODO: Consider notifying user about save failure in a future update
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving torrents: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <inheritdoc />
    public string GetDefaultDownloadPath()
    {
        // Return cached path if available and directory still exists
        if (_cachedDownloadPath != null && Directory.Exists(_cachedDownloadPath))
        {
            return _cachedDownloadPath;
        }

        // Use the app's cache directory for downloads on mobile, or Documents on desktop
        var basePath = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS
            ? FileSystem.CacheDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var downloadPath = Path.Combine(basePath, "TorrentFree", "Downloads");
        
        if (!Directory.Exists(downloadPath))
        {
            Directory.CreateDirectory(downloadPath);
        }

        _cachedDownloadPath = downloadPath;
        return downloadPath;
    }
}

/// <summary>
/// Data structure for storing torrent information in JSON.
/// </summary>
internal class TorrentStorageData
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; }
    public List<TorrentItem> Torrents { get; set; } = [];
}
