using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree.ViewModels;

/// <summary>
/// Main view model for the torrent client.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFilePicker _torrentFilePicker;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IStorageService _storageService;
    private bool _disposed;
    private bool _isLoadingSettings;

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
    /// Global download limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private int globalDownloadLimitKbps;

    /// <summary>
    /// Global upload limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private int globalUploadLimitKbps;

    /// <summary>
    /// Max concurrent active downloads (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private int maxActiveDownloads = 2;

    /// <summary>
    /// Max concurrent active seeds (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private int maxActiveSeeds = 2;

    /// <summary>
    /// Global max seed ratio (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private double globalMaxSeedRatio;

    /// <summary>
    /// Global max seed time in minutes (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    private int globalMaxSeedMinutes;

    /// <summary>
    /// Controls visibility of selected torrent details.
    /// </summary>
    [ObservableProperty]
    private bool showSelectedTorrentDetails;

    /// <summary>
    /// Indicates if selected torrent details should be shown.
    /// </summary>
    public bool CanShowSelectedTorrentDetails => ShowSelectedTorrentDetails && SelectedTorrent != null;

    /// <summary>
    /// Indicates if there are no torrents in the list.
    /// </summary>
    public bool IsEmpty => Torrents.Count == 0;

    public MainViewModel(ITorrentService torrentService, ITorrentFilePicker torrentFilePicker, ITorrentFileParser torrentFileParser, IStorageService storageService)
    {
        _torrentService = torrentService;
        _torrentFilePicker = torrentFilePicker;
        _torrentFileParser = torrentFileParser;
        _storageService = storageService;
        Torrents.CollectionChanged += OnTorrentsCollectionChanged;

        ApplyGlobalSettings();
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnMaxActiveDownloadsChanged(int value)
    {
        ApplyQueueLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnMaxActiveSeedsChanged(int value)
    {
        ApplyQueueLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalMaxSeedRatioChanged(double value)
    {
        ApplySeedingLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalMaxSeedMinutesChanged(int value)
    {
        ApplySeedingLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnShowSelectedTorrentDetailsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowSelectedTorrentDetails));
    }

    partial void OnSelectedTorrentChanged(TorrentItem? value)
    {
        ShowSelectedTorrentDetails = false;
        OnPropertyChanged(nameof(CanShowSelectedTorrentDetails));
    }

    private void ApplyGlobalSettings()
    {
        ApplyGlobalSpeedLimits();
        ApplyQueueLimits();
        ApplySeedingLimits();
    }

    private void ApplyGlobalSpeedLimits()
    {
        _torrentService.UpdateGlobalSpeedLimits(GlobalDownloadLimitKbps, GlobalUploadLimitKbps);
    }

    private void ApplyQueueLimits()
    {
        _torrentService.UpdateQueueLimits(MaxActiveDownloads, MaxActiveSeeds);
    }

    private void ApplySeedingLimits()
    {
        _torrentService.UpdateSeedingLimits(GlobalMaxSeedRatio, GlobalMaxSeedMinutes);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync("SettingsPage");
    }

    [RelayCommand]
    private void ToggleSelectedTorrentDetails()
    {
        ShowSelectedTorrentDetails = !ShowSelectedTorrentDetails;
    }

    private async Task PersistSettingsAsync()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        var settings = new AppSettings
        {
            GlobalDownloadLimitKbps = GlobalDownloadLimitKbps,
            GlobalUploadLimitKbps = GlobalUploadLimitKbps,
            MaxActiveDownloads = MaxActiveDownloads,
            MaxActiveSeeds = MaxActiveSeeds,
            GlobalMaxSeedRatio = GlobalMaxSeedRatio,
            GlobalMaxSeedMinutes = GlobalMaxSeedMinutes
        };

        await _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    private async Task ShowInFolderAsync(TorrentItem torrent)
    {
        if (torrent is null || !torrent.CanOpenDownloadedFile)
        {
            return;
        }

        try
        {
            var downloadPath = torrent.DownloadedFilePath;
            var isDirectory = Directory.Exists(downloadPath);

            // For directories, we want to open the folder itself
            // For files, we want to open the containing folder and select the file
            var targetPath = isDirectory ? downloadPath : downloadPath;
            var folderPath = isDirectory ? downloadPath : Path.GetDirectoryName(downloadPath);

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                try
                {
                    if (isDirectory)
                    {
                        // For directories, just open the folder
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{downloadPath}\"",
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        // For files, open File Explorer and select the file
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{downloadPath}\"",
                            UseShellExecute = true
                        });
                    }
                    return;
                }
                catch
                {
                    // Fallback below
                }
            }
            else if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            {
                try
                {
                    if (isDirectory)
                    {
                        // For directories, just open the folder
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "open",
                            Arguments = $"\"{downloadPath}\"",
                            UseShellExecute = false
                        });
                    }
                    else
                    {
                        // For files, reveal in Finder
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "open",
                            Arguments = $"-R \"{downloadPath}\"",
                            UseShellExecute = false
                        });
                    }
                    return;
                }
                catch
                {
                    // Fallback below
                }
            }

            // Best-effort fallback: open the folder (for directories, open directly; for files, open containing folder)
            var targetFolder = isDirectory ? downloadPath : folderPath;
            if (Directory.Exists(targetFolder))
            {
                await Launcher.Default.OpenAsync(new Uri(targetFolder));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Show in folder error: {ex}");
            ErrorMessage = "Failed to open folder for the download.";
        }
    }

    /// <summary>
    /// Lets the user pick a local .torrent file, converts it to a magnet link, and starts the download.
    /// </summary>
    [RelayCommand]
    private async Task BrowseTorrentFileAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var picked = await _torrentFilePicker.PickTorrentFileAsync();
            if (picked is null)
            {
                return;
            }

            var metadata = _torrentFileParser.Parse(picked.Content);
            TorrentItem? torrent = null;
            try
            {
                torrent = await _torrentService.AddTorrentFileAsync(metadata);
                if (torrent is null)
                {
                    ErrorMessage = "Invalid .torrent file. Unable to extract an info hash.";
                    return;
                }
            }
            catch (DuplicateTorrentException)
            {
                ErrorMessage = "This torrent is already in your list.";
                return;
            }

            var folder = !string.IsNullOrWhiteSpace(picked.FullPath)
                ? Path.GetDirectoryName(picked.FullPath)
                : null;

            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                torrent.SavePath = folder;
            }

            await _torrentService.StartTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Browse torrent file error: {ex}");
            ErrorMessage = "Failed to import .torrent file. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnTorrentsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
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
            _isLoadingSettings = true;
            var settings = await _storageService.LoadSettingsAsync();
            GlobalDownloadLimitKbps = settings.GlobalDownloadLimitKbps;
            GlobalUploadLimitKbps = settings.GlobalUploadLimitKbps;
            MaxActiveDownloads = settings.MaxActiveDownloads;
            MaxActiveSeeds = settings.MaxActiveSeeds;
            GlobalMaxSeedRatio = settings.GlobalMaxSeedRatio;
            GlobalMaxSeedMinutes = settings.GlobalMaxSeedMinutes;

            ApplyGlobalSettings();
            await _torrentService.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization error: {ex}");
            ErrorMessage = "Failed to load your downloads. Please restart the app.";
        }
        finally
        {
            _isLoadingSettings = false;
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
                ErrorMessage = "Invalid magnet link. Please ensure your link starts with 'magnet:?' and contains an info hash (xt= parameter).";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add torrent error: {ex}");
            ErrorMessage = "Failed to add torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Start torrent error: {ex}");
            ErrorMessage = "Failed to start torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Pause torrent error: {ex}");
            ErrorMessage = "Failed to pause torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Stop torrent error: {ex}");
            ErrorMessage = "Failed to stop torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Remove torrent error: {ex}");
            ErrorMessage = "Failed to remove torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Start specific torrent error: {ex}");
            ErrorMessage = "Failed to start torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Pause specific torrent error: {ex}");
            ErrorMessage = "Failed to pause torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Stop specific torrent error: {ex}");
            ErrorMessage = "Failed to stop torrent. Please try again.";
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
            System.Diagnostics.Debug.WriteLine($"Remove specific torrent error: {ex}");
            ErrorMessage = "Failed to remove torrent. Please try again.";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
        GC.SuppressFinalize(this);
    }
}
