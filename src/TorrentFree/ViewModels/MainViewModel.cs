using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
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
    private readonly TorrentImportService _torrentImportService;
    private readonly IStorageService _storageService;
    private readonly IFileAssociationService _fileAssociationService;
    private readonly INotificationService _notificationService;
    private readonly ILibroNestLauncher _libroNestLauncher;
    private readonly AudiobookAvailabilityCache _audiobookAvailability = new();
    private readonly DownloadControlsState _downloadControls = new();
    private bool _disposed;
    private bool _isLoadingSettings;
    private bool _processedCommandLine;
    private AppSettings _loadedSettings = new();
    private PeriodicTimer? _statsTimer;
    private CancellationTokenSource? _statsTimerCts;
    private CancellationTokenSource? _magnetAutoStartCts;
    private bool _magnetRequiresExplicitStart;
    private bool _statsTimerStarted;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _hasInitialized;

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
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedTorrentDetailsCommand))]
    public partial TorrentItem? SelectedTorrent { get; set; }

    /// <summary>
    /// Indicates if the view model is busy with an operation.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// When enabled, torrents are grouped by status priority.
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
    /// Controls visibility of bandwidth and download controls. Starts collapsed each session.
    /// </summary>
    public bool ShowDownloadControls => _downloadControls.ShowDownloadControls;

    public string DownloadControlsButtonText => LocalizationResourceManager.Instance[
        ShowDownloadControls ? "HideDownloadControls" : "ShowDownloadControls"];

    public string DownloadControlsAccessibilityHint => string.Join(", ",
        LocalizationResourceManager.Instance["Bandwidth"],
        LocalizationResourceManager.Instance["StartAllButton"],
        LocalizationResourceManager.Instance["StopAllButton"],
        LocalizationResourceManager.Instance["DownloadingOnTop"]);

    /// <summary>
    /// Controls visibility of selected torrent details.
    /// </summary>
    public bool ShowSelectedTorrentDetails => _downloadControls.ShowSelectedTorrentDetails;

    /// <summary>
    /// Indicates if selected torrent details should be shown.
    /// </summary>
    public bool CanShowSelectedTorrentDetails => _downloadControls.CanShowSelectedTorrentDetails;

    /// <summary>
    /// Indicates if there are no torrents in the list.
    /// </summary>
    public bool IsEmpty => Torrents.Count == 0;

    public bool IsInitialized => _hasInitialized;

    public bool RequiresMagnetConfirmation => _magnetRequiresExplicitStart;

    /// <summary>
    /// Indicates whether any torrent can be started or resumed.
    /// </summary>
    public bool CanStartAllTorrents => !IsBusy && Torrents.Any(torrent => torrent.CanStart);

    /// <summary>
    /// Indicates whether any torrent can be stopped.
    /// </summary>
    public bool CanStopAllTorrents => !IsBusy && Torrents.Any(torrent => torrent.CanStop);

    public AppPromptService Prompts { get; }

    public MainViewModel(ITorrentService torrentService, ITorrentFilePicker torrentFilePicker, IStorageService storageService, IFileAssociationService fileAssociationService, INotificationService notificationService, TorrentImportService torrentImportService, AppPromptService prompts, ILibroNestLauncher libroNestLauncher)
    {
        _libroNestLauncher = libroNestLauncher;
        Prompts = prompts;
        _torrentService = torrentService;
        _torrentFilePicker = torrentFilePicker;
        _torrentImportService = torrentImportService;
        _storageService = storageService;
        _fileAssociationService = fileAssociationService;
        _notificationService = notificationService;
        Torrents.CollectionChanged += OnTorrentsCollectionChanged;

        LocalizationResourceManager.Instance.PropertyChanged += OnLocalizationChanged;

        InitializeDisplayTorrents();

        ApplyGlobalSettings();
    }

    partial void OnMagnetLinkInputChanged(string value)
    {
        CancelPendingMagnetAutoStart();

        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            _magnetRequiresExplicitStart = false;
            OnPropertyChanged(nameof(RequiresMagnetConfirmation));
        }

        if (_magnetRequiresExplicitStart || IsBusy || string.IsNullOrWhiteSpace(trimmed) || !_torrentService.IsValidMagnetLink(trimmed))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _magnetAutoStartCts = cts;
        SafeFireAndForget(AutoStartMagnetInputAsync(trimmed, cts.Token));
    }

    partial void OnSortByStatusChanged(bool value)
    {
        SyncDisplayTorrents();
        SafeFireAndForget(PersistSortPreferenceAsync());
    }

    partial void OnIsBusyChanged(bool value)
    {
        UpdateBulkActionState();
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
    }

    partial void OnMaxActiveDownloadsChanged(int value)
    {
        ApplyQueueLimits();
    }

    partial void OnMaxActiveSeedsChanged(int value)
    {
        ApplyQueueLimits();
    }

    partial void OnGlobalMaxSeedRatioChanged(double value)
    {
        ApplySeedingLimits();
    }

    partial void OnGlobalMaxSeedMinutesChanged(int value)
    {
        ApplySeedingLimits();
    }

    partial void OnSelectedTorrentChanged(TorrentItem? value)
    {
        _downloadControls.SetSelection(value is not null);
        NotifyDownloadControlsStateChanged();
    }

    private void NotifyDownloadControlsStateChanged()
    {
        OnPropertyChanged(nameof(ShowDownloadControls));
        OnPropertyChanged(nameof(ShowSelectedTorrentDetails));
        OnPropertyChanged(nameof(CanShowSelectedTorrentDetails));
        OnPropertyChanged(nameof(DownloadControlsButtonText));
    }

    private void ApplyGlobalSettings()
    {
        ApplyGlobalSpeedLimits();
        ApplyQueueLimits();
        ApplySeedingLimits();
        ApplyProxySettings();
        _torrentService.UpdateKeepDeviceAwake(_loadedSettings.KeepDeviceAwake);
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

    private void ApplyProxySettings()
    {
        _torrentService.UpdateProxySettings(
            _loadedSettings.ProxyEnabled,
            _loadedSettings.ProxyHost,
            _loadedSettings.ProxyPort,
            _loadedSettings.ProxyUsername,
            _loadedSettings.ProxyPassword);
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
        RevealSelectedTorrentDetails();
    }

    public bool RevealSelectedTorrentDetails()
    {
        if (!_downloadControls.RevealSelectedTorrentDetails()) return false;
        NotifyDownloadControlsStateChanged();
        return true;
    }

    private async Task PersistSortPreferenceAsync()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _loadedSettings = await AppSettingsPersistence.MergeAndSaveAsync(
            _storageService,
            existingSettings => AppSettingsFactory.CreateWithSortByStatus(existingSettings, SortByStatus));
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
#if ANDROID
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // Exporting copies the whole download to public storage, which can take a while.
                var wasBusy = IsBusy;
                IsBusy = true;
                try
                {
                    if (!await TryOpenAndroidFolderAsync(torrent.Id, downloadPath, folderPath, isDirectory))
                    {
                        ErrorMessage = LocalizationResourceManager.Instance["ErrorExportDownload"];
                    }
                }
                finally
                {
                    IsBusy = wasBusy;
                }

                return;
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
            ErrorMessage = LocalizationResourceManager.Instance["ErrorOpenFolder"];
        }
    }

#if ANDROID
    private static async Task<bool> TryOpenAndroidFolderAsync(string ownerId, string downloadPath, string folderPath, bool isDirectory)
    {
        var targetFolder = isDirectory ? downloadPath : folderPath;
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        {
            return false;
        }

        try
        {
            // Android 6-9 need the runtime storage permission to write to public Downloads.
            if (OperatingSystem.IsAndroidVersionAtLeast(29)
                || await Permissions.RequestAsync<Permissions.StorageWrite>() == PermissionStatus.Granted)
            {
                return await DownloadFolderExportCoordinator.ExportAndOpenAsync(
                    () => AndroidDownloadExportService.ExportToPublicDownloadsAsync(ownerId, downloadPath, isDirectory),
                    AndroidDownloadExportService.TryOpenFolder,
                    () => AndroidDownloadExportService.TryOpenPublicDownloadsFolder());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Android public Downloads export error: {ex}");
        }

        return false;
    }
#endif

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

            var metadata = await _torrentImportService.PrepareAsync(picked);
            await TryAddTorrentFromMetadataAsync(
                metadata,
                notifyDuplicate: true,
                notifyInvalid: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Browse torrent file error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnTorrentsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var selected = SelectedTorrent;
        if (selected is not null && !Torrents.Contains(selected))
            SelectedTorrent = Torrents.FirstOrDefault(torrent => torrent.Id == selected.Id);

        OnPropertyChanged(nameof(IsEmpty));
        UpdateTorrentHandlers(e);
        SyncDisplayTorrents();
        UpdateBulkActionState();
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
        AttachTorrentCommands(torrent);
        torrent.PropertyChanged += OnTorrentPropertyChanged;
        _ = RefreshAudiobookActionAsync(torrent);
    }

    private void DetachTorrentHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged -= OnTorrentPropertyChanged;
        _audiobookAvailability.Invalidate(torrent.DownloadedFilePath);
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DownloadControlsButtonText));
        OnPropertyChanged(nameof(DownloadControlsAccessibilityHint));

        foreach (var torrent in DisplayTorrents)
        {
            torrent.RefreshLocalizableProperties();
        }
    }

    private void AttachTorrentCommands(TorrentItem torrent)
    {
        // Bind per-item UI buttons directly to these commands to avoid Source-based bindings in XAML.
        torrent.ShowInFolderCommand = ShowInFolderCommand;
        torrent.OpenInLibroNestCommand = OpenInLibroNestCommand;
        torrent.StartSpecificTorrentCommand = StartSpecificTorrentCommand;
        torrent.PauseSpecificTorrentCommand = PauseSpecificTorrentCommand;
        torrent.StopSpecificTorrentCommand = StopSpecificTorrentCommand;
        torrent.RemoveSpecificTorrentCommand = RemoveSpecificTorrentCommand;
        torrent.ShowSpecificTorrentLimitsCommand = ShowSpecificTorrentLimitsCommand;
    }

    private void OnTorrentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TorrentItem item && e.PropertyName == nameof(TorrentItem.CanOpenDownloadedFile))
            _ = RefreshAudiobookActionAsync(item);

        if (e.PropertyName == nameof(TorrentItem.Status))
        {
            UpdateBulkActionState();
        }

        if (SortByStatus && e.PropertyName is nameof(TorrentItem.Status) or nameof(TorrentItem.Name))
        {
            SyncDisplayTorrents();
        }
    }

    private async Task RefreshAudiobookActionAsync(TorrentItem torrent)
    {
        if (!_libroNestLauncher.IsSupported || !torrent.CanOpenDownloadedFile)
        {
            torrent.CanOpenInLibroNest = false;
            if (torrent.Progress < 100 && torrent.DateCompleted is null
                && torrent.Status is not (DownloadStatus.Completed or DownloadStatus.Seeding))
                _audiobookAvailability.Invalidate(torrent.DownloadedFilePath);
            return;
        }
        var path = torrent.DownloadedFilePath;
        var scan = _audiobookAvailability.HasAudioAsync(path);
        try
        {
            var hasAudio = await scan;
            if (_audiobookAvailability.IsCurrent(path, scan) && IsCurrentAudiobookRequest(torrent, path))
                torrent.CanOpenInLibroNest = hasAudio;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_audiobookAvailability.IsCurrent(path, scan) && IsCurrentAudiobookRequest(torrent, path))
                torrent.CanOpenInLibroNest = false;
        }
    }

    [RelayCommand]
    private async Task OpenInLibroNestAsync(TorrentItem? torrent)
    {
        if (torrent is null || !_libroNestLauncher.IsSupported || !torrent.CanOpenDownloadedFile) return;
        try
        {
            ErrorMessage = null;
            var path = torrent.DownloadedFilePath;
            var files = await Task.Run(() => AudiobookFiles.Enumerate(path).ToArray());
            if (!IsCurrentAudiobookRequest(torrent, path)) return;
            await _libroNestLauncher.OpenAsync(path, files);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not open LibroNest: {exception}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorOpenLibroNest"];
            _audiobookAvailability.Invalidate(torrent.DownloadedFilePath);
            await RefreshAudiobookActionAsync(torrent);
        }
    }

    private bool IsCurrentAudiobookRequest(TorrentItem torrent, string path) =>
        !_disposed && Torrents.Contains(torrent) && torrent.CanOpenDownloadedFile && path == torrent.DownloadedFilePath;

    private void UpdateBulkActionState()
    {
        OnPropertyChanged(nameof(CanStartAllTorrents));
        OnPropertyChanged(nameof(CanStopAllTorrents));
        StartAllTorrentsCommand.NotifyCanExecuteChanged();
        StopAllTorrentsCommand.NotifyCanExecuteChanged();
    }

    private void SyncDisplayTorrents()
    {
        var ordered = SortByStatus
            ? Torrents
                .Select((torrent, index) => new { torrent, index })
                .OrderBy(entry => GetStatusSortOrder(entry.torrent.Status))
                .ThenBy(entry => entry.torrent.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.torrent)
                .ToList()
            : Torrents.ToList();

        // Reconcile in-place to minimise CollectionChanged events.
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].DisplayIndex = i;

            if (i < DisplayTorrents.Count)
            {
                if (!ReferenceEquals(DisplayTorrents[i], ordered[i]))
                {
                    var existingIndex = IndexOfRef(DisplayTorrents, ordered[i], i);
                    if (existingIndex >= 0)
                    {
                        DisplayTorrents.Move(existingIndex, i);
                    }
                    else
                    {
                        DisplayTorrents.Insert(i, ordered[i]);
                    }
                }
            }
            else
            {
                DisplayTorrents.Add(ordered[i]);
            }
        }

        // Remove any trailing items.
        while (DisplayTorrents.Count > ordered.Count)
        {
            DisplayTorrents.RemoveAt(DisplayTorrents.Count - 1);
        }
    }

    private static int GetStatusSortOrder(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading => 0,
        DownloadStatus.Seeding => 1,
        DownloadStatus.Paused => 2,
        DownloadStatus.Stopped => 3,
        DownloadStatus.Queued => 4,
        DownloadStatus.WaitingForWifi => 5,
        DownloadStatus.Completed => 6,
        DownloadStatus.Failed => 7,
        _ => 8
    };

    private static int IndexOfRef(ObservableCollection<TorrentItem> collection, TorrentItem item, int startIndex)
    {
        for (var i = startIndex; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], item))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Initializes the view model.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            await PromptFileAssociationAsync();
        }
        catch
        {
            // EnsureInitializedAsync has already logged the failure and exposed the
            // localized error. Keep the UI command retryable without surfacing an
            // unhandled async-command exception.
        }
    }

    /// <summary>
    /// Waits for first-time initialization to complete. Later calls refresh the settings
    /// snapshot before returning. File activation uses this method directly so it cannot
    /// race an InitializeCommand already running for the main page.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            if (_hasInitialized)
            {
                await RefreshSettingsAsync();
                return;
            }

            IsBusy = true;
            try
            {
                _isLoadingSettings = true;
                var settings = await AppSettingsPersistence.LoadAsync(_storageService);
                ApplyLoadedSettings(settings);

                ApplyGlobalSettings();
                await _torrentService.InitializeAsync();
                StartStatsTimer();
                _hasInitialized = true;

                // Restored transfers which were active when the app closed come back Queued.
                SafeFireAndForget(_torrentService.StartQueuedTorrentsAsync());

                SafeFireAndForget(_notificationService.EnsurePermissionAsync());
#if !WINDOWS
                SafeFireAndForget(ProcessCommandLineArgumentsAsync());
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Initialization error: {ex}");
                ErrorMessage = LocalizationResourceManager.Instance["ErrorLoadDownloads"];
                throw;
            }
            finally
            {
                _isLoadingSettings = false;
                IsBusy = false;
            }
        }
        finally
        {
            try
            {
                _initializationLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // The view model was disposed (e.g. page navigated away) while this call
                // was still finishing up; there is nothing left to coordinate.
            }
        }
    }

    private async Task RefreshSettingsAsync()
    {
        try
        {
            _isLoadingSettings = true;
            var currentSettings = await AppSettingsPersistence.LoadAsync(_storageService);
            ApplyLoadedSettings(currentSettings);
            ApplyGlobalSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings refresh error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorLoadDownloads"];
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void ApplyLoadedSettings(AppSettings settings)
    {
        _loadedSettings = settings;
        GlobalDownloadLimitKbps = settings.GlobalDownloadLimitKbps;
        GlobalUploadLimitKbps = settings.GlobalUploadLimitKbps;
        MaxActiveDownloads = settings.MaxActiveDownloads;
        MaxActiveSeeds = settings.MaxActiveSeeds;
        GlobalMaxSeedRatio = settings.GlobalMaxSeedRatio;
        GlobalMaxSeedMinutes = settings.GlobalMaxSeedMinutes;
        SortByStatus = settings.SortByStatus;
    }

    public Task ImportTorrentFileFromPathAsync(string filePath)
    {
        return TryAddTorrentFromFilePathAsync(filePath, notifyDuplicate: false, notifyInvalid: true);
    }

    /// <summary>
    /// Previews a magnet link opened from another app. The user must press Download before
    /// the link is added or any tracker/peer connections are made. Call on the UI thread.
    /// </summary>
    public async Task ImportMagnetLinkAsync(string magnetLink)
    {
        try
        {
            await EnsureInitializedAsync();
            var trimmed = magnetLink.Trim();
            if (!_torrentService.IsValidMagnetLink(trimmed))
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorInvalidMagnet"];
                return;
            }

            ErrorMessage = null;
            CancelPendingMagnetAutoStart();
            _magnetRequiresExplicitStart = true;
            MagnetLinkInput = trimmed;
            OnPropertyChanged(nameof(RequiresMagnetConfirmation));
            if (Shell.Current is { } shell)
                await shell.GoToAsync("//MainPage");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Magnet activation error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorAddTorrent"];
        }
    }

    /// <summary>
    /// Adds and starts .torrent content opened from another app, where no stable file path
    /// exists (for example an Android content URI). Call on the UI thread.
    /// </summary>
    public async Task ImportTorrentContentAsync(string fileName, byte[] content)
    {
        try
        {
            await EnsureInitializedAsync();
            var metadata = await _torrentImportService.PrepareAsync(new TorrentPickedFile(fileName, null, content));
            if (await TryAddTorrentFromMetadataAsync(metadata, notifyDuplicate: true, notifyInvalid: true))
            {
                ErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Torrent activation error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"];
        }
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

        var torrentPaths = args
            .Skip(1)
            .Where(static arg => arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase));

        await ActivationImportCoordinator.ImportAsync(
            torrentPaths,
            static () => Task.CompletedTask,
            ImportTorrentFileFromPathAsync);
    }

    private async Task<bool> TryAddTorrentFromFilePathAsync(string filePath, bool notifyDuplicate, bool notifyInvalid)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var content = await TorrentFileContentReader.ReadFromFileAsync(filePath);
            var metadata = await _torrentImportService.PrepareAsync(new TorrentPickedFile(Path.GetFileName(filePath), filePath, content));
            return await TryAddTorrentFromMetadataAsync(
                metadata,
                notifyDuplicate,
                notifyInvalid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Torrent add error: {ex}");
            if (notifyInvalid)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"];
            }
            return false;
        }
    }

    private async Task<bool> TryAddTorrentFromMetadataAsync(TorrentMetadata metadata, bool notifyDuplicate, bool notifyInvalid)
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
                ErrorMessage = LocalizationResourceManager.Instance["ErrorDuplicateTorrent"];
            }
            return false;
        }

        if (torrent is null)
        {
            if (notifyInvalid)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorInvalidTorrentFile"];
            }
            return false;
        }

        await _torrentService.StartTorrentAsync(torrent);
        return true;
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

        var shell = Shell.Current;
        if (shell is null)
        {
            return;
        }

        var shouldAssociate = await shell.DisplayAlertAsync(
            LocalizationResourceManager.Instance["AssociateTorrentTitle"],
            LocalizationResourceManager.Instance["AssociateTorrentMessage"],
            LocalizationResourceManager.Instance["Yes"],
            LocalizationResourceManager.Instance["No"]);

        Preferences.Default.Set(promptKey, true);

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
        CancelPendingMagnetAutoStart();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var submittedLink = MagnetLinkInput.Trim();
            var result = await _torrentService.AddTorrentAsync(submittedLink);
            if (result != null)
            {
                // A protocol activation may preview another link while this add is awaiting storage.
                if (string.Equals(MagnetLinkInput.Trim(), submittedLink, StringComparison.Ordinal))
                    MagnetLinkInput = string.Empty;
                // Auto-start the download
                await _torrentService.StartTorrentAsync(result);
            }
            else
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorInvalidMagnet"];
            }
        }
        catch (DuplicateTorrentException)
        {
            ErrorMessage = LocalizationResourceManager.Instance["ErrorDuplicateTorrent"];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add torrent error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorAddTorrent"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PasteMagnetLinkAsync()
    {
        try
        {
            if (!Clipboard.Default.HasText)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorClipboardEmpty"];
                return;
            }

            var clipboardText = (await Clipboard.Default.GetTextAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorClipboardEmpty"];
                return;
            }

            ErrorMessage = null;
            MagnetLinkInput = clipboardText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Paste magnet link error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorPasteClipboard"];
        }
    }

    private async Task AutoStartMagnetInputAsync(string magnetLink, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

            if (cancellationToken.IsCancellationRequested || IsBusy)
            {
                return;
            }

            if (!string.Equals(MagnetLinkInput.Trim(), magnetLink, StringComparison.Ordinal))
            {
                return;
            }

            if (!CanAddTorrent())
            {
                return;
            }

            await AddTorrentCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user keeps typing, clears the field, or submits manually.
        }
    }

    private void CancelPendingMagnetAutoStart()
    {
        _magnetAutoStartCts?.Cancel();
        _magnetAutoStartCts?.Dispose();
        _magnetAutoStartCts = null;
    }

    private bool CanAddTorrent()
    {
        return !string.IsNullOrWhiteSpace(MagnetLinkInput) &&
               _torrentService.IsValidMagnetLink(MagnetLinkInput.Trim());
    }

    /// <summary>
    /// Starts or resumes the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartAllTorrents))]
    private async Task StartAllTorrentsAsync()
    {
        var torrentsToStart = Torrents.Where(torrent => torrent.CanStart).ToList();
        if (torrentsToStart.Count == 0)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var failed = false;
            foreach (var torrent in torrentsToStart)
            {
                try
                {
                    await _torrentService.StartTorrentAsync(torrent);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Start all torrents error for '{torrent.Name}': {ex}");
                    failed = true;
                }
            }

            if (failed)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorStartTorrent"];
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Stops all active torrents.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopAllTorrents))]
    private async Task StopAllTorrentsAsync()
    {
        var torrentsToStop = Torrents.Where(torrent => torrent.CanStop).ToList();
        if (torrentsToStop.Count == 0)
        {
            return;
        }

        if (!await ConfirmStopAsync())
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var failed = false;
            foreach (var torrent in torrentsToStop)
            {
                try
                {
                    await _torrentService.StopTorrentAsync(torrent);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Stop all torrents error for '{torrent.Name}': {ex}");
                    failed = true;
                }
            }

            if (failed)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorStopTorrent"];
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Starts or resumes the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTorrent))]
    private async Task StartTorrentAsync()
    {
        if (SelectedTorrent == null) return;
        await StartTorrentCoreAsync(SelectedTorrent, setBusy: true);
    }

    private bool CanStartTorrent() => SelectedTorrent?.CanStart ?? false;

    /// <summary>
    /// Pauses the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPauseTorrent))]
    private async Task PauseTorrentAsync()
    {
        if (SelectedTorrent == null) return;
        await PauseTorrentCoreAsync(SelectedTorrent, setBusy: true);
    }

    private bool CanPauseTorrent() => SelectedTorrent?.CanPause ?? false;

    /// <summary>
    /// Stops the selected torrent download.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopTorrent))]
    private async Task StopTorrentAsync()
    {
        if (SelectedTorrent == null) return;
        await StopTorrentCoreAsync(SelectedTorrent, setBusy: true);
    }

    private bool CanStopTorrent() => SelectedTorrent?.CanStop ?? false;

    /// <summary>
    /// Removes the selected torrent from the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveTorrent))]
    private async Task RemoveTorrentAsync()
    {
        if (SelectedTorrent == null) return;

        // The selection is cleared only after the removal is confirmed and completed.
        await RemoveTorrentCoreAsync(SelectedTorrent, setBusy: true);
    }

    private bool CanRemoveTorrent() => SelectedTorrent != null;

    /// <summary>
    /// Starts a specific torrent (used from UI list buttons).
    /// </summary>
    // Shared by every row: without concurrent execution, one slow start disables all rows.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StartSpecificTorrentAsync(TorrentItem torrent) =>
        torrent?.CanStart == true ? StartTorrentCoreAsync(torrent, setBusy: false) : Task.CompletedTask;

    /// <summary>
    /// Pauses a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PauseSpecificTorrentAsync(TorrentItem torrent) =>
        torrent?.CanPause == true ? PauseTorrentCoreAsync(torrent, setBusy: false) : Task.CompletedTask;

    /// <summary>
    /// Stops a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private Task StopSpecificTorrentAsync(TorrentItem torrent) =>
        torrent?.CanStop == true ? StopTorrentCoreAsync(torrent, setBusy: false) : Task.CompletedTask;

    /// <summary>
    /// Removes a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private async Task RemoveSpecificTorrentAsync(TorrentItem torrent)
    {
        if (torrent == null) return;

        await RemoveTorrentCoreAsync(torrent, setBusy: false);
    }

    private async Task StartTorrentCoreAsync(TorrentItem torrent, bool setBusy)
    {
        if (setBusy) IsBusy = true;
        try
        {
            await _torrentService.StartTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Start torrent error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorStartTorrent"];
        }
        finally
        {
            if (setBusy) IsBusy = false;
        }
    }

    private async Task PauseTorrentCoreAsync(TorrentItem torrent, bool setBusy)
    {
        if (setBusy) IsBusy = true;
        try
        {
            await _torrentService.PauseTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Pause torrent error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorPauseTorrent"];
        }
        finally
        {
            if (setBusy) IsBusy = false;
        }
    }

    private async Task StopTorrentCoreAsync(TorrentItem torrent, bool setBusy)
    {
        if (!await ConfirmStopAsync())
        {
            return;
        }

        if (setBusy) IsBusy = true;
        try
        {
            await _torrentService.StopTorrentAsync(torrent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stop torrent error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorStopTorrent"];
        }
        finally
        {
            if (setBusy) IsBusy = false;
        }
    }

    private async Task RemoveTorrentCoreAsync(TorrentItem torrent, bool setBusy)
    {
        if (setBusy) IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await ShowDeleteDialogAsync(torrent);
            if (result is null)
            {
                return;
            }

            var removal = await _torrentService.RemoveTorrentAsync(torrent, result.DeleteTorrentFile, result.DeleteDownloadedFiles);
            if (removal.Removed && ReferenceEquals(SelectedTorrent, torrent))
            {
                SelectedTorrent = null;
            }

            if (removal.DownloadedFilesLeftInPlace)
            {
                ErrorMessage = LocalizationResourceManager.Instance["ErrorRemoveFilesLeftInPlace"];
            }
            if (removal.TorrentFileLeftInPlace)
            {
                var sourceWarning = LocalizationResourceManager.Instance["ErrorRemoveTorrentFileLeftInPlace"];
                ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage)
                    ? sourceWarning : ErrorMessage + Environment.NewLine + sourceWarning;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remove torrent error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorRemoveTorrent"];
        }
        finally
        {
            if (setBusy) IsBusy = false;
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

    /// <summary>
    /// Prompts the user to confirm the stop action.
    /// </summary>
    private static async Task<bool> ConfirmStopAsync()
    {
        if (Shell.Current is null)
        {
            return false;
        }

        return await Shell.Current.DisplayAlertAsync(
            LocalizationResourceManager.Instance["StopAndResetTitle"],
            LocalizationResourceManager.Instance["StopAndResetMessage"],
            LocalizationResourceManager.Instance["StopButton"],
            LocalizationResourceManager.Instance["Cancel"]);
    }

    private void StartStatsTimer()
    {
        if (_statsTimerStarted)
        {
            return;
        }

        _statsTimerStarted = true;
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _statsTimerCts = cts;
        _statsTimer = timer;

        _ = Task.Run(async () =>
        {
            try
            {
                // Capture cts/timer locally: StopStatsTimer nulls out (and disposes) the
                // instance fields, and reading them from this loop after that raced with
                // ObjectDisposedException/NullReferenceException. The local references stay
                // valid, and PeriodicTimer.WaitForNextTickAsync simply returns false once
                // disposed, ending the loop cleanly.
                while (await timer.WaitForNextTickAsync(token))
                {
                    // Torrents is an ObservableCollection owned by the UI thread; summing it
                    // on a background thread can observe mid-Add/Remove state and throw.
                    // Snapshot and sum on the main thread where the collection is mutated.
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        long totalDownload = 0;
                        long totalUpload = 0;
                        foreach (var t in Torrents)
                        {
                            totalDownload += t.DownloadSpeed;
                            totalUpload += t.UploadSpeed;
                        }

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
        CancelPendingMagnetAutoStart();
        StopStatsTimer();
        _initializationLock.Dispose();
        LocalizationResourceManager.Instance.PropertyChanged -= OnLocalizationChanged;
        foreach (var torrent in DisplayTorrents)
        {
            DetachTorrentHandlers(torrent);
        }
        Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs the task without awaiting, logging any exceptions instead of crashing.
    /// </summary>
    private static async void SafeFireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fire-and-forget error: {ex}");
        }
    }
}
