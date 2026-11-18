using MonoTorrent;
using TorrentFree.Models;

namespace TorrentFree.Services;

public partial class TorrentService
{
    private sealed record PayloadLayout(string Destination, string Directory)
    {
        public static PayloadLayout From(string root, Torrent metadata)
        {
            var single = metadata.Files.Count == 1;
            var destination = Path.GetFullPath(Path.Combine(root,
                single ? EscapeTorrentFilePath(metadata.Files[0].Path) : EscapeTorrentPath(metadata.Name)));
            if (!PathGuard.IsPathWithinDirectory(destination, root)) throw DestinationError("ErrorPayloadPath");
            return new(destination, single ? root : destination);
        }

        public string FilePath(ITorrentFile file)
        {
            var path = Path.GetFullPath(Path.Combine(Directory, EscapeTorrentFilePath(file.Path)));
            if (!PathGuard.IsPathWithinDirectory(path, Directory)) throw DestinationError("ErrorPayloadPath");
            return path;
        }
    }

    private static IOException DestinationError(string key) => new(LocalizationResourceManager.Instance[key]);

    private async Task ReservePayloadDestinationAsync(TorrentItem torrent, Torrent? metadata)
    {
        var root = ResolveDownloadRoot(new DirectoryInfo(torrent.SavePath));
        var dedicated = PathsEqual(torrent.ReservedPayloadPath, root);
        if (metadata is null && !dedicated) root = Path.Combine(root, "TorrentFree-" + Guid.NewGuid().ToString("N"));
        var destination = metadata is null || dedicated ? root : PayloadLayout.From(root, metadata).Destination;
        // Legacy records can refer to a different alias for this volume. Compute their actual
        // layout outside the gate. New reservations are always rechecked inside the gate.
        TorrentItem[] legacy;
        lock (_torrentsLock)
            legacy = Torrents.Where(t => t != torrent && t.ReservedPayloadPath is null && MayHaveDownloadedData(t)).ToArray();
        var legacyPaths = new Dictionary<TorrentItem, string[]>();
        foreach (var other in legacy)
            legacyPaths[other] = await GetLegacyDestinationsAsync(other).ConfigureAwait(false);

        bool changed;
        await _payloadOwnershipLock.WaitAsync(_disposalToken).ConfigureAwait(false);
        try
        {
            string[] others;
            lock (_torrentsLock)
                others = Torrents.Where(t => t != torrent)
                    .SelectMany(t => t.ReservedPayloadPath is { } path ? new[] { path } : legacyPaths.GetValueOrDefault(t, []))
                    .ToArray();
            if (others.Any(path => PayloadPathsOverlap(destination, path)))
            {
                if (torrent.ReservedPayloadPath is not null || MayHaveDownloadedData(torrent))
                    throw DestinationError("ErrorPayloadConflict");
                root = Path.Combine(root, "TorrentFree-" + Guid.NewGuid().ToString("N"));
                destination = metadata is null ? root : PayloadLayout.From(root, metadata).Destination;
                if (others.Any(path => PayloadPathsOverlap(destination, path))) throw DestinationError("ErrorPayloadConflict");
            }
            changed = torrent.SavePath != root || torrent.ReservedPayloadPath != destination;
            if (changed)
                await _dispatcher.InvokeAsync(() =>
                {
                    torrent.SavePath = root;
                    torrent.ReservedPayloadPath = destination;
                });
        }
        finally { _payloadOwnershipLock.Release(); }

        // AddAsync can rename partial files while installing metadata. Check its entire layout
        // before registration, outside the shared reservation lock.
        if (metadata is not null)
        {
            var layout = PayloadLayout.From(root, metadata);
            var checkedDirectories = new HashSet<string>(GetPathComparer()) { root };
            foreach (var file in metadata.Files)
            {
                _disposalToken.ThrowIfCancellationRequested();
                ValidatePayloadPath(root, layout.FilePath(file), checkedDirectories);
            }
        }
        else Directory.CreateDirectory(root);
        if (changed) await SaveAsync().ConfigureAwait(false);
    }

    private async Task<string[]> GetLegacyDestinationsAsync(TorrentItem torrent)
    {
        var savedRoot = string.IsNullOrWhiteSpace(torrent.SavePath) ? _storageService.GetDefaultDownloadPath() : torrent.SavePath;
        var root = ResolveDownloadRoot(new DirectoryInfo(savedRoot));
        if (torrent.ResolvedDownloadPath is { } resolved && PathGuard.IsPathWithinDirectory(resolved, savedRoot))
            return [Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(savedRoot, resolved)))];
        if ((GetMetadataPath(torrent) ?? FindEngineMetadataCachePath(torrent)) is { } path)
        {
            try { return [PayloadLayout.From(root, await LoadTorrentFileBoundedAsync(path, torrent)).Destination]; }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException or TorrentException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine($"Legacy destination metadata unavailable: {ex.Message}"); }
        }
        // With no file list, protect both the possible single-file and multi-file spellings.
        if (string.IsNullOrWhiteSpace(torrent.Name)) return [root];
        return [Path.Combine(root, EscapeTorrentFilePath(torrent.Name)), Path.Combine(root, EscapeTorrentPath(torrent.Name))];
    }

    internal MonoTorrent.PieceWriter.IPieceWriter CreatePayloadWriter(int maxOpenFiles) =>
        new PayloadPieceWriter(maxOpenFiles, FindPayloadOwner, () => _pendingSave = true);

    private TorrentItem FindPayloadOwner(string path)
    {
        lock (_torrentsLock)
            return Torrents.FirstOrDefault(t => t.ReservedPayloadPath is { } dest
                && (GetPathComparer().Equals(path, dest) || GetPathComparer().Equals(path, dest + ".!mt")
                    || PathGuard.IsPathWithinDirectory(path, dest)))
                ?? throw DestinationError("ErrorPayloadPath");
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

    internal static void ValidatePayloadPath(string root, string path, HashSet<string> checkedDirectories)
    {
        if (!PathGuard.IsPathWithinDirectory(path, root))
            throw DestinationError("ErrorPayloadPath");
        for (var directory = Path.GetDirectoryName(path); directory is not null && checkedDirectories.Add(directory);
            directory = Path.GetDirectoryName(directory))
            if (new DirectoryInfo(directory).LinkTarget is not null)
                throw DestinationError("ErrorPayloadPath");
        if (new FileInfo(path).LinkTarget is not null || new FileInfo(path + ".!mt").LinkTarget is not null)
            throw DestinationError("ErrorPayloadPath");
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
