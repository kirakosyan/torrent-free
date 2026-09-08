namespace TorrentFree.Services;

internal static class PathGuard
{
    public static bool IsPathWithinDirectory(string candidatePath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        try
        {
            var fullCandidate = Path.GetFullPath(candidatePath);
            var fullBase = Path.GetFullPath(baseDirectory);
            var normalizedBase = EnsureTrailingSeparator(fullBase);
            return fullCandidate.StartsWith(normalizedBase, GetPathComparison());
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
