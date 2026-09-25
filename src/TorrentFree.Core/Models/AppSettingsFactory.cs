namespace TorrentFree.Models;

/// <summary>
/// Creates persisted app settings from view-model state while preserving unrelated fields.
/// </summary>
public static class AppSettingsFactory
{
    /// <summary>
    /// Builds a settings payload which changes only the main page's sort preference.
    /// Every settings-page-owned value comes from the latest persisted snapshot.
    /// </summary>
    public static AppSettings CreateWithSortByStatus(AppSettings existing, bool sortByStatus)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return new AppSettings
        {
            GlobalDownloadLimitKbps = existing.GlobalDownloadLimitKbps,
            GlobalUploadLimitKbps = existing.GlobalUploadLimitKbps,
            MaxActiveDownloads = existing.MaxActiveDownloads,
            MaxActiveSeeds = existing.MaxActiveSeeds,
            GlobalMaxSeedRatio = existing.GlobalMaxSeedRatio,
            GlobalMaxSeedMinutes = existing.GlobalMaxSeedMinutes,
            SortByStatus = sortByStatus,
            WifiOnly = existing.WifiOnly,
            DownloadToTorrentFolder = existing.DownloadToTorrentFolder,
            SpecificDownloadFolder = existing.SpecificDownloadFolder ?? string.Empty,
            ProxyEnabled = existing.ProxyEnabled,
            ProxyHost = existing.ProxyHost ?? string.Empty,
            ProxyPort = existing.ProxyPort is > 0 and <= 65535 ? existing.ProxyPort : 1080,
            ProxyUsername = existing.ProxyUsername ?? string.Empty,
            ProxyPassword = existing.ProxyPassword ?? string.Empty,
            ProxyPasswordUnavailable = existing.ProxyPasswordUnavailable,
            Language = existing.Language,
            Theme = ThemeSettings.Normalize(existing.Theme),
            DesktopWasMaximized = existing.DesktopWasMaximized,
            KeepDeviceAwake = existing.KeepDeviceAwake
        };
    }

    /// <summary>
    /// Builds the settings payload for the settings page save flow.
    /// </summary>
    /// <param name="proxyPasswordUnavailable">
    /// True while the page shows no password because secure storage could not return it and
    /// the user has not entered one; the latest persisted password is then kept.
    /// </param>
    public static AppSettings CreateForSettingsPage(
        AppSettings existing,
        int globalDownloadLimitKbps,
        int globalUploadLimitKbps,
        int maxActiveDownloads,
        int maxActiveSeeds,
        double globalMaxSeedRatio,
        int globalMaxSeedMinutes,
        bool downloadToTorrentFolder,
        string specificDownloadFolder,
        bool proxyEnabled,
        string proxyHost,
        int proxyPort,
        string proxyUsername,
        string proxyPassword,
        string? language,
        string? theme,
        bool? wifiOnly = null,
        bool? keepDeviceAwake = null,
        bool proxyPasswordUnavailable = false)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var keepExistingPassword = proxyPasswordUnavailable && string.IsNullOrEmpty(proxyPassword);

        return new AppSettings
        {
            GlobalDownloadLimitKbps = globalDownloadLimitKbps,
            GlobalUploadLimitKbps = globalUploadLimitKbps,
            MaxActiveDownloads = maxActiveDownloads,
            MaxActiveSeeds = maxActiveSeeds,
            GlobalMaxSeedRatio = globalMaxSeedRatio,
            GlobalMaxSeedMinutes = globalMaxSeedMinutes,
            SortByStatus = existing.SortByStatus,
            WifiOnly = wifiOnly ?? existing.WifiOnly,
            DownloadToTorrentFolder = downloadToTorrentFolder,
            SpecificDownloadFolder = specificDownloadFolder?.Trim() ?? string.Empty,
            ProxyEnabled = proxyEnabled,
            ProxyHost = proxyHost ?? string.Empty,
            ProxyPort = proxyPort is > 0 and <= 65535 ? proxyPort : 1080,
            ProxyUsername = proxyUsername ?? string.Empty,
            ProxyPassword = (keepExistingPassword ? existing.ProxyPassword : proxyPassword) ?? string.Empty,
            ProxyPasswordUnavailable = keepExistingPassword && existing.ProxyPasswordUnavailable,
            Language = language,
            Theme = ThemeSettings.Normalize(theme),
            DesktopWasMaximized = existing.DesktopWasMaximized,
            KeepDeviceAwake = keepDeviceAwake ?? existing.KeepDeviceAwake
        };
    }
}
