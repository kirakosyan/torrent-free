namespace TorrentFree.Services;

internal readonly record struct TorrentIdentity(string Id, string InfoHash, string MagnetLink);

internal readonly record struct TorrentRestoreDecision(bool ShouldAdd, bool ShouldPersistChanges, bool ClearTorrentFileMetadata);

internal static class TorrentRestoreRules
{
    public static TorrentRestoreDecision Evaluate(TorrentIdentity torrent, string? torrentFilePath, IEnumerable<TorrentIdentity> existingTorrents, Func<string, bool> magnetValidator)
    {
        ArgumentNullException.ThrowIfNull(existingTorrents);
        ArgumentNullException.ThrowIfNull(magnetValidator);

        if (HasMissingTorrentFile(torrentFilePath))
        {
            if (!magnetValidator(torrent.MagnetLink))
            {
                return new TorrentRestoreDecision(ShouldAdd: false, ShouldPersistChanges: true, ClearTorrentFileMetadata: false);
            }

            return IsDuplicateOfAny(torrent, existingTorrents)
                ? new TorrentRestoreDecision(ShouldAdd: false, ShouldPersistChanges: true, ClearTorrentFileMetadata: false)
                : new TorrentRestoreDecision(ShouldAdd: true, ShouldPersistChanges: true, ClearTorrentFileMetadata: true);
        }

        if (IsDuplicateOfAny(torrent, existingTorrents))
        {
            return new TorrentRestoreDecision(ShouldAdd: false, ShouldPersistChanges: true, ClearTorrentFileMetadata: false);
        }

        return new TorrentRestoreDecision(ShouldAdd: true, ShouldPersistChanges: false, ClearTorrentFileMetadata: false);
    }

    public static bool HasMissingTorrentFile(string? torrentFilePath)
    {
        return !string.IsNullOrWhiteSpace(torrentFilePath)
            && !File.Exists(torrentFilePath);
    }

    public static bool IsDuplicateOfAny(TorrentIdentity torrent, IEnumerable<TorrentIdentity> existingTorrents)
    {
        ArgumentNullException.ThrowIfNull(existingTorrents);

        foreach (var existing in existingTorrents)
        {
            if (!string.IsNullOrWhiteSpace(torrent.Id)
                && torrent.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(torrent.InfoHash)
                && torrent.InfoHash.Equals(existing.InfoHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(torrent.MagnetLink)
                && torrent.MagnetLink.Equals(existing.MagnetLink, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}