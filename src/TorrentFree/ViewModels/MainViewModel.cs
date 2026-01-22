using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree.ViewModels;

/// <summary>
/// Main view model for the torrent client.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITorrentService _torrentService;

    /// <summary>
    /// Collection of all torrent items.
    /// </summary>
    public ObservableCollection<TorrentItem> Torrents => _torrentService.Torrents;

    /// <summary>
    /// The magnet link input by the user.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTorrentCommand))]
    private string magnetLinkInput = string.Empty;

    /// <summary>
    /// Currently selected torrent item.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTorrentCommand))]
    private TorrentItem? selectedTorrent;

    /// <summary>
    /// Indicates if the view model is busy with an operation.
    /// </summary>
    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// Error message to display to the user.
    /// </summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>
    /// Indicates if there are no torrents in the list.
    /// </summary>
    public bool IsEmpty => Torrents.Count == 0;

    public MainViewModel(ITorrentService torrentService)
    {
        _torrentService = torrentService;
        Torrents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Initializes the view model.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _torrentService.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Adds a new torrent from the magnet link input.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddTorrent))]
    private async Task AddTorrentAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _torrentService.AddTorrentAsync(MagnetLinkInput.Trim());
            if (result != null)
            {
                MagnetLinkInput = string.Empty;
                // Auto-start the download
                await _torrentService.StartTorrentAsync(result);
            }
            else
            {
                ErrorMessage = "Invalid magnet link. Please enter a valid magnet:? URL.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add torrent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddTorrent()
    {
        return !string.IsNullOrWhiteSpace(MagnetLinkInput) &&
               _torrentService.IsValidMagnetLink(MagnetLinkInput.Trim());
    }

    /// <summary>
    /// Starts or resumes the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTorrent))]
    private async Task StartTorrentAsync()
    {
        if (SelectedTorrent == null) return;

        IsBusy = true;
        try
        {
            await _torrentService.StartTorrentAsync(SelectedTorrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start torrent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStartTorrent() => SelectedTorrent?.CanStart ?? false;

    /// <summary>
    /// Pauses the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPauseTorrent))]
    private async Task PauseTorrentAsync()
    {
        if (SelectedTorrent == null) return;

        IsBusy = true;
        try
        {
            await _torrentService.PauseTorrentAsync(SelectedTorrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to pause torrent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanPauseTorrent() => SelectedTorrent?.CanPause ?? false;

    /// <summary>
    /// Stops the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopTorrent))]
    private async Task StopTorrentAsync()
    {
        if (SelectedTorrent == null) return;

        IsBusy = true;
        try
        {
            await _torrentService.StopTorrentAsync(SelectedTorrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to stop torrent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStopTorrent() => SelectedTorrent?.CanStop ?? false;

    /// <summary>
    /// Removes the selected torrent from the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveTorrent))]
    private async Task RemoveTorrentAsync()
    {
        if (SelectedTorrent == null) return;

        IsBusy = true;
        try
        {
            var torrentToRemove = SelectedTorrent;
            SelectedTorrent = null;
            await _torrentService.RemoveTorrentAsync(torrentToRemove);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove torrent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRemoveTorrent() => SelectedTorrent != null;

    /// <summary>
    /// Starts a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private async Task StartSpecificTorrentAsync(TorrentItem torrent)
    {
        if (torrent == null || !torrent.CanStart) return;

        try
        {
            await _torrentService.StartTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start torrent: {ex.Message}";
        }
    }

    /// <summary>
    /// Pauses a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private async Task PauseSpecificTorrentAsync(TorrentItem torrent)
    {
        if (torrent == null || !torrent.CanPause) return;

        try
        {
            await _torrentService.PauseTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to pause torrent: {ex.Message}";
        }
    }

    /// <summary>
    /// Stops a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private async Task StopSpecificTorrentAsync(TorrentItem torrent)
    {
        if (torrent == null || !torrent.CanStop) return;

        try
        {
            await _torrentService.StopTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to stop torrent: {ex.Message}";
        }
    }

    /// <summary>
    /// Removes a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private async Task RemoveSpecificTorrentAsync(TorrentItem torrent)
    {
        if (torrent == null) return;

        try
        {
            if (SelectedTorrent == torrent)
            {
                SelectedTorrent = null;
            }
            await _torrentService.RemoveTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove torrent: {ex.Message}";
        }
    }
}
