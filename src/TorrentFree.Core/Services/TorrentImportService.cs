namespace TorrentFree.Services;

/// <summary>Retains imported metadata before publishing the torrent to the queue.</summary>
public sealed class TorrentImportService(StoragePaths paths, ITorrentFileParser parser)
{
    private static readonly AsyncKeyedLocker CacheLocks = new();

    internal static string GetCachePath(string appDataDirectory, string? infoHash)
    {
        if (infoHash is not { Length: 40 or 64 } hash || !hash.All(Uri.IsHexDigit))
            throw new FormatException("The torrent does not contain a supported info hash.");
        return Path.Combine(appDataDirectory, "ImportedTorrents", hash.ToLowerInvariant() + ".torrent");
    }

    internal static ValueTask<AsyncKeyedLocker.Releaser> LockCacheAsync(string cachePath, CancellationToken cancellationToken = default)
    {
        var key = Path.GetFullPath(cachePath);
        return CacheLocks.AcquireAsync(OperatingSystem.IsWindows() ? key.ToUpperInvariant() : key, cancellationToken);
    }

    internal static async Task WriteCacheAsync(string cachePath, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var temporaryPath = cachePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally { File.Delete(temporaryPath); }
    }

    public async Task<TorrentMetadata> PrepareAsync(TorrentPickedFile picked, CancellationToken cancellationToken = default)
    {
        var metadata = parser.Parse(picked.Content);
        var cachePath = GetCachePath(paths.AppDataDirectory, metadata.InfoHashHex);
        await using (await LockCacheAsync(cachePath, cancellationToken))
            await WriteCacheAsync(cachePath, picked.Content, cancellationToken);

        var sourcePath = GetLocalSourcePath(picked.FullPath);
        return metadata with
        {
            CachedFilePath = cachePath,
            CachedContent = picked.Content.ToArray(),
            SourceFilePath = sourcePath,
            SourceFileName = picked.FileName,
            DownloadSourcePath = IsSourceFolderWritable(sourcePath) ? sourcePath : null
        };
    }

    private static string? GetLocalSourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile) return null;
            path = uri.LocalPath;
        }
        return Path.IsPathFullyQualified(path) && File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static bool IsSourceFolderWritable(string? path)
    {
        if (path is null) return false;
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(path)!, $".torrentfree-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
