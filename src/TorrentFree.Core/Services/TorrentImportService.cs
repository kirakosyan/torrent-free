namespace TorrentFree.Services;

/// <summary>Retains imported metadata before publishing the torrent to the queue.</summary>
public sealed class TorrentImportService(StoragePaths paths, ITorrentFileParser parser)
{
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async Task<TorrentMetadata> PrepareAsync(TorrentPickedFile picked, CancellationToken cancellationToken = default)
    {
        var metadata = parser.Parse(picked.Content);
        if (metadata.InfoHashHex is not { Length: 40 or 64 } hash || !hash.All(Uri.IsHexDigit))
            throw new FormatException("The torrent does not contain a supported info hash.");

        var directory = Path.Combine(paths.AppDataDirectory, "ImportedTorrents");
        var cachePath = Path.Combine(directory, hash.ToLowerInvariant() + ".torrent");
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = cachePath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, picked.Content, cancellationToken);
                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            finally { File.Delete(temporaryPath); }
        }
        finally { _cacheLock.Release(); }

        var sourcePath = GetLocalSourcePath(picked.FullPath);
        return metadata with
        {
            CachedFilePath = cachePath,
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
