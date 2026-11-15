using TorrentFree.Models;

namespace TorrentFree.Services;

public sealed record DownloadExportFile(string SourcePath, string RelativeDirectory, long Length);

public sealed record DownloadExportPlan(IReadOnlyList<DownloadExportFile> Files)
{
    public long TotalBytes => Files.Sum(file => file.Length);

    public static DownloadExportPlan Create(string downloadPath, bool isDirectory, IReadOnlyList<TorrentFileChoice> choices,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isDirectory ? downloadPath : Path.GetDirectoryName(downloadPath)!));
        var checkedDirectories = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var files = new List<DownloadExportFile>();
        foreach (var choice in choices.Where(file => file.IsSelected))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = isDirectory ? Path.Combine(root, choice.Path.Replace('/', Path.DirectorySeparatorChar)) : downloadPath;
            path = Path.GetFullPath(path);
            if (!PathGuard.IsPathWithinDirectory(path, root) || (!isDirectory && choices.Count != 1))
                throw new InvalidDataException("The selected file is outside its download folder.");
            // Do not follow a link into unrelated files, even when metadata names are valid.
            CheckNotLink(path);
            for (var current = Path.GetDirectoryName(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if (!checkedDirectories.Add(current)) break;
                CheckNotLink(current);
                if (string.Equals(current, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
            }
            var length = new FileInfo(path).Length;
            if (length != choice.Length) throw new InvalidDataException("The selected download is incomplete.");
            files.Add(new(path, Path.GetRelativePath(root, Path.GetDirectoryName(path)!), length));
        }
        if (files.Count == 0) throw new InvalidDataException("No completed files are selected.");
        return new(files);
    }

    /// <summary>Offline recovery for completed legacy downloads whose metadata is unavailable.</summary>
    public static DownloadExportPlan FromCompletedFiles(string downloadPath, bool isDirectory,
        IReadOnlyCollection<string>? selectedPaths, CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(downloadPath));
        var choices = new List<TorrentFileChoice>();
        if (!isDirectory)
            choices.Add(new(Path.GetFileName(root), new FileInfo(root).Length));
        else if (selectedPaths is not null)
        {
            // A saved selection is authoritative: never include a deselected or unrelated file.
            foreach (var selected in selectedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(Path.Combine(root, selected.Replace('/', Path.DirectorySeparatorChar)));
                if (!PathGuard.IsPathWithinDirectory(path, root)) throw new InvalidDataException("Invalid selected file path.");
                choices.Add(new(selected, new FileInfo(path).Length));
            }
        }
        else
        {
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckNotLink(directory);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                    else if (!path.EndsWith(".!mt", StringComparison.OrdinalIgnoreCase))
                        choices.Add(new(Path.GetRelativePath(root, path), new FileInfo(path).Length));
                }
            }
        }
        return Create(root, isDirectory, choices, cancellationToken);
    }

    private static void CheckNotLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Export cannot follow symbolic links.");
    }
}

public enum DownloadExportPhase { Verifying, Copying }
public sealed record DownloadExportProgress(DownloadExportPhase Phase, long Bytes, long TotalBytes);
public sealed record DownloadExportBatchProgress(int FileNumber, int FileCount, DownloadExportProgress Progress);
public sealed class DownloadExportSpaceException() : IOException("There is not enough free space to export this file.");
