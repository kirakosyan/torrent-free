using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System;
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
    private const int MaxChartPoints = 60;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFilePicker _torrentFilePicker;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IStorageService _storageService;
    private readonly IFileAssociationService _fileAssociationService;
    private readonly INotificationService _notificationService;
    private bool _disposed;
    private bool _isLoadingSettings;
    private bool _processedCommandLine;
    private PeriodicTimer? _statsTimer;
    private CancellationTokenSource? _statsTimerCts;
    private bool _statsTimerStarted;

    /// <summary>
    /// Collection of all torrent items.
    /// </summary>
    public ObservableCollection<TorrentItem> Torrents => _torrentService.Torrents;

    /// <summary>
    /// Collection of torrents shown in the UI (can be sorted).
    /// </summary>
    public ObservableCollection<TorrentItem> DisplayTorrents { get; } = [];

    /// <summary>
    /// Global download speed history in KB/s.
    /// </summary>
    public ObservableCollection<double> GlobalDownloadHistory { get; } = [];

    /// <summary>
    /// Global upload speed history in KB/s.
    /// </summary>
    public ObservableCollection<double> GlobalUploadHistory { get; } = [];

    /// <summary>
    /// The magnet link input by the user.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTorrentCommand))]
    public partial string MagnetLinkInput { get; set; } = string.Empty;

    /// <summary>
    /// Currently selected torrent item.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTorrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTorrentCommand))]
    public partial TorrentItem? SelectedTorrent { get; set; }

    /// <summary>
    /// Indicates if the view model is busy with an operation.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// When enabled, downloading torrents are shown on top.
    /// </summary>
    [ObservableProperty]
    public partial bool SortByStatus { get; set; }

    /// <summary>
    /// Error message to display to the user.
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Global download limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int GlobalDownloadLimitKbps { get; set; }

    /// <summary>
    /// Global upload limit in KB/s (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int GlobalUploadLimitKbps { get; set; }

    /// <summary>
    /// Max concurrent active downloads (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int MaxActiveDownloads { get; set; } = 2;

    /// <summary>
    /// Max concurrent active seeds (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int MaxActiveSeeds { get; set; } = 2;

    /// <summary>
    /// Global max seed ratio (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial double GlobalMaxSeedRatio { get; set; }

    /// <summary>
    /// Global max seed time in minutes (0 = unlimited).
    /// </summary>
    [ObservableProperty]
    public partial int GlobalMaxSeedMinutes { get; set; }

    /// <summary>
    /// Controls visibility of selected torrent details.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSelectedTorrentDetails { get; set; }

    /// <summary>
    /// Indicates if selected torrent details should be shown.
    /// </summary>
    public bool CanShowSelectedTorrentDetails => ShowSelectedTorrentDetails && SelectedTorrent != null;

    /// <summary>
    /// Indicates if there are no torrents in the list.
    /// </summary>
    public bool IsEmpty => Torrents.Count == 0;

    public MainViewModel(ITorrentService torrentService, ITorrentFilePicker torrentFilePicker, ITorrentFileParser torrentFileParser, IStorageService storageService, IFileAssociationService fileAssociationService, INotificationService notificationService)
    {
        _torrentService = torrentService;
        _torrentFilePicker = torrentFilePicker;
        _torrentFileParser = torrentFileParser;
        _storageService = storageService;
        _fileAssociationService = fileAssociationService;
        _notificationService = notificationService;
        Torrents.CollectionChanged += OnTorrentsCollectionChanged;

        InitializeDisplayTorrents();

        ApplyGlobalSettings();
    }

    partial void OnSortByStatusChanged(bool value)
    {
        SyncDisplayTorrents();
        _ = PersistSettingsAsync();
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
            GlobalMaxSeedMinutes = GlobalMaxSeedMinutes,
            SortByStatus = SortByStatus
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

#if WINDOWS
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
#endif
#if MACCATALYST
            if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
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
#endif

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
            await TryAddTorrentFromMetadataAsync(
                metadata,
                picked.FullPath,
                picked.FileName,
                notifyDuplicate: true,
                notifyInvalid: true);
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
        UpdateTorrentHandlers(e);
        SyncDisplayTorrents();
    }

    private void InitializeDisplayTorrents()
    {
        foreach (var torrent in Torrents)
        {
            AttachTorrentHandlers(torrent);
        }

        SyncDisplayTorrents();
    }

    private void UpdateTorrentHandlers(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            foreach (var existing in DisplayTorrents)
            {
                DetachTorrentHandlers(existing);
            }

            foreach (var torrent in Torrents)
            {
                AttachTorrentHandlers(torrent);
            }

            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var oldItem in e.OldItems.OfType<TorrentItem>())
            {
                DetachTorrentHandlers(oldItem);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newItem in e.NewItems.OfType<TorrentItem>())
            {
                AttachTorrentHandlers(newItem);
            }
        }
    }

    private void AttachTorrentHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged += OnTorrentPropertyChanged;
    }

    private void DetachTorrentHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged -= OnTorrentPropertyChanged;
    }

    private void OnTorrentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorrentItem.Status) && SortByStatus)
        {
            SyncDisplayTorrents();
        }
    }

    private void SyncDisplayTorrents()
    {
        var ordered = SortByStatus
            ? Torrents
                .Select((torrent, index) => new { torrent, index })
                .OrderBy(entry => entry.torrent.Status == DownloadStatus.Downloading ? 0 : 1)
                .ThenBy(entry => entry.torrent.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.torrent)
            : Torrents.AsEnumerable();

        DisplayTorrents.Clear();
        var index = 0;
        foreach (var torrent in ordered)
        {
            torrent.DisplayIndex = index++;
            DisplayTorrents.Add(torrent);
        }
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
            SortByStatus = settings.SortByStatus;

            ApplyGlobalSettings();
            await _notificationService.EnsurePermissionAsync();
            await _torrentService.InitializeAsync();

            await PromptFileAssociationAsync();
            await ProcessCommandLineArgumentsAsync();
            StartStatsTimer();
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

    public Task ImportTorrentFileFromPathAsync(string filePath)
    {
        return TryAddTorrentFromFilePathAsync(filePath, notifyDuplicate: false, notifyInvalid: true);
    }

    private async Task ProcessCommandLineArgumentsAsync()
    {
        if (_processedCommandLine)
        {
            return;
        }

        _processedCommandLine = true;

        var args = Environment.GetCommandLineArgs();
        if (args.Length <= 1)
        {
            return;
        }

        foreach (var arg in args.Skip(1))
        {
            if (!arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await TryAddTorrentFromFilePathAsync(arg, notifyDuplicate: false, notifyInvalid: true);
        }
    }

    private async Task<bool> TryAddTorrentFromFilePathAsync(string filePath, bool notifyDuplicate, bool notifyInvalid)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var content = await File.ReadAllBytesAsync(filePath);
            var metadata = _torrentFileParser.Parse(content);
            return await TryAddTorrentFromMetadataAsync(
                metadata,
                filePath,
                Path.GetFileName(filePath),
                notifyDuplicate,
                notifyInvalid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Torrent add error: {ex}");
            if (notifyInvalid)
            {
                ErrorMessage = "Failed to import .torrent file. Please try again.";
            }
            return false;
        }
    }

    private async Task<bool> TryAddTorrentFromMetadataAsync(TorrentMetadata metadata, string? filePath, string? fileName, bool notifyDuplicate, bool notifyInvalid)
    {
        TorrentItem? torrent = null;
        try
        {
            torrent = await _torrentService.AddTorrentFileAsync(metadata);
        }
        catch (DuplicateTorrentException)
        {
            if (notifyDuplicate)
            {
                ErrorMessage = "This torrent is already in your list.";
            }
            return false;
        }

        if (torrent is null)
        {
            if (notifyInvalid)
            {
                ErrorMessage = "Invalid .torrent file. Unable to extract an info hash.";
            }
            return false;
        }

        ApplyTorrentFileMetadata(torrent, filePath, fileName);
        await _torrentService.StartTorrentAsync(torrent);
        return true;
    }

    private static void ApplyTorrentFileMetadata(TorrentItem torrent, string? filePath, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            torrent.TorrentFilePath = filePath;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            torrent.TorrentFileName = fileName;
        }

        var folder = !string.IsNullOrWhiteSpace(filePath)
            ? Path.GetDirectoryName(filePath)
            : null;

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            torrent.SavePath = folder;
        }
    }

    private async Task PromptFileAssociationAsync()
    {
        if (!_fileAssociationService.IsSupported)
        {
            return;
        }

        const string promptKey = "torrent.association.prompted";
        if (Preferences.Default.Get(promptKey, false))
        {
            return;
        }

        Preferences.Default.Set(promptKey, true);

        if (Shell.Current is null)
        {
            return;
        }

        var shouldAssociate = await Shell.Current.DisplayAlertAsync(
            "Associate .torrent files",
            "Do you want to open .torrent files with Torrent Free by default?",
            "Yes",
            "No");

        if (shouldAssociate)
        {
            await _fileAssociationService.AssociateAsync();
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
            var result = await ShowDeleteDialogAsync(torrentToRemove);
            if (result is null)
            {
                return;
            }

            SelectedTorrent = null;
            await _torrentService.RemoveTorrentAsync(torrentToRemove, result.DeleteTorrentFile, result.DeleteDownloadedFiles);
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
            var result = await ShowDeleteDialogAsync(torrent);
            if (result is null)
            {
                return;
            }

            await _torrentService.RemoveTorrentAsync(torrent, result.DeleteTorrentFile, result.DeleteDownloadedFiles);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remove specific torrent error: {ex}");
            ErrorMessage = "Failed to remove torrent. Please try again.";
        }
    }

    private static async Task<DeleteTorrentDialogResult?> ShowDeleteDialogAsync(TorrentItem torrent)
    {
        if (Shell.Current?.Navigation is null)
        {
            return null;
        }

        var dialog = new DeleteTorrentDialogPage(torrent.Name);
        await Shell.Current.Navigation.PushModalAsync(dialog);
        return await dialog.Result;
    }

    private void StartStatsTimer()
    {
        if (_statsTimerStarted)
        {
            return;
        }

        _statsTimerStarted = true;
        _statsTimerCts = new CancellationTokenSource();
        _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        _ = Task.Run(async () =>
        {
            try
            {
                while (_statsTimer is not null && await _statsTimer.WaitForNextTickAsync(_statsTimerCts.Token))
                {
                    var totalDownload = Torrents.Sum(t => t.DownloadSpeed);
                    var totalUpload = Torrents.Sum(t => t.UploadSpeed);

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        AppendSample(GlobalDownloadHistory, totalDownload / 1024d);
                        AppendSample(GlobalUploadHistory, totalUpload / 1024d);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // timer canceled
            }
        });
    }

    private void StopStatsTimer()
    {
        _statsTimerCts?.Cancel();
        _statsTimerCts?.Dispose();
        _statsTimerCts = null;

        _statsTimer?.Dispose();
        _statsTimer = null;
    }

    private static void AppendSample(ObservableCollection<double> samples, double value)
    {
        samples.Add(Math.Max(0, value));
        while (samples.Count > MaxChartPoints)
        {
            samples.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopStatsTimer();
        foreach (var torrent in DisplayTorrents)
        {
            DetachTorrentHandlers(torrent);
        }
        Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
        GC.SuppressFinalize(this);
    }
}
