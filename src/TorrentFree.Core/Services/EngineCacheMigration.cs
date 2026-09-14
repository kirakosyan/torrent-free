namespace TorrentFree.Services;

internal static class EngineCacheMigration
{
    internal const string CompletionMarker = ".legacy-cache-migration.complete";

    public static void Migrate(string downloadDirectory, string cacheDirectory)
    {
        var sourceRoot = Path.GetFullPath(downloadDirectory);
        var destinationRoot = Path.GetFullPath(cacheDirectory);
        var marker = Path.Combine(destinationRoot, CompletionMarker);
        if (File.Exists(marker)) return;
        Directory.CreateDirectory(destinationRoot);
        if (IsLink(sourceRoot) || IsLink(destinationRoot))
            throw new IOException("Engine cache migration cannot follow directory links.");

        foreach (var folder in new[] { "fastresume", "metadata" })
        {
            var source = Path.Combine(sourceRoot, folder);
            var destination = Path.Combine(destinationRoot, folder);
            if (!Directory.Exists(source) || IsLink(source)) continue;
            Directory.CreateDirectory(destination);
            if (IsLink(destination)) throw new IOException("Engine cache destination is a directory link.");
            foreach (var file in Directory.EnumerateFiles(source))
            {
                var hash = Path.GetFileNameWithoutExtension(file);
                var extension = Path.GetExtension(file);
                if (hash.Length is not (40 or 64) || !hash.All(Uri.IsHexDigit)) continue;
                if (folder == "fastresume" ? extension != ".fresume" : extension is not (".torrent" or ".v2hashes")) continue;
                MoveFile(file, Path.Combine(destination, Path.GetFileName(file)), sourceRoot, destinationRoot);
            }
            if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
        }

        MoveFile(Path.Combine(sourceRoot, "dht_nodes.cache"), Path.Combine(destinationRoot, "dht_nodes.cache"), sourceRoot, destinationRoot);
        File.WriteAllText(marker, "complete");
    }

    private static void MoveFile(string source, string destination, string sourceRoot, string destinationRoot)
    {
        if (!PathGuard.IsPathWithinDirectory(source, sourceRoot)
            || !PathGuard.IsPathWithinDirectory(destination, destinationRoot))
            throw new IOException("Engine cache path escaped its directory.");
        // Preserve unrelated files, links, and any newer cache already in the destination.
        if (!File.Exists(source) || IsLink(source) || File.Exists(destination) || Directory.Exists(destination)) return;
        File.Move(source, destination);
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
