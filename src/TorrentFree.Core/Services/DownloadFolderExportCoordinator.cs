namespace TorrentFree.Services;

public enum DownloadFolderExportResult { Completed, DownloadUnavailable, PermissionDenied, ExportFailed }

/// <summary>Opening public Downloads is useful only after a successful export.</summary>
public static class DownloadFolderExportCoordinator
{
    public static async Task<DownloadFolderExportResult> TryExportAndOpenAsync(Func<bool> downloadExists,
        Func<Task<bool>> requestPermission, Func<Task<string?>> export,
        Func<string, bool> openExportedFolder, Func<bool> openDownloads)
    {
        if (!downloadExists()) return DownloadFolderExportResult.DownloadUnavailable;
        try
        {
            if (!await requestPermission()) return DownloadFolderExportResult.PermissionDenied;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download export permission error: {ex}");
            return DownloadFolderExportResult.PermissionDenied;
        }
        try
        {
            return await ExportAndOpenAsync(export, openExportedFolder, openDownloads)
                ? DownloadFolderExportResult.Completed : DownloadFolderExportResult.ExportFailed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Download export error: {ex}");
            return DownloadFolderExportResult.ExportFailed;
        }
    }

    public static string? GetErrorResourceKey(DownloadFolderExportResult result) => result switch
    {
        DownloadFolderExportResult.Completed => null,
        DownloadFolderExportResult.ExportFailed => "ErrorExportDownload",
        _ => "ErrorOpenFolder"
    };

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
