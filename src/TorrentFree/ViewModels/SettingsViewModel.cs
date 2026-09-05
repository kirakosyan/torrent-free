using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ILocalizationService _localizationService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IThemeService _themeService;
    private AppSettings _loadedSettings = new();
    private bool _isLoadingSettings = true;
    private bool _isNormalizing;
    private bool _isUpdatingAssociation;
    private bool _isSyncingThemeOptions;

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
    /// When enabled, .torrent imports download next to the source file.
    /// </summary>
    [ObservableProperty]
    public partial bool DownloadToTorrentFolder { get; set; } = true;

    /// <summary>
    /// Custom download folder used when torrent-folder mode is disabled.
    /// </summary>
    [ObservableProperty]
    public partial string SpecificDownloadFolder { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if SOCKS5 proxy is enabled.
    /// </summary>
    [ObservableProperty]
    public partial bool ProxyEnabled { get; set; }

    /// <summary>
    /// SOCKS5 proxy host address.
    /// </summary>
    [ObservableProperty]
    public partial string ProxyHost { get; set; } = string.Empty;

    /// <summary>
    /// SOCKS5 proxy port.
    /// </summary>
    [ObservableProperty]
    public partial int ProxyPort { get; set; } = 1080;

    /// <summary>
    /// SOCKS5 proxy username (optional).
    /// </summary>
    [ObservableProperty]
    public partial string ProxyUsername { get; set; } = string.Empty;

    /// <summary>
    /// SOCKS5 proxy password (optional).
    /// </summary>
    [ObservableProperty]
    public partial string ProxyPassword { get; set; } = string.Empty;

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

    /// <summary>
    /// Available language options. Empty string means system default.
    /// </summary>
    public ObservableCollection<LanguageOption> AvailableLanguages { get; } =
    [
        new("System Default", ""),
        new("English", "en"),
        new("\u0627\u0644\u0639\u0631\u0628\u064A\u0629", "ar"),
        new("\u7B80\u4F53\u4E2D\u6587", "zh-CN"),
        new("\u010Ce\u0161tina", "cs-CZ"),
        new("Dansk", "da-DK"),
        new("Nederlands", "nl-NL"),
        new("Suomi", "fi-FI"),
        new("Deutsch", "de-DE"),
        new("Magyar", "hu-HU"),
        new("Bahasa Indonesia", "id"),
        new("Italiano", "it-IT"),
        new("\u65E5\u672C\u8A9E", "ja-JP"),
        new("\uD55C\uAD6D\uC5B4", "ko-KR"),
        new("Norsk", "nb-NO"),
        new("Polski", "pl-PL"),
        new("Portugu\u00EAs (Brasil)", "pt-BR"),
        new("Rom\u00E2n\u0103", "ro"),
        new("\u0E44\u0E17\u0E22", "th"),
        new("Ti\u1EBFng Vi\u1EC7t", "vi"),
        new("Español", "es"),
        new("Français", "fr"),
        new("Türkçe", "tr"),
        new("हिन्दी", "hi"),
        new("Русский", "ru"),
    ];

    /// <summary>
    /// The currently selected language option.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; } = null!;

    /// <summary>
    /// Available theme options: follow system, light, or dark.
    /// </summary>
    public ObservableCollection<ThemeOption> AvailableThemes { get; } = [];

    /// <summary>
    /// The currently selected theme option.
    /// </summary>
    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; } = null!;

    /// <summary>
    /// Indicates whether the custom download folder field should be shown.
    /// </summary>
    public bool UseSpecificDownloadFolder => !DownloadToTorrentFolder;

    /// <summary>
    /// Indicates whether a native folder picker button can be shown.
    /// </summary>
    public bool IsFolderPickerSupported => _folderPickerService.IsSupported;

    /// <summary>
    /// Indicates whether the settings page is running on Android.
    /// </summary>
    public bool IsAndroid
    {
        get
        {
#if ANDROID
            return true;
#else
            return false;
#endif
        }
    }

    public SettingsViewModel(IStorageService storageService, ITorrentService torrentService, IFileAssociationService fileAssociationService, ILocalizationService localizationService, IFolderPickerService folderPickerService, IThemeService themeService)
    {
        _storageService = storageService;
        _torrentService = torrentService;
        _fileAssociationService = fileAssociationService;
        _localizationService = localizationService;
        _folderPickerService = folderPickerService;
        _themeService = themeService;
        IsFileAssociationSupported = _fileAssociationService.IsSupported;
        SelectedLanguage = AvailableLanguages[0];
        RebuildThemeOptions(ThemeSettings.System);

        // Theme option labels are localized, so rebuild them when the language changes.
        LocalizationResourceManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RebuildThemeOptions(SelectedTheme?.Code ?? ThemeSettings.System);
    }

    /// <summary>
    /// Rebuilds the theme picker items so their (localized) display names refresh,
    /// preserving the current selection by code. The picker re-renders because its
    /// items source changes.
    /// </summary>
    private void RebuildThemeOptions(string selectedCode)
    {
        _isSyncingThemeOptions = true;
        try
        {
            AvailableThemes.Clear();
            AvailableThemes.Add(new ThemeOption(ThemeSettings.System, "ThemeFollowSystem"));
            AvailableThemes.Add(new ThemeOption(ThemeSettings.Light, "ThemeLight"));
            AvailableThemes.Add(new ThemeOption(ThemeSettings.Dark, "ThemeDark"));
            SelectedTheme = AvailableThemes.FirstOrDefault(option => option.Code == selectedCode)
                            ?? AvailableThemes[0];
        }
        finally
        {
            _isSyncingThemeOptions = false;
        }
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try { await LoadSettingsCoreAsync(); }
        catch (Exception ex)
        {
            _isLoadingSettings = true;
            ValidationMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Settings initialization failed: {ex}");
        }
    }

    private async Task LoadSettingsCoreAsync()
    {
        _isLoadingSettings = true;
        var settings = await AppSettingsPersistence.LoadAsync(_storageService);
        _loadedSettings = settings;

        GlobalDownloadLimitKbps = settings.GlobalDownloadLimitKbps;
        GlobalUploadLimitKbps = settings.GlobalUploadLimitKbps;
        MaxActiveDownloads = settings.MaxActiveDownloads;
        MaxActiveSeeds = settings.MaxActiveSeeds;
        GlobalMaxSeedRatio = settings.GlobalMaxSeedRatio;
        GlobalMaxSeedMinutes = settings.GlobalMaxSeedMinutes;
        DownloadToTorrentFolder = settings.DownloadToTorrentFolder;
        SpecificDownloadFolder = settings.SpecificDownloadFolder?.Trim() ?? string.Empty;
        ProxyEnabled = settings.ProxyEnabled;
        ProxyHost = settings.ProxyHost ?? string.Empty;
        ProxyPort = settings.ProxyPort is > 0 and <= 65535 ? settings.ProxyPort : 1080;
        ProxyUsername = settings.ProxyUsername ?? string.Empty;
        ProxyPassword = settings.ProxyPassword ?? string.Empty;

        var languageCode = NormalizeLanguageCode(settings.Language ?? "");
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == languageCode)
                           ?? AvailableLanguages[0];

        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Code == ThemeSettings.Normalize(settings.Theme))
                        ?? AvailableThemes[0];

        _isLoadingSettings = false;
        await RefreshFileAssociationAsync();
        NormalizeAllSettings();
        ApplySettingsToService();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalDownloadLimitKbpsChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalDownloadLimitKbps), value, 0, MaxKbpsLimit, LocalizationResourceManager.Instance["DownloadKBs"], " KB/s", out var normalized))
        {
            GlobalDownloadLimitKbps = normalized;
            return;
        }

        ApplySpeedLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalUploadLimitKbpsChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalUploadLimitKbps), value, 0, MaxKbpsLimit, LocalizationResourceManager.Instance["UploadKBs"], " KB/s", out var normalized))
        {
            GlobalUploadLimitKbps = normalized;
            return;
        }

        ApplySpeedLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnMaxActiveDownloadsChanged(int value)
    {
        if (TryNormalizeInt(nameof(MaxActiveDownloads), value, 0, MaxActiveLimit, LocalizationResourceManager.Instance["MaxActiveDownloads"], "", out var normalized))
        {
            MaxActiveDownloads = normalized;
            return;
        }

        ApplyQueueLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnMaxActiveSeedsChanged(int value)
    {
        if (TryNormalizeInt(nameof(MaxActiveSeeds), value, 0, MaxActiveLimit, LocalizationResourceManager.Instance["MaxActiveSeeds"], "", out var normalized))
        {
            MaxActiveSeeds = normalized;
            return;
        }

        ApplyQueueLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalMaxSeedRatioChanged(double value)
    {
        if (TryNormalizeSeedRatio(value, out var normalized))
        {
            GlobalMaxSeedRatio = normalized;
            return;
        }

        ApplySeedingLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnGlobalMaxSeedMinutesChanged(int value)
    {
        if (TryNormalizeInt(nameof(GlobalMaxSeedMinutes), value, 0, MaxSeedMinutesLimit, LocalizationResourceManager.Instance["MaxSeedMinutes"], " min", out var normalized))
        {
            GlobalMaxSeedMinutes = normalized;
            return;
        }

        ApplySeedingLimits();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnDownloadToTorrentFolderChanged(bool value)
    {
        OnPropertyChanged(nameof(UseSpecificDownloadFolder));

        if (!value && string.IsNullOrWhiteSpace(SpecificDownloadFolder))
        {
            SpecificDownloadFolder = _storageService.GetDefaultDownloadPath();
        }

        if (_isLoadingSettings) return;
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnSpecificDownloadFolderChanged(string value)
    {
        if (_isLoadingSettings) return;
        SafeFireAndForget(PersistSettingsAsync());
    }

    [RelayCommand]
    private async Task BrowseDownloadFolderAsync()
    {
        try
        {
            var selectedFolder = await _folderPickerService.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                return;
            }

            SpecificDownloadFolder = selectedFolder;
            ValidationMessage = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Folder picker error: {ex}");
            ValidationMessage = LocalizationResourceManager.Instance["ValidationSelectFolder"];
        }
    }

    [RelayCommand]
    private Task OpenAndroidDownloadsFolderAsync()
    {
#if ANDROID
        try
        {
            AndroidDownloadExportService.EnsurePublicDownloadsFolder();
            if (!AndroidDownloadExportService.TryOpenPublicDownloadsFolder())
            {
                ValidationMessage = LocalizationResourceManager.Instance["ErrorOpenFolder"];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open Android downloads folder error: {ex}");
            ValidationMessage = LocalizationResourceManager.Instance["ErrorOpenFolder"];
        }
#endif
        return Task.CompletedTask;
    }

    partial void OnProxyEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyProxySettings();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnProxyHostChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyProxySettings();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnProxyPortChanged(int value)
    {
        if (TryNormalizeInt(nameof(ProxyPort), value, 1, 65535, LocalizationResourceManager.Instance["Port"], "", out var normalized))
        {
            ProxyPort = normalized;
            return;
        }

        if (_isLoadingSettings) return;
        ApplyProxySettings();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnProxyUsernameChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyProxySettings();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnProxyPasswordChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyProxySettings();
        SafeFireAndForget(PersistSettingsAsync());
    }

    partial void OnIsTorrentAssociatedChanged(bool value)
    {
        if (_isLoadingSettings || _isUpdatingAssociation || !IsFileAssociationSupported)
        {
            return;
        }

        SafeFireAndForget(ToggleFileAssociationAsync(value));
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_isLoadingSettings || value is null) return;

        var culture = string.IsNullOrEmpty(value.Code)
            ? LocalizationResourceManager.OriginalSystemCulture
            : new CultureInfo(value.Code);

        _localizationService.SetCulture(culture);
        SafeFireAndForget(PersistSettingsAsync());
    }

    /// <summary>
    /// Maps legacy/aliased culture codes saved by older builds to the canonical code
    /// used by the current language list. The deprecated Norwegian alias "no"/"no-NO"
    /// is canonicalized to "nb-NO" (Bokmål); otherwise the code is returned unchanged.
    /// </summary>
    private static string NormalizeLanguageCode(string code) => code switch
    {
        "no-NO" or "no" => "nb-NO",
        _ => code,
    };

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (_isLoadingSettings || _isSyncingThemeOptions || value is null) return;

        _themeService.Apply(value.Code);
        SafeFireAndForget(PersistSettingsAsync());
    }

    private void ApplySettingsToService()
    {
        ApplySpeedLimits();
        ApplyQueueLimits();
        ApplySeedingLimits();
        ApplyProxySettings();
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

    private void ApplyProxySettings()
    {
        _torrentService.UpdateProxySettings(ProxyEnabled, ProxyHost, ProxyPort, ProxyUsername, ProxyPassword);
    }

    private async Task PersistSettingsAsync()
    {
        if (_isLoadingSettings || _isNormalizing)
        {
            return;
        }

        // Merge with the latest snapshot under the shared view-model update lock so a
        // concurrent sort toggle cannot be replaced by this page's older snapshot.
        _loadedSettings = await AppSettingsPersistence.MergeAndSaveAsync(
            _storageService,
            existingSettings => AppSettingsFactory.CreateForSettingsPage(
                existingSettings,
                GlobalDownloadLimitKbps,
                GlobalUploadLimitKbps,
                MaxActiveDownloads,
                MaxActiveSeeds,
                GlobalMaxSeedRatio,
                GlobalMaxSeedMinutes,
                DownloadToTorrentFolder,
                SpecificDownloadFolder,
                ProxyEnabled,
                ProxyHost,
                ProxyPort,
                ProxyUsername,
                ProxyPassword,
                SelectedLanguage?.Code,
                SelectedTheme?.Code));
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
            ValidationMessage = LocalizationResourceManager.Instance["ValidationFileAssociation"];
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

        var proxyPort = NormalizeInt(ProxyPort, 1, 65535);
        adjusted |= proxyPort != ProxyPort;
        ProxyPort = proxyPort;

        ValidationMessage = adjusted ? LocalizationResourceManager.Instance["ValidationAdjustedLimits"] : null;

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
                ? string.Format(LocalizationResourceManager.Instance["ValidationNegative"], label, min, unit)
                : string.Format(LocalizationResourceManager.Instance["ValidationTooHigh"], label, max, unit);
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
                ? LocalizationResourceManager.Instance["ValidationSeedRatioInvalid"]
                : string.Format(LocalizationResourceManager.Instance["ValidationSeedRatioHigh"], MaxSeedRatioLimit);
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

    /// <summary>
    /// Runs the task without awaiting, logging any exceptions instead of crashing.
    /// </summary>
    private async void SafeFireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fire-and-forget error: {ex}");
            ValidationMessage = ex.Message;
        }
    }
}

/// <summary>
/// Represents a selectable language in the settings picker.
/// </summary>
public record LanguageOption(string DisplayName, string Code)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a selectable theme in the settings picker. The display name is resolved
/// from localized resources so it follows the active language.
/// </summary>
public sealed class ThemeOption(string code, string resourceKey)
{
    public string Code { get; } = code;

    public string DisplayName => LocalizationResourceManager.Instance[resourceKey];

    public override string ToString() => DisplayName;
}
