using MonoTorrent;
using TorrentFree.Models;

namespace TorrentFree.Services;

public partial class TorrentService
{
    private async Task PreparePayloadDestinationAsync(TorrentItem torrent, Torrent metadata, bool usePartialFiles)
    {
        await _payloadOwnershipLock.WaitAsync(_disposalToken).ConfigureAwait(false);
        try
        {
            var root = ResolveDownloadRoot(new DirectoryInfo(torrent.SavePath));
            var single = metadata.Files.Count == 1;
            var member = single ? EscapeTorrentFilePath(metadata.Files[0].Path) : EscapeTorrentPath(metadata.Name);
            var checkedDirectories = new HashSet<string>(GetPathComparer()) { root };
            var destination = GetPayloadPath(root, member, checkedDirectories);
            string[] otherDestinations;
            lock (_torrentsLock)
                otherDestinations = Torrents.Where(t => t != torrent)
                    .Select(t => t.ReservedPayloadPath ?? (MayHaveDownloadedData(t) ? t.DownloadedFilePath : null))
                    .OfType<string>().ToArray();

            if (otherDestinations.Any(other => PayloadPathsOverlap(destination, other)))
            {
                if (torrent.ReservedPayloadPath is not null || MayHaveDownloadedData(torrent))
                    throw new IOException("Another torrent uses this download location. Keep one transfer here and reimport the other into a different folder.");
                // Use a fresh subdirectory of the user's selected folder for the conflicting import.
                root = Path.Combine(root, "TorrentFree-" + Guid.NewGuid().ToString("N"));
                destination = GetPayloadPath(root, member, checkedDirectories);
                if (otherDestinations.Any(other => PayloadPathsOverlap(destination, other)))
                    throw new IOException("The selected download folder belongs to another torrent.");
            }

            var created = (torrent.CreatedPayloadPaths ?? []).ToHashSet(GetPathComparer());
            var selected = torrent.SelectedFilePaths?.ToHashSet(StringComparer.Ordinal);
            // MonoTorrent sets up paths (including legacy partial-file renames) when AddAsync
            // registers the manager. Validate every member before allowing that call.
            var files = metadata.Files.Select(file => (
                Path: single ? destination : GetPayloadPath(destination, EscapeTorrentFilePath(file.Path), checkedDirectories),
                Selected: selected is null || selected.Contains(NormalizeFilePath(file.Path)))).ToArray();
            var changed = torrent.ReservedPayloadPath != destination;
            try
            {
                foreach (var file in files)
                {
                    _disposalToken.ThrowIfCancellationRequested();
                    if (!file.Selected || File.Exists(file.Path) || File.Exists(file.Path + ".!mt")) continue;
                    var path = usePartialFiles ? file.Path + ".!mt" : file.Path;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    try { using var owned = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                    catch (IOException) when (File.Exists(path)) { continue; }
                    // MonoTorrent moves its partial file to the final name on completion.
                    created.Add(path);
                    created.Add(file.Path);
                    changed = true;
                }
            }
            finally
            {
                // A later member may fail. Keep the files already created removable on retry.
                if (changed)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        torrent.SavePath = root;
                        torrent.ReservedPayloadPath = destination;
                        torrent.CreatedPayloadPaths = created.ToArray();
                    });
                    await SaveAsync().ConfigureAwait(false);
                }
            }
        }
        finally { _payloadOwnershipLock.Release(); }
    }

    // The chosen root can legitimately be an alias (including the OS's app-data directory).
    // Resolve it before comparing destinations. Links supplied inside the payload are rejected.
    private static string ResolveDownloadRoot(DirectoryInfo directory)
    {
        var ancestors = new Stack<string>();
        while (directory.Parent is { } parent)
        {
            ancestors.Push(directory.Name);
            directory = parent;
        }
        var resolved = directory.FullName;
        while (ancestors.TryPop(out var member))
        {
            var next = new DirectoryInfo(Path.Combine(resolved, member));
            resolved = next.LinkTarget is null ? next.FullName : next.ResolveLinkTarget(true)!.FullName;
        }
        return resolved;
    }

    private static string GetPayloadPath(string root, string relative, HashSet<string> checkedDirectories)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!PathGuard.IsPathWithinDirectory(path, root))
            throw new IOException("A torrent file is outside its download folder.");
        for (var directory = Path.GetDirectoryName(path); directory is not null && checkedDirectories.Add(directory);
            directory = Path.GetDirectoryName(directory))
            if (new DirectoryInfo(directory).LinkTarget is not null)
                throw new IOException("Torrent downloads cannot follow a directory link.");
        if (new FileInfo(path).LinkTarget is not null || new FileInfo(path + ".!mt").LinkTarget is not null)
            throw new IOException("Torrent downloads cannot follow a file link.");
        return path;
    }

    private static bool PayloadPathsOverlap(string left, string right)
    {
        // Removable and emulated volumes can be case-insensitive even when the OS is not.
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        return string.Equals(left, right, comparison)
            || string.Equals(left + ".!mt", right, comparison) || string.Equals(left, right + ".!mt", comparison)
            || left.StartsWith(right + Path.DirectorySeparatorChar, comparison)
            || right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }
}
