namespace TorrentFree.Services;

/// <summary>Platform-protected storage for credentials which must not be written to app state files.</summary>
public interface ISecretStore
{
    /// <summary>Returns the stored value, or <see langword="null"/> when none exists.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Stores the value; <see langword="null"/> or an empty value removes the entry.</summary>
    Task SetAsync(string key, string? value);
}
