namespace TorrentFree.Models;

/// <summary>
/// Resolves the effective save path for new torrents from persisted settings.
/// </summary>
public static class DownloadLocationResolver
{
    /// <summary>
    /// Chooses the save path for a new torrent.
    /// </summary>
    public static string ResolveSavePath(AppSettings settings, string? sourceTorrentFilePath, string fallbackDownloadPath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(fallbackDownloadPath))
        {
            throw new ArgumentException("Fallback download path is required.", nameof(fallbackDownloadPath));
        }

        if (!settings.DownloadToTorrentFolder)
        {
            var specificFolder = settings.SpecificDownloadFolder?.Trim();
            if (!string.IsNullOrWhiteSpace(specificFolder))
            {
                return specificFolder;
            }
        }

        var torrentFolder = Path.GetDirectoryName(sourceTorrentFilePath);
        return string.IsNullOrWhiteSpace(torrentFolder) ? fallbackDownloadPath : torrentFolder;
    }
}