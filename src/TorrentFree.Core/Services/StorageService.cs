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
    private string? _knownSecretKey;
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

            var key = GetSecretKey(data.ProxyCredentialId);
            if (string.IsNullOrEmpty(settings.ProxyPassword) && key is not null)
            {
                var (readSucceeded, password) = await TryGetSecretAsync(key).ConfigureAwait(false);
                settings.ProxyPassword = password ?? string.Empty;
                settings.ProxyPasswordUnavailable = !readSucceeded || (data.ProxyCredentialId is not null && string.IsNullOrEmpty(password));
            }

            // Bind legacy credentials to this settings snapshot before the endpoint can change.
            if (!string.IsNullOrEmpty(settings.ProxyPassword)
                && (data.ProxyCredentialId is null || !string.IsNullOrEmpty(data.Settings?.ProxyPassword)))
            {
                var id = Guid.NewGuid().ToString("N");
                if (await TrySetSecretAsync(GetSecretKey(id)!, settings.ProxyPassword).ConfigureAwait(false))
                {
                    var scrubbed = settings.Clone();
                    scrubbed.ProxyPassword = string.Empty;
                    data.Settings = scrubbed;
                    await WriteDataAsync(data, id).ConfigureAwait(false);
                    await WriteDataAsync(data).ConfigureAwait(false);
                }
            }
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
            var id = data.ProxyCredentialId;
            if (secrets is not null)
            {
                var sameEndpoint = data.Settings?.HasSameProxyEndpoint(settings) == true;
                if (passwordUnknown && !sameEndpoint)
                    throw new InvalidOperationException("Re-enter the proxy password before changing its endpoint.");

                if (!passwordUnknown)
                {
                    if (string.IsNullOrEmpty(settings.ProxyPassword))
                        id = string.Empty;
                    else if (!sameEndpoint || id is null || _knownSecretKey != GetSecretKey(id)
                        || !string.Equals(_lastSecretValue, settings.ProxyPassword, StringComparison.Ordinal))
                    {
                        id = Guid.NewGuid().ToString("N");
                        if (!await TrySetSecretAsync(GetSecretKey(id)!, settings.ProxyPassword).ConfigureAwait(false))
                            id = string.Empty; // Preserve the existing plaintext fallback in this same snapshot.
                    }
                }
                if (!string.IsNullOrEmpty(id) || passwordUnknown) stored.ProxyPassword = string.Empty;
            }

            data.Settings = stored;
            await WriteDataAsync(data, id).ConfigureAwait(false);
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

    private static string? GetSecretKey(string? id) => id switch
    {
        null => ProxyPasswordSecretKey,
        "" => null,
        _ when Guid.TryParseExact(id, "N", out _) => ProxyPasswordSecretKey + "-" + id,
        _ => throw new JsonException("Invalid proxy credential reference.")
    };

    private async Task<(bool Succeeded, string? Value)> TryGetSecretAsync(string key)
    {
        try
        {
            var value = await secrets!.GetAsync(key).ConfigureAwait(false);
            _lastSecretValue = string.IsNullOrEmpty(value) ? null : value;
            _knownSecretKey = key;
            return (true, value);
        }
        catch (Exception ex)
        {
            _knownSecretKey = null;
            // Fail closed: a missing password makes the proxy reject the connection.
            System.Diagnostics.Debug.WriteLine($"Secret store read failed: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<bool> TrySetSecretAsync(string key, string? value)
    {
        try
        {
            await secrets!.SetAsync(key, value).ConfigureAwait(false);
            _lastSecretValue = value;
            _knownSecretKey = key;
            return true;
        }
        catch (Exception ex)
        {
            _knownSecretKey = null;
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
        catch (DirectoryNotFoundException) { return TorrentStorageData.Empty(); }
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
                throw new SavedStateRecoveryException(ex);
            }

            var corruptPath = _dataPath + $".corrupt-{Guid.NewGuid():N}";
            File.Copy(_dataPath, corruptPath);
            await RestoreStateAsync(recovered, backupJson, overwrite: true).ConfigureAwait(false);
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
        JsonException? recoveryError = null;
        var corruptBackup = false;
        foreach (var candidate in new[] { _dataPath + ".tmp", _dataPath + ".bak" })
        {
            string json;
            TorrentStorageData recovered;
            try
            {
                json = await File.ReadAllTextAsync(candidate).ConfigureAwait(false);
                recovered = DeserializeData(json);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }
            catch (JsonException ex)
            {
                recoveryError ??= ex;
                corruptBackup |= candidate.EndsWith(".bak", StringComparison.Ordinal);
                continue;
            }

            await RestoreStateAsync(recovered, json, overwrite: false).ConfigureAwait(false);
            return recovered;
        }

        // Missing files mean a fresh install; existing but unreadable state does not.
        // Leave all recovery candidates intact and prevent later saves from replacing them.
        if (corruptBackup)
            throw new SavedStateRecoveryException(recoveryError!);
        if (recoveryError is not null)
        {
            // A torn first save has never committed a primary or backup. Archive the
            // evidence before allowing a fresh state to replace that temporary file.
            File.Move(_dataPath + ".tmp", _dataPath + $".corrupt-{Guid.NewGuid():N}");
        }
        return TorrentStorageData.Empty();
    }

    private async Task RestoreStateAsync(TorrentStorageData recovered, string json, bool overwrite)
    {
        // Older backups had no binding to the shared secure-store key. It may now hold a
        // different endpoint's password. Only a password in the snapshot itself can migrate.
        if (secrets is not null && recovered.ProxyCredentialId is null && string.IsNullOrEmpty(recovered.Settings?.ProxyPassword))
        {
            recovered.ProxyCredentialId = string.Empty;
            json = JsonSerializer.Serialize(recovered, _jsonOptions);
        }
        var recoveryPath = _dataPath + ".recovery.tmp";
        try
        {
            await File.WriteAllTextAsync(recoveryPath, json).ConfigureAwait(false);
            File.Move(recoveryPath, _dataPath, overwrite);
        }
        finally { TryDeleteTemporaryFile(recoveryPath); }
    }

    private TorrentStorageData DeserializeData(string json)
    {
        var data = JsonSerializer.Deserialize<TorrentStorageData>(json, _jsonOptions)
            ?? throw new JsonException("The saved application state is null.");
        _ = GetSecretKey(data.ProxyCredentialId);
        _ = GetSecretKey(data.BackupProxyCredentialId);
        return data;
    }

    private Task WriteDataAsync(TorrentStorageData data) => WriteDataAsync(data, data.ProxyCredentialId);

    private async Task WriteDataAsync(TorrentStorageData data, string? credentialId)
    {
        var retiredId = data.BackupProxyCredentialId;
        data.BackupProxyCredentialId = data.ProxyCredentialId;
        data.ProxyCredentialId = credentialId;
        data.Version = "1.0";
        data.LastUpdated = DateTime.UtcNow;
        var tempPath = _dataPath + ".tmp";
        var deleteTemp = true;
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            Directory.CreateDirectory(paths.AppDataDirectory);
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
            if (deleteTemp && !string.IsNullOrEmpty(credentialId) && credentialId != data.BackupProxyCredentialId)
                await TryDeleteSecretAsync(GetSecretKey(credentialId)!).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (deleteTemp)
            {
                TryDeleteTemporaryFile(tempPath);
            }
        }

        // The old backup has now been rotated away. Keep secrets needed by either recoverable file.
        if (secrets is not null && retiredId != data.ProxyCredentialId && retiredId != data.BackupProxyCredentialId
            && GetSecretKey(retiredId) is { } retiredKey)
            await TryDeleteSecretAsync(retiredKey).ConfigureAwait(false);
    }

    private async Task TryDeleteSecretAsync(string key)
    {
        try { await secrets!.SetAsync(key, null).ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not remove retired proxy credential: {ex.Message}"); }
        if (_knownSecretKey == key)
        {
            _knownSecretKey = null;
            _lastSecretValue = null;
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
    public static TorrentStorageData Empty() => new() { ProxyCredentialId = string.Empty, BackupProxyCredentialId = string.Empty };

    // null identifies the legacy shared secret. An empty reference means no secure credential.
    public string? ProxyCredentialId { get; set; }
    public string? BackupProxyCredentialId { get; set; }
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; }
    public List<TorrentItem>? Torrents { get; set; } = [];
    public AppSettings? Settings { get; set; } = new();
}
