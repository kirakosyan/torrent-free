namespace TorrentFree.Models;

/// <summary>
/// Result of the delete torrent dialog.
/// </summary>
public sealed class DeleteTorrentDialogResult
{
    public DeleteTorrentDialogResult(bool deleteTorrentFile, bool deleteDownloadedFiles)
    {
        DeleteTorrentFile = deleteTorrentFile;
        DeleteDownloadedFiles = deleteDownloadedFiles;
    }

    public bool DeleteTorrentFile { get; }

    public bool DeleteDownloadedFiles { get; }
}
