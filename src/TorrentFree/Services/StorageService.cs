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
    /// Loads app settings from storage.
    /// </summary>
    Task<AppSettings> LoadSettingsAsync();

    /// <summary>
    /// Saves app settings to storage.
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>
    /// Updates the persisted desktop window maximized state without overwriting other settings.
    /// </summary>
    Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized);

    /// <summary>
    /// Gets the default download path.
    /// </summary>
    string GetDefaultDownloadPath();
}

/// <summary>
/// Service for persisting torrent data to JSON file.
/// </summary>
public class StorageService : IStorageService, IDisposable
{
    private const string TorrentsFileName = "torrents.json";
    private readonly string _dataPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private string? _cachedDownloadPath;
    private AppSettings _cachedSettings = new();

    public StorageService()
    {
        _dataPath = Path.Combine(GetAppDataDirectory(), TorrentsFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip
        };
    }

    // On Windows the MAUI FileSystem.AppDataDirectory points to the MSIX package sandbox
    // (LocalState) when running packaged, but to a different temp path when unpackaged.
    // Using LocalApplicationData + app subfolder gives a consistent, user-scoped path in
    // both modes so data is never lost when switching between Debug (unpackaged) and a
    // deployed MSIX build.
    private static string GetAppDataDirectory()
    {
#if WINDOWS
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TorrentFree");
        Directory.CreateDirectory(dir);

        // One-time migration: copy data from the old MSIX sandbox path if the new location
        // is empty and the old one has data (happens on first run after this change).
        MigrateFromMsixSandboxIfNeeded(dir);

        return dir;
#else
        return FileSystem.AppDataDirectory;
#endif
    }

#if WINDOWS
    private static void MigrateFromMsixSandboxIfNeeded(string newDir)
    {
        try
        {
            var newFile = Path.Combine(newDir, TorrentsFileName);
            if (File.Exists(newFile))
            {
                return; // already migrated or has its own data
            }

            // FileSystem.AppDataDirectory on a packaged app points to the MSIX LocalState folder.
            // Try to locate it via the known Packages path pattern.
            var packagesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages");

            if (!Directory.Exists(packagesRoot))
            {
                return;
            }

            foreach (var pkgDir in Directory.EnumerateDirectories(packagesRoot, "com.torrentfree.app*"))
            {
                var candidate = Path.Combine(pkgDir, "LocalState", TorrentsFileName);
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, newFile, overwrite: false);
                    System.Diagnostics.Debug.WriteLine($"Migrated torrents.json from {candidate} to {newFile}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data migration error: {ex.Message}");
        }
    }
#endif

    /// <inheritdoc />
    public async Task<List<TorrentItem>> LoadTorrentsAsync()
    {
        try
        {
            var data = await LoadDataAsync();
            _cachedSettings = data.Settings ?? new AppSettings();
            return data.Torrents ?? [];
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
                Torrents = torrents.ToList(),
                Settings = _cachedSettings
            };

            await WriteDataAsync(data);
        }
        catch (IOException ex)
        {
            // Log I/O errors - disk full, permissions, etc.
            System.Diagnostics.Debug.WriteLine($"Error saving torrents (I/O error): {ex.Message}");
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
    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            var data = await LoadDataAsync();
            _cachedSettings = data.Settings ?? new AppSettings();
            return _cachedSettings;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }
    }

    /// <inheritdoc />
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _saveLock.WaitAsync();
        try
        {
            // Read current data under the lock so concurrent SaveTorrentsAsync
            // cannot interleave between our read and write.
            var data = await LoadDataAsync();
            data.Settings = settings;
            data.Torrents ??= [];
            data.Version = "1.0";
            data.LastUpdated = DateTime.UtcNow;

            _cachedSettings = settings;

            await WriteDataAsync(data);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings (I/O error): {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized)
    {
        await _saveLock.WaitAsync();
        try
        {
            var data = await LoadDataAsync();
            var settings = data.Settings ?? new AppSettings();
            if (settings.DesktopWasMaximized == desktopWasMaximized)
            {
                _cachedSettings = settings;
                return;
            }

            settings.DesktopWasMaximized = desktopWasMaximized;
            data.Settings = settings;
            data.Torrents ??= [];
            data.Version = "1.0";
            data.LastUpdated = DateTime.UtcNow;

            _cachedSettings = settings;

            await WriteDataAsync(data);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving desktop window state (I/O error): {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving desktop window state: {ex.Message}");
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

#if ANDROID
        // Prefer app-specific external Downloads storage so completed files persist and can be surfaced via Android file providers.
        var basePath = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
            ?? FileSystem.AppDataDirectory;
#elif IOS
        var basePath = FileSystem.CacheDirectory;
#else
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif

        var downloadPath = Path.Combine(basePath, "TorrentFree", "Downloads");

        if (!Directory.Exists(downloadPath))
        {
            Directory.CreateDirectory(downloadPath);
        }

        _cachedDownloadPath = downloadPath;
        return downloadPath;
    }

    private async Task<TorrentStorageData> LoadDataAsync()
    {
        if (!File.Exists(_dataPath))
        {
            return new TorrentStorageData { Settings = new AppSettings() };
        }

        var json = await File.ReadAllTextAsync(_dataPath);
        var data = JsonSerializer.Deserialize<TorrentStorageData>(json, _jsonOptions);
        return data ?? new TorrentStorageData { Settings = new AppSettings() };
    }

    private async Task WriteDataAsync(TorrentStorageData data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);

        var directory = Path.GetDirectoryName(_dataPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _dataPath + ".tmp";
        var moved = false;
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _dataPath, overwrite: true);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to clean up temp file {tempPath}: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        _saveLock.Dispose();
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
    public AppSettings? Settings { get; set; }
}
