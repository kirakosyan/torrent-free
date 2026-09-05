namespace TorrentFree.Services;

/// <summary>Persistent paths supplied by the host, independent of its UI framework.</summary>
public sealed record StoragePaths(string AppDataDirectory, string DownloadDirectory);
