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
    private readonly IFileAssociationService _fileAssociationService;
    private bool _isLoadingSettings;
    private bool _isNormalizing;
    private bool _isUpdatingAssociation;

    private const int MaxKbpsLimit = 1_000_000;
    private const int MaxActiveLimit = 200;
    private const double MaxSeedRatioLimit = 100;
    private const int MaxSeedMinutesLimit = 525_600;

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
    /// Validation message shown to the user.
    /// </summary>
    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    /// <summary>
    /// Indicates if file association is supported on this platform.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFileAssociationSupported { get; set; }

    /// <summary>
    /// Indicates if .torrent files are associated with the app.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTorrentAssociated { get; set; }

    public SettingsViewModel(IStorageService storageService, ITorrentService torrentService, IFileAssociationService fileAssociationService)
    {
        _storageService = storageService;
        _torrentService = torrentService;
        _fileAssociationService = fileAssociationService;
        IsFileAssociationSupported = _fileAssociationService.IsSupported;
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
        await RefreshFileAssociationAsync();
        NormalizeAllSettings();
        ApplySettingsToService();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalDownloadLimitKbps), value, 0, MaxKbpsLimit, "Download limit", " KB/s", out var normalized))
        {
            GlobalDownloadLimitKbps = normalized;
            return;
        }

        ApplySpeedLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalUploadLimitKbps), value, 0, MaxKbpsLimit, "Upload limit", " KB/s", out var normalized))
        {
            GlobalUploadLimitKbps = normalized;
            return;
        }

        ApplySpeedLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnMaxActiveDownloadsChanged(int value)
    {
        if (TryNormalizeInt(nameof(MaxActiveDownloads), value, 0, MaxActiveLimit, "Max active downloads", "", out var normalized))
        {
            MaxActiveDownloads = normalized;
            return;
        }

        ApplyQueueLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnMaxActiveSeedsChanged(int value)
    {
        if (TryNormalizeInt(nameof(MaxActiveSeeds), value, 0, MaxActiveLimit, "Max active seeds", "", out var normalized))
        {
            MaxActiveSeeds = normalized;
            return;
        }

        ApplyQueueLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalMaxSeedRatioChanged(double value)
    {
        if (TryNormalizeSeedRatio(value, out var normalized))
        {
            GlobalMaxSeedRatio = normalized;
            return;
        }

        ApplySeedingLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnGlobalMaxSeedMinutesChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalMaxSeedMinutes), value, 0, MaxSeedMinutesLimit, "Max seed minutes", " min", out var normalized))
        {
            GlobalMaxSeedMinutes = normalized;
            return;
        }

        ApplySeedingLimits();
        _ = PersistSettingsAsync();
    }

    partial void OnIsTorrentAssociatedChanged(bool value)
    {
        if (_isLoadingSettings || _isUpdatingAssociation || !IsFileAssociationSupported)
        {
            return;
        }

        _ = ToggleFileAssociationAsync(value);
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
        if (_isLoadingSettings || _isNormalizing)
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

    private async Task RefreshFileAssociationAsync()
    {
        if (!IsFileAssociationSupported)
        {
            return;
        }

        _isUpdatingAssociation = true;
        IsTorrentAssociated = await _fileAssociationService.IsAssociatedAsync();
        _isUpdatingAssociation = false;
    }

    private async Task ToggleFileAssociationAsync(bool enable)
    {
        if (!IsFileAssociationSupported)
        {
            return;
        }

        _isUpdatingAssociation = true;
        var result = enable
            ? await _fileAssociationService.AssociateAsync()
            : await _fileAssociationService.RemoveAssociationAsync();
        _isUpdatingAssociation = false;

        if (!result)
        {
            ValidationMessage = "Unable to update file association.";
        }

        await RefreshFileAssociationAsync();
    }

    private void NormalizeAllSettings()
    {
        _isNormalizing = true;

        var adjusted = false;

        var downloadLimit = NormalizeInt(GlobalDownloadLimitKbps, 0, MaxKbpsLimit);
        adjusted |= downloadLimit != GlobalDownloadLimitKbps;
        GlobalDownloadLimitKbps = downloadLimit;

        var uploadLimit = NormalizeInt(GlobalUploadLimitKbps, 0, MaxKbpsLimit);
        adjusted |= uploadLimit != GlobalUploadLimitKbps;
        GlobalUploadLimitKbps = uploadLimit;

        var maxDownloads = NormalizeInt(MaxActiveDownloads, 0, MaxActiveLimit);
        adjusted |= maxDownloads != MaxActiveDownloads;
        MaxActiveDownloads = maxDownloads;

        var maxSeeds = NormalizeInt(MaxActiveSeeds, 0, MaxActiveLimit);
        adjusted |= maxSeeds != MaxActiveSeeds;
        MaxActiveSeeds = maxSeeds;

        var seedRatio = NormalizeSeedRatio(GlobalMaxSeedRatio);
        adjusted |= Math.Abs(seedRatio - GlobalMaxSeedRatio) > double.Epsilon;
        GlobalMaxSeedRatio = seedRatio;

        var seedMinutes = NormalizeInt(GlobalMaxSeedMinutes, 0, MaxSeedMinutesLimit);
        adjusted |= seedMinutes != GlobalMaxSeedMinutes;
        GlobalMaxSeedMinutes = seedMinutes;

        ValidationMessage = adjusted ? "Some settings were adjusted to safe limits." : null;

        _isNormalizing = false;
    }

    private bool TryNormalizeInt(string propertyName, int value, int min, int max, string label, string unit, out int normalized)
    {
        normalized = NormalizeInt(value, min, max);

        if (_isLoadingSettings || _isNormalizing)
        {
            return false;
        }

        if (normalized != value)
        {
            _isNormalizing = true;
            ValidationMessage = value < min
                ? $"{label} cannot be negative. Set to {min}{unit}."
                : $"{label} is too high. Capped at {max}{unit}.";
            _isNormalizing = false;
            return true;
        }

        ValidationMessage = null;
        return false;
    }

    private bool TryNormalizeSeedRatio(double value, out double normalized)
    {
        normalized = NormalizeSeedRatio(value);

        if (_isLoadingSettings || _isNormalizing)
        {
            return false;
        }

        if (Math.Abs(normalized - value) > double.Epsilon)
        {
            _isNormalizing = true;
            ValidationMessage = double.IsNaN(value) || double.IsInfinity(value) || value < 0
                ? "Seed ratio must be a valid non-negative number. Reset to 0."
                : $"Seed ratio is too high. Capped at {MaxSeedRatioLimit}.";
            _isNormalizing = false;
            return true;
        }

        ValidationMessage = null;
        return false;
    }

    private static int NormalizeInt(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static double NormalizeSeedRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        if (value > MaxSeedRatioLimit)
        {
            return MaxSeedRatioLimit;
        }

        return value;
    }
}