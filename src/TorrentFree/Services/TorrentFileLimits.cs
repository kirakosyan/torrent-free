namespace TorrentFree.Services;

/// <summary>
/// Resource limits applied while importing untrusted .torrent metadata.
/// </summary>
internal static class TorrentFileLimits
{
    public const int MaxFileSizeBytes = 16 * 1024 * 1024;
    public const int MaxBencodeDepth = 100;
    public const int MaxBencodeNodes = 200_000;
    public const int MaxBencodeStrings = 100_000;
    public const int MaxBencodeContainerEntries = 200_000;
    public const int MaxBencodeEntriesPerContainer = 100_000;
    public const int MaxDictionaryKeyBytes = 4 * 1024;
    public const int MaxTorrentNameBytes = 4 * 1024;
    public const int MaxTrackerCount = 256;
    public const int MaxTrackerUrlBytes = 4 * 1024;
}
