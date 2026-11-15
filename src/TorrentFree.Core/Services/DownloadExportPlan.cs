using TorrentFree.Models;

namespace TorrentFree.Services;

public sealed record DownloadExportFile(string SourcePath, string RelativeDirectory, long Length);

public sealed record DownloadExportPlan(IReadOnlyList<DownloadExportFile> Files)
{
    public long TotalBytes => Files.Sum(file => file.Length);

    public static DownloadExportPlan Create(string downloadPath, bool isDirectory, IReadOnlyList<TorrentFileChoice> choices)
    {
        var root = Path.GetFullPath(isDirectory ? downloadPath : Path.GetDirectoryName(downloadPath)!);
        var files = new List<DownloadExportFile>();
        foreach (var choice in choices.Where(file => file.IsSelected))
        {
            var path = isDirectory ? Path.Combine(root, choice.Path.Replace('/', Path.DirectorySeparatorChar)) : downloadPath;
            path = Path.GetFullPath(path);
            if (!PathGuard.IsPathWithinDirectory(path, root) || (!isDirectory && choices.Count != 1))
                throw new InvalidDataException("The selected file is outside its download folder.");
            // Do not follow a link into unrelated files, even when metadata names are valid.
            for (var current = path; current != root; current = Path.GetDirectoryName(current)!)
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Export cannot follow symbolic links.");
            var length = new FileInfo(path).Length;
            if (length != choice.Length) throw new InvalidDataException("The selected download is incomplete.");
            files.Add(new(path, Path.GetRelativePath(root, Path.GetDirectoryName(path)!), length));
        }
        if (files.Count == 0) throw new InvalidDataException("No completed files are selected.");
        return new(files);
    }
}

public enum DownloadExportPhase { Verifying, Copying }
public sealed record DownloadExportProgress(DownloadExportPhase Phase, long Bytes, long TotalBytes);
public sealed class DownloadExportSpaceException() : IOException("There is not enough free space to export this file.");
