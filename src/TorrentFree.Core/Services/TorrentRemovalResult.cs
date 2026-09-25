namespace TorrentFree.Services;

/// <summary>Outcome of <see cref="ITorrentService.RemoveTorrentAsync"/>.</summary>
/// <param name="Removed">The torrent was removed from the list.</param>
/// <param name="DownloadedFilesLeftInPlace">
/// Deleting downloaded files was requested, but some files could not be identified or deleted
/// and remain on disk.
/// </param>
public readonly record struct TorrentRemovalResult(bool Removed, bool DownloadedFilesLeftInPlace)
{
    public static TorrentRemovalResult NotRemoved { get; } = new(false, false);
}
