namespace TorrentFree.Models;

/// <summary>Visibility and selection rules for the downloads controls and limits editor.</summary>
public sealed class DownloadControlsState
{
    public bool ShowDownloadControls { get; private set; }
    public bool ShowSelectedTorrentDetails { get; private set; }
    public bool HasSelection { get; private set; }

    public bool CanToggleSelectedTorrentDetails => HasSelection;
    public bool CanShowSelectedTorrentDetails => ShowSelectedTorrentDetails && HasSelection;
    public bool NeedsTorrentSelection(bool hasDownloads) => hasDownloads && !HasSelection;

    public void ToggleDownloadControls()
    {
        ShowDownloadControls = !ShowDownloadControls;
        if (!ShowDownloadControls)
            ShowSelectedTorrentDetails = false;
    }

    public void SetSelection(bool hasSelection)
    {
        HasSelection = hasSelection;
        if (!hasSelection)
            ShowSelectedTorrentDetails = false;
    }

    public void ToggleSelectedTorrentDetails()
    {
        if (!HasSelection) return;
        ShowDownloadControls = true;
        ShowSelectedTorrentDetails = !ShowSelectedTorrentDetails;
    }

    public bool RevealSelectedTorrentDetails()
    {
        if (!HasSelection) return false;
        ShowDownloadControls = true;
        ShowSelectedTorrentDetails = true;
        return true;
    }
}
