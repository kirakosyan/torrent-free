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
    string GetAppDataPath();

    /// <summary>Whether download-location settings may place downloads outside the default folder.</summary>
    bool SupportsCustomDownloadLocations => true;
}

/// <summary>Persists application state atomically. Read and write failures reach the caller.</summary>
/// <remarks>
/// The last state read or written is cached, so routine torrent saves do not re-read and re-parse
/// the file; this instance is the only writer. The proxy password is kept in
/// <see cref="ISecretStore"/> when one is available and never written to the state file.
/// </remarks>
public sealed class StorageService(StoragePaths paths, ISecretStore? secrets = null) : IStorageService, IDisposable
{
    internal const string ProxyPasswordSecretKey = "proxy-password";

    private readonly string _dataPath = Path.Combine(paths.AppDataDirectory, "torrents.json");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip
    };
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _torrentsLoaded;
    private TorrentStorageData? _cachedData;
    private bool _secretValueKnown;
    private string? _lastSecretValue;

    public async Task<List<TorrentItem>> LoadTorrentsAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _torrentsLoaded = false;
            _cachedData = null;
            var data = await LoadDataAsync().ConfigureAwait(false);
            _cachedData = data;
            _torrentsLoaded = true;
            return data.Torrents ?? [];
        }
        finally { _saveLock.Release(); }
    }

    public async Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_torrentsLoaded)
                throw new InvalidOperationException("Load the saved torrent list successfully before replacing it.");

            // Keep the latest settings under the same lock; a torrent save must not restore an old snapshot.
            var data = await GetDataAsync().ConfigureAwait(false);
            data.Torrents = torrents.ToList();
            await WriteDataAsync(data).ConfigureAwait(false);
        }
        finally { _saveLock.Release(); }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await GetDataAsync().ConfigureAwait(false);
            var settings = (data.Settings ?? new AppSettings()).Clone();
            if (secrets is null)
            {
                return settings;
            }

            if (!string.IsNullOrEmpty(settings.ProxyPassword))
            {
                // A plaintext password from an older version (or a previous secret-store
                // failure). Move it out of the state file once the secret store accepts it.
                if (await TrySetSecretAsync(settings.ProxyPassword).ConfigureAwait(false))
                {
                    await RemovePlaintextPasswordAsync(data).ConfigureAwait(false);
                }

                return settings;
            }

            var (readSucceeded, password) = await TryGetSecretAsync().ConfigureAwait(false);
            settings.ProxyPassword = password ?? string.Empty;
            settings.ProxyPasswordUnavailable = !readSucceeded;
            return settings;
        }
        finally { _saveLock.Release(); }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await GetDataAsync().ConfigureAwait(false);
            var stored = settings.Clone();
            stored.ProxyPasswordUnavailable = false;
            // An empty password that could not be read is unknown, not cleared: writing it would
            // turn a transient secure-storage error into a deleted credential.
            var passwordUnknown = settings.ProxyPasswordUnavailable && string.IsNullOrEmpty(settings.ProxyPassword);
            if (secrets is not null
                && (passwordUnknown || await TrySetSecretAsync(settings.ProxyPassword).ConfigureAwait(false)))
            {
                stored.ProxyPassword = string.Empty;
            }

            data.Settings = stored;
            await WriteDataAsync(data).ConfigureAwait(false);
        }
        finally { _saveLock.Release(); }
    }

    public async Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await GetDataAsync().ConfigureAwait(false);
            data.Settings ??= new AppSettings();
            if (data.Settings.DesktopWasMaximized == desktopWasMaximized)
                return;
            var updated = data.Settings.Clone();
            updated.DesktopWasMaximized = desktopWasMaximized;
            data.Settings = updated;
            await WriteDataAsync(data).ConfigureAwait(false);
        }
        finally { _saveLock.Release(); }
    }

    public string GetDefaultDownloadPath()
    {
        Directory.CreateDirectory(paths.DownloadDirectory);
        return paths.DownloadDirectory;
    }

    public string GetAppDataPath() => paths.AppDataDirectory;

    public bool SupportsCustomDownloadLocations => paths.SupportsCustomDownloadLocations;

    private async Task<TorrentStorageData> GetDataAsync()
    {
        if (_cachedData is not null)
        {
            return _cachedData;
        }

        var data = await LoadDataAsync().ConfigureAwait(false);
        _cachedData = data;
        return data;
    }

    private async Task RemovePlaintextPasswordAsync(TorrentStorageData data)
    {
        var scrubbed = (data.Settings ?? new AppSettings()).Clone();
        scrubbed.ProxyPassword = string.Empty;
        data.Settings = scrubbed;
        // Write twice: the first write moves the plaintext state into the backup file, and
        // the second replaces that backup with the scrubbed state.
        await WriteDataAsync(data).ConfigureAwait(false);
        await WriteDataAsync(data).ConfigureAwait(false);
    }

    private async Task<(bool Succeeded, string? Value)> TryGetSecretAsync()
    {
        try
        {
            var value = await secrets!.GetAsync(ProxyPasswordSecretKey).ConfigureAwait(false);
            _lastSecretValue = string.IsNullOrEmpty(value) ? null : value;
            _secretValueKnown = true;
            return (true, value);
        }
        catch (Exception ex)
        {
            _secretValueKnown = false;
            // Fail closed: a missing password makes the proxy reject the connection.
            System.Diagnostics.Debug.WriteLine($"Secret store read failed: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<bool> TrySetSecretAsync(string? value)
    {
        value = string.IsNullOrEmpty(value) ? null : value;
        if (_secretValueKnown && string.Equals(value, _lastSecretValue, StringComparison.Ordinal))
            return true;

        try
        {
            await secrets!.SetAsync(ProxyPasswordSecretKey, value).ConfigureAwait(false);
            _lastSecretValue = value;
            _secretValueKnown = true;
            return true;
        }
        catch (Exception ex)
        {
            _secretValueKnown = false;
            // Keep the proxy usable on devices whose secure storage is broken; the password
            // then stays in the state file, as it did before secure storage was introduced.
            System.Diagnostics.Debug.WriteLine($"Secret store write failed: {ex.Message}");
            return false;
        }
    }

    private async Task<TorrentStorageData> LoadDataAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(_dataPath).ConfigureAwait(false); }
        catch (FileNotFoundException) { return await RecoverMissingStateAsync().ConfigureAwait(false); }
        catch (DirectoryNotFoundException) { return new(); }
        try { return DeserializeData(json); }
        catch (JsonException)
        {
            // Validate the backup before touching either file. I/O failures still surface;
            // a locked or inaccessible state file must never look like an empty install.
            string backupJson;
            TorrentStorageData recovered;
            try
            {
                backupJson = await File.ReadAllTextAsync(_dataPath + ".bak").ConfigureAwait(false);
                recovered = DeserializeData(backupJson);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
            {
                throw new JsonException("The saved application state is corrupt and no valid backup is available.", ex);
            }

            var corruptPath = _dataPath + $".corrupt-{Guid.NewGuid():N}";
            File.Copy(_dataPath, corruptPath);
            var recoveryPath = _dataPath + ".recovery.tmp";
            try
            {
                await File.WriteAllTextAsync(recoveryPath, backupJson).ConfigureAwait(false);
                File.Move(recoveryPath, _dataPath, overwrite: true);
            }
            finally { TryDeleteTemporaryFile(recoveryPath); }
            return recovered;
        }
    }

    /// <summary>
    /// A write rotates the state file into the backup before moving the new file into place.
    /// If the process stopped between those renames, the state file is missing but the new
    /// (temporary) or previous (backup) state still exists and must not look like a fresh install.
    /// </summary>
    private async Task<TorrentStorageData> RecoverMissingStateAsync()
    {
        foreach (var candidate in new[] { _dataPath + ".tmp", _dataPath + ".bak" })
        {
            string json;
            TorrentStorageData recovered;
            try
            {
                json = await File.ReadAllTextAsync(candidate).ConfigureAwait(false);
                recovered = DeserializeData(json);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
            {
                continue;
            }

            var recoveryPath = _dataPath + ".recovery.tmp";
            try
            {
                await File.WriteAllTextAsync(recoveryPath, json).ConfigureAwait(false);
                File.Move(recoveryPath, _dataPath, overwrite: false);
            }
            finally { TryDeleteTemporaryFile(recoveryPath); }
            return recovered;
        }

        return new();
    }

    private TorrentStorageData DeserializeData(string json) =>
        JsonSerializer.Deserialize<TorrentStorageData>(json, _jsonOptions)
        ?? throw new JsonException("The saved application state is null.");

    private async Task WriteDataAsync(TorrentStorageData data)
    {
        data.Version = "1.0";
        data.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        Directory.CreateDirectory(paths.AppDataDirectory);
        var tempPath = _dataPath + ".tmp";
        var deleteTemp = true;
        try
        {
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            // Rotate by rename instead of copying the previous file into the backup: one write
            // per save instead of two. RecoverMissingStateAsync covers a stop between renames.
            if (File.Exists(_dataPath))
            {
                File.Move(_dataPath, _dataPath + ".bak", overwrite: true);
                // From here the temporary file may be the only copy of the newest state.
                deleteTemp = false;
            }
            File.Move(tempPath, _dataPath, overwrite: true);
            _cachedData = data;
        }
        catch
        {
            // Re-read the file next time rather than trusting state which was not written.
            _cachedData = null;
            throw;
        }
        finally
        {
            if (deleteTemp)
            {
                TryDeleteTemporaryFile(tempPath);
            }
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
