namespace TorrentFree.Services;

/// <summary>
/// Keeps credentials in platform secure storage (Android Keystore, Apple Keychain, Windows data
/// protection). Where secure storage is unavailable, such as unpackaged Windows debug builds,
/// calls throw and <see cref="StorageService"/> keeps its previous behavior.
/// </summary>
public sealed class MauiSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SecureStorage.Default.Remove(key);
            return Task.CompletedTask;
        }

        return SecureStorage.Default.SetAsync(key, value);
    }
}
