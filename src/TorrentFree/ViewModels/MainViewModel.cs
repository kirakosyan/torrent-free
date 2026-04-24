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
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IStorageService _storageService;
    private readonly IFileAssociationService _fileAssociationService;
    private readonly INotificationService _notificationService;
    private bool _disposed;
    private bool _isLoadingSettings;
    private bool _processedCommandLine;
    private AppSettings _loadedSettings = new();
    private PeriodicTimer? _statsTimer;
    private CancellationTokenSource? _statsTimerCts;
    private CancellationTokenSource? _magnetAutoStartCts;
    private bool _statsTimerStarted;
    private bool _isInitializing;
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

    /// <summary>
    /// Indicates whether any torrent can be started or resumed.
    /// </summary>
    public bool CanStartAllTorrents => !IsBusy && Torrents.Any(torrent => torrent.CanStart);

    /// <summary>
    /// Indicates whether any torrent can be stopped.
    /// </summary>
    public bool CanStopAllTorrents => !IsBusy && Torrents.Any(torrent => torrent.CanStop);

    public MainViewModel(ITorrentService torrentService, ITorrentFilePicker torrentFilePicker, ITorrentFileParser torrentFileParser, IStorageService storageService, IFileAssociationService fileAssociationService, INotificationService notificationService)
    {
        _torrentService = torrentService;
        _torrentFilePicker = torrentFilePicker;
        _torrentFileParser = torrentFileParser;
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
        if (IsBusy || string.IsNullOrWhiteSpace(trimmed) || !_torrentService.IsValidMagnetLink(trimmed))
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
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnIsBusyChanged(bool value)
    {
        UpdateBulkActionState();
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        ApplyGlobalSpeedLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnMaxActiveDownloadsChanged(int value)
    {
        ApplyQueueLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnMaxActiveSeedsChanged(int value)
    {
        ApplyQueueLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalMaxSeedRatioChanged(double value)
    {
        ApplySeedingLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalMaxSeedMinutesChanged(int value)
    {
        ApplySeedingLimits();
        SafeFireAndForget(PersistSettingsAsync());
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
        ApplyProxySettings();
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

        var existingSettings = await _storageService.LoadSettingsAsync();
        var settings = AppSettingsFactory.CreateForMainPage(
            existingSettings,
            GlobalDownloadLimitKbps,
            GlobalUploadLimitKbps,
            MaxActiveDownloads,
            MaxActiveSeeds,
            GlobalMaxSeedRatio,
            GlobalMaxSeedMinutes,
            SortByStatus);

        _loadedSettings = settings;
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
                if (await TryOpenAndroidFolderAsync(downloadPath, folderPath, isDirectory))
                {
                    return;
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
            ErrorMessage = LocalizationResourceManager.Instance["ErrorOpenFolder"];
        }
    }

#if ANDROID
    private static async Task<bool> TryOpenAndroidFolderAsync(string downloadPath, string folderPath, bool isDirectory)
    {
        var targetFolder = isDirectory ? downloadPath : folderPath;
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        {
            return false;
        }

        if (TryOpenAndroidDocumentFolder(targetFolder))
        {
            return true;
        }

        if (!isDirectory && File.Exists(downloadPath))
        {
            try
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(downloadPath)
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Android file open fallback error: {ex}");
            }
        }

        return false;
    }

    private static bool TryOpenAndroidDocumentFolder(string folderPath)
    {
        var folderUri = BuildAndroidExternalStorageDocumentUri(folderPath);
        if (folderUri is null)
        {
            return false;
        }

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var packageManager = context.PackageManager;
        if (packageManager is null)
        {
            return false;
        }

        var viewIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        viewIntent.SetDataAndType(folderUri, Android.Provider.DocumentsContract.Document.MimeTypeDir);
        viewIntent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);

        if (viewIntent.ResolveActivity(packageManager) is not null)
        {
            context.StartActivity(viewIntent);
            return true;
        }

        var treeIntent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            treeIntent.PutExtra(Android.Provider.DocumentsContract.ExtraInitialUri, folderUri);
        }

        treeIntent.AddFlags(
            Android.Content.ActivityFlags.GrantReadUriPermission |
            Android.Content.ActivityFlags.GrantWriteUriPermission |
            Android.Content.ActivityFlags.GrantPersistableUriPermission |
            Android.Content.ActivityFlags.NewTask);

        if (treeIntent.ResolveActivity(packageManager) is null)
        {
            return false;
        }

        context.StartActivity(treeIntent);
        return true;
    }

    private static Android.Net.Uri? BuildAndroidExternalStorageDocumentUri(string folderPath)
    {
        var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(externalRoot))
        {
            return null;
        }

        var fullFolderPath = Path.GetFullPath(folderPath);
        if (!fullFolderPath.StartsWith(externalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(externalRoot, fullFolderPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        var documentId = relativePath == "."
            ? "primary:"
            : $"primary:{relativePath}";

        return Android.Net.Uri.Parse($"content://com.android.externalstorage.documents/document/{Android.Net.Uri.Encode(documentId)}");
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
            ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"];
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
    }

    private void DetachTorrentHandlers(TorrentItem torrent)
    {
        torrent.PropertyChanged -= OnTorrentPropertyChanged;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var torrent in DisplayTorrents)
        {
            torrent.RefreshLocalizableProperties();
        }
    }

    private void AttachTorrentCommands(TorrentItem torrent)
    {
        // Bind per-item UI buttons directly to these commands to avoid Source-based bindings in XAML.
        torrent.ShowInFolderCommand = ShowInFolderCommand;
        torrent.StartSpecificTorrentCommand = StartSpecificTorrentCommand;
        torrent.PauseSpecificTorrentCommand = PauseSpecificTorrentCommand;
        torrent.StopSpecificTorrentCommand = StopSpecificTorrentCommand;
        torrent.RemoveSpecificTorrentCommand = RemoveSpecificTorrentCommand;
    }

    private void OnTorrentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorrentItem.Status))
        {
            UpdateBulkActionState();
        }

        if (e.PropertyName == nameof(TorrentItem.Status) && SortByStatus)
        {
            SyncDisplayTorrents();
        }
    }

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
                .OrderBy(entry => entry.torrent.Status == DownloadStatus.Downloading ? 0 : 1)
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
        if (_hasInitialized || _isInitializing)
        {
            return;
        }

        _isInitializing = true;
        IsBusy = true;
        try
        {
            _isLoadingSettings = true;
            var settings = await _storageService.LoadSettingsAsync();
            _loadedSettings = settings;
            GlobalDownloadLimitKbps = settings.GlobalDownloadLimitKbps;
            GlobalUploadLimitKbps = settings.GlobalUploadLimitKbps;
            MaxActiveDownloads = settings.MaxActiveDownloads;
            MaxActiveSeeds = settings.MaxActiveSeeds;
            GlobalMaxSeedRatio = settings.GlobalMaxSeedRatio;
            GlobalMaxSeedMinutes = settings.GlobalMaxSeedMinutes;
            SortByStatus = settings.SortByStatus;

            ApplyGlobalSettings();
            await _torrentService.InitializeAsync();
            StartStatsTimer();
            _hasInitialized = true;

            SafeFireAndForget(_notificationService.EnsurePermissionAsync());
            SafeFireAndForget(PromptFileAssociationAsync());
            SafeFireAndForget(ProcessCommandLineArgumentsAsync());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization error: {ex}");
            ErrorMessage = LocalizationResourceManager.Instance["ErrorLoadDownloads"];
        }
        finally
        {
            _isLoadingSettings = false;
            IsBusy = false;
            _isInitializing = false;
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
                ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"];
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

        await ApplyTorrentFileMetadataAsync(torrent, filePath, fileName);
        await _torrentService.StartTorrentAsync(torrent);
        return true;
    }

    private async Task ApplyTorrentFileMetadataAsync(TorrentItem torrent, string? filePath, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            torrent.TorrentFilePath = filePath;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            torrent.TorrentFileName = fileName;
        }

        var settings = await _storageService.LoadSettingsAsync();
        var fallbackDownloadPath = string.IsNullOrWhiteSpace(torrent.SavePath)
            ? _storageService.GetDefaultDownloadPath()
            : torrent.SavePath;

        torrent.SavePath = DownloadLocationResolver.ResolveSavePath(settings, filePath, fallbackDownloadPath);
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
            LocalizationResourceManager.Instance["AssociateTorrentTitle"],
            LocalizationResourceManager.Instance["AssociateTorrentMessage"],
            LocalizationResourceManager.Instance["Yes"],
            LocalizationResourceManager.Instance["No"]);

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
            var result = await _torrentService.AddTorrentAsync(MagnetLinkInput.Trim());
            if (result != null)
            {
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

        var torrentToRemove = SelectedTorrent;
        SelectedTorrent = null;
        await RemoveTorrentCoreAsync(torrentToRemove, setBusy: true);
    }

    private bool CanRemoveTorrent() => SelectedTorrent != null;

    /// <summary>
    /// Starts a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
    private Task StartSpecificTorrentAsync(TorrentItem torrent) =>
        torrent?.CanStart == true ? StartTorrentCoreAsync(torrent, setBusy: false) : Task.CompletedTask;

    /// <summary>
    /// Pauses a specific torrent (used from UI list buttons).
    /// </summary>
    [RelayCommand]
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

        if (SelectedTorrent == torrent)
        {
            SelectedTorrent = null;
        }
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
        try
        {
            var result = await ShowDeleteDialogAsync(torrent);
            if (result is null)
            {
                return;
            }

            await _torrentService.RemoveTorrentAsync(torrent, result.DeleteTorrentFile, result.DeleteDownloadedFiles);
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
        _statsTimerCts = new CancellationTokenSource();
        _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        _ = Task.Run(async () =>
        {
            try
            {
                while (_statsTimer is not null && await _statsTimer.WaitForNextTickAsync(_statsTimerCts.Token))
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
