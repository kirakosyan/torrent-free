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
            DownloadToTorrentFolder = existing.DownloadToTorrentFolder,
            SpecificDownloadFolder = existing.SpecificDownloadFolder ?? string.Empty,
            ProxyEnabled = existing.ProxyEnabled,
            ProxyHost = existing.ProxyHost ?? string.Empty,
            ProxyPort = existing.ProxyPort is > 0 and <= 65535 ? existing.ProxyPort : 1080,
            ProxyUsername = existing.ProxyUsername ?? string.Empty,
            ProxyPassword = existing.ProxyPassword ?? string.Empty,
            Language = existing.Language,
            Theme = ThemeSettings.Normalize(existing.Theme),
            DesktopWasMaximized = existing.DesktopWasMaximized
        };
    }

    /// <summary>
    /// Builds the settings payload for the settings page save flow.
    /// </summary>
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
        string? theme)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return new AppSettings
        {
            GlobalDownloadLimitKbps = globalDownloadLimitKbps,
            GlobalUploadLimitKbps = globalUploadLimitKbps,
            MaxActiveDownloads = maxActiveDownloads,
            MaxActiveSeeds = maxActiveSeeds,
            GlobalMaxSeedRatio = globalMaxSeedRatio,
            GlobalMaxSeedMinutes = globalMaxSeedMinutes,
            SortByStatus = existing.SortByStatus,
            DownloadToTorrentFolder = downloadToTorrentFolder,
            SpecificDownloadFolder = specificDownloadFolder?.Trim() ?? string.Empty,
            ProxyEnabled = proxyEnabled,
            ProxyHost = proxyHost ?? string.Empty,
            ProxyPort = proxyPort is > 0 and <= 65535 ? proxyPort : 1080,
            ProxyUsername = proxyUsername ?? string.Empty,
            ProxyPassword = proxyPassword ?? string.Empty,
            Language = language,
            Theme = ThemeSettings.Normalize(theme),
            DesktopWasMaximized = existing.DesktopWasMaximized
        };
    }
}
