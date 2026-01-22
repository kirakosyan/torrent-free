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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading torrents: {ex.Message}");
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
        // Use the app's cache directory for downloads on mobile, or Documents on desktop
        var basePath = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS
            ? FileSystem.CacheDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var downloadPath = Path.Combine(basePath, "TorrentFree", "Downloads");
        
        if (!Directory.Exists(downloadPath))
        {
            Directory.CreateDirectory(downloadPath);
        }

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
