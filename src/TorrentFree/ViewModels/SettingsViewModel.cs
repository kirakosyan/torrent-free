using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree.ViewModels;

/// <summary>
/// View model for app settings.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IStorageService _storageService;
    private readonly ITorrentService _torrentService;
    private bool _isLoadingSettings;

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

    public SettingsViewModel(IStorageService storageService, ITorrentService torrentService)
    {
        _storageService = storageService;
        _torrentService = torrentService;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        _isLoadingSettings = true;
        var settings = await _storageService.LoadSettingsAsync();

        GlobalDownloadLimitKbps = settings.GlobalDownloadLimitKbps;
        GlobalUploadLimitKbps = settings.GlobalUploadLimitKbps;
        MaxActiveDownloads = settings.MaxActiveDownloads;
        MaxActiveSeeds = settings.MaxActiveSeeds;
        GlobalMaxSeedRatio = settings.GlobalMaxSeedRatio;
        GlobalMaxSeedMinutes = settings.GlobalMaxSeedMinutes;

        _isLoadingSettings = false;
        ApplySettingsToService();
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        ApplySpeedLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        ApplySpeedLimits();
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

    private void ApplySettingsToService()
    {
        ApplySpeedLimits();
        ApplyQueueLimits();
        ApplySeedingLimits();
    }

    private void ApplySpeedLimits()
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
}