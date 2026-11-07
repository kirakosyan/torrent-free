using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Services;

namespace TorrentFree.Models;

public partial class DownloadSelectionViewModel : ObservableObject
{
    private readonly DownloadControlsState _downloadControls = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedTorrentDetailsCommand))]
    public partial TorrentItem? SelectedTorrent { get; set; }

    public bool ShowDownloadControls => _downloadControls.ShowDownloadControls;
    public bool ShowSelectedTorrentDetails => _downloadControls.ShowSelectedTorrentDetails;
    public bool CanShowSelectedTorrentDetails => _downloadControls.CanShowSelectedTorrentDetails;

    public string DownloadControlsButtonText => LocalizationResourceManager.Instance[
        ShowDownloadControls ? "HideDownloadControls" : "ShowDownloadControls"];

    public string DownloadControlsAccessibilityHint => string.Join(", ",
        LocalizationResourceManager.Instance["Bandwidth"],
        LocalizationResourceManager.Instance["StartAllButton"],
        LocalizationResourceManager.Instance["StopAllButton"],
        LocalizationResourceManager.Instance["DownloadingOnTop"]);

    partial void OnSelectedTorrentChanged(TorrentItem? value)
    {
        _downloadControls.SetSelection(value is not null);
        NotifyDownloadControlsStateChanged();
        OnTorrentSelectionChanged();
    }

    protected virtual void OnTorrentSelectionChanged() { }

    private void NotifyDownloadControlsStateChanged()
    {
        OnPropertyChanged(nameof(ShowDownloadControls));
        OnPropertyChanged(nameof(ShowSelectedTorrentDetails));
        OnPropertyChanged(nameof(CanShowSelectedTorrentDetails));
        OnPropertyChanged(nameof(DownloadControlsButtonText));
    }

    public void ShowDownloadsForNotification(TorrentItem? torrent)
    {
        if (torrent is null) return;
        OnOpeningDownloadNotification(torrent);
        SelectedTorrent = torrent;
        if (_downloadControls.HideSelectedTorrentDetails())
            NotifyDownloadControlsStateChanged();
    }

    protected virtual void OnOpeningDownloadNotification(TorrentItem torrent) { }

    [RelayCommand]
    private void ToggleDownloadControls()
    {
        _downloadControls.ToggleDownloadControls();
        NotifyDownloadControlsStateChanged();
    }

    private bool CanToggleSelectedTorrentDetails() => _downloadControls.CanToggleSelectedTorrentDetails;

    [RelayCommand(CanExecute = nameof(CanToggleSelectedTorrentDetails))]
    private void ToggleSelectedTorrentDetails()
    {
        _downloadControls.ToggleSelectedTorrentDetails();
        NotifyDownloadControlsStateChanged();
    }

    [RelayCommand]
    private void ShowSpecificTorrentLimits(TorrentItem? torrent)
    {
        if (torrent is null) return;
        SelectedTorrent = torrent;
        if (_downloadControls.RevealSelectedTorrentDetails())
            NotifyDownloadControlsStateChanged();
    }
}
