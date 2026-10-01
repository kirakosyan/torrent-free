namespace TorrentFree.Services;

/// <summary>Opening public Downloads is useful only after a successful export.</summary>
public static class DownloadFolderExportCoordinator
{
    public static async Task<bool> ExportAndOpenAsync(Func<Task<string?>> export,
        Func<string, bool> openExportedFolder, Func<bool> openDownloads)
    {
        var folder = await export();
        if (string.IsNullOrWhiteSpace(folder)) return false;
        if (!openExportedFolder(folder)) openDownloads();
        // The copy succeeded even when the device has no folder viewer.
        return true;
    }
}
