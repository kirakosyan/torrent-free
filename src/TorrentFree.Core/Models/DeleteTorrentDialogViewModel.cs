using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFree.Models;

public partial class DeleteTorrentDialogViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string TorrentName { get; set; }

    [ObservableProperty]
    public partial bool DeleteTorrentFile { get; set; }

    [ObservableProperty]
    public partial bool DeleteDownloadedFiles { get; set; }

    public DeleteTorrentDialogViewModel(string torrentName)
    {
        TorrentName = torrentName;
        // Each delete confirmation starts with both options selected; users can clear either to keep those files.
        DeleteTorrentFile = true;
        DeleteDownloadedFiles = true;
    }

    /// <summary>
    /// Always allow the delete/remove action. When both checkboxes are unchecked
    /// the torrent is removed from the list without deleting any files.
    /// </summary>
    public bool CanDelete => true;
}
