namespace TorrentFree.Services;

/// <summary>Persistent paths supplied by the host, independent of its UI framework.</summary>
/// <param name="SupportsCustomDownloadLocations">
/// <see langword="false"/> where downloads must stay in <paramref name="DownloadDirectory"/>:
/// mobile platforms only expose temporary copies of picked .torrent files and restrict writes to
/// arbitrary folders, so "next to the .torrent file" and custom folders do not apply there.
/// </param>
public sealed record StoragePaths(string AppDataDirectory, string DownloadDirectory, bool SupportsCustomDownloadLocations = true);
