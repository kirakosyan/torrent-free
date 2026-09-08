using System.Text.Json;
using TorrentFree.Models;

namespace TorrentFree.Services;

public interface IStorageService
{
    Task<List<TorrentItem>> LoadTorrentsAsync();
    Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents);
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized);
    string GetDefaultDownloadPath();
}

/// <summary>Persists application state atomically. Read and write failures reach the caller.</summary>
public sealed class StorageService(StoragePaths paths) : IStorageService, IDisposable
{
    private readonly string _dataPath = Path.Combine(paths.AppDataDirectory, "torrents.json");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip
    };
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _torrentsLoaded;

    public async Task<List<TorrentItem>> LoadTorrentsAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            _torrentsLoaded = false;
            var data = await LoadDataAsync();
            _torrentsLoaded = true;
            return data.Torrents ?? [];
        }
        finally { _saveLock.Release(); }
    }

    public async Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
    {
        await _saveLock.WaitAsync();
        try
        {
            if (!_torrentsLoaded)
                throw new InvalidOperationException("Load the saved torrent list successfully before replacing it.");

            // Read settings under the same lock; a torrent save must not restore an old snapshot.
            var data = await LoadDataAsync();
            data.Torrents = torrents.ToList();
            await WriteDataAsync(data);
        }
        finally { _saveLock.Release(); }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _saveLock.WaitAsync();
        try { return (await LoadDataAsync()).Settings ?? new AppSettings(); }
        finally { _saveLock.Release(); }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _saveLock.WaitAsync();
        try
        {
            var data = await LoadDataAsync();
            data.Settings = settings;
            await WriteDataAsync(data);
        }
        finally { _saveLock.Release(); }
    }

    public async Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized)
    {
        await _saveLock.WaitAsync();
        try
        {
            var data = await LoadDataAsync();
            data.Settings ??= new AppSettings();
            if (data.Settings.DesktopWasMaximized == desktopWasMaximized)
                return;
            data.Settings.DesktopWasMaximized = desktopWasMaximized;
            await WriteDataAsync(data);
        }
        finally { _saveLock.Release(); }
    }

    public string GetDefaultDownloadPath()
    {
        Directory.CreateDirectory(paths.DownloadDirectory);
        return paths.DownloadDirectory;
    }

    private async Task<TorrentStorageData> LoadDataAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(_dataPath); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        return JsonSerializer.Deserialize<TorrentStorageData>(json, _jsonOptions)
            ?? throw new JsonException("The saved application state is null.");
    }

    private async Task WriteDataAsync(TorrentStorageData data)
    {
        data.Version = "1.0";
        data.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        Directory.CreateDirectory(paths.AppDataDirectory);
        var tempPath = _dataPath + ".tmp";
        var backupTempPath = _dataPath + ".bak.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            if (File.Exists(_dataPath))
            {
                File.Copy(_dataPath, backupTempPath, overwrite: true);
                File.Move(backupTempPath, _dataPath + ".bak", overwrite: true);
            }
            File.Move(tempPath, _dataPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
            TryDeleteTemporaryFile(backupTempPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Temporary state cleanup failed: {ex.Message}"); }
    }

    public void Dispose() => _saveLock.Dispose();
}

internal sealed class TorrentStorageData
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; }
    public List<TorrentItem>? Torrents { get; set; } = [];
    public AppSettings? Settings { get; set; } = new();
}
