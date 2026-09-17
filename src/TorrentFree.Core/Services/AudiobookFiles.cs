namespace TorrentFree.Services;

public static class AudiobookFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4b", ".m4a", ".aac", ".flac", ".ogg", ".wav" };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));

    // Never follow links out of the selected download or share unrelated files.
    public static IEnumerable<string> Enumerate(string path)
    {
        if (File.Exists(path))
        {
            if (IsSupported(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                yield return path;
            yield break;
        }
        if (!Directory.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            yield break;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
            if (IsSupported(file)) yield return file;
    }
}
