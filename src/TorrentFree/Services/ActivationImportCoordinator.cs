namespace TorrentFree.Services;

internal static class ActivationImportCoordinator
{
    public static async Task ImportAsync(
        IEnumerable<string> paths,
        Func<Task> initializeAsync,
        Func<string, Task> importAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(initializeAsync);
        ArgumentNullException.ThrowIfNull(importAsync);

        var importPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (importPaths.Length == 0)
        {
            return;
        }

        await initializeAsync();

        foreach (var path in importPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await importAsync(path);
        }
    }
}