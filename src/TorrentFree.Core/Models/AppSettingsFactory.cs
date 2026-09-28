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

        var updated = existing.Clone();
        updated.SortByStatus = sortByStatus;
        return updated;
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

        var updated = existing.Clone();
        updated.GlobalDownloadLimitKbps = globalDownloadLimitKbps;
        updated.GlobalUploadLimitKbps = globalUploadLimitKbps;
        updated.MaxActiveDownloads = maxActiveDownloads;
        updated.MaxActiveSeeds = maxActiveSeeds;
        updated.GlobalMaxSeedRatio = globalMaxSeedRatio;
        updated.GlobalMaxSeedMinutes = globalMaxSeedMinutes;
        updated.WifiOnly = wifiOnly ?? existing.WifiOnly;
        updated.DownloadToTorrentFolder = downloadToTorrentFolder;
        updated.SpecificDownloadFolder = specificDownloadFolder?.Trim() ?? string.Empty;
        updated.ProxyEnabled = proxyEnabled;
        updated.ProxyHost = proxyHost ?? string.Empty;
        updated.ProxyPort = proxyPort is > 0 and <= 65535 ? proxyPort : 1080;
        updated.ProxyUsername = proxyUsername ?? string.Empty;
        updated.ProxyPassword = (keepExistingPassword ? existing.ProxyPassword : proxyPassword) ?? string.Empty;
        updated.ProxyPasswordUnavailable = keepExistingPassword && existing.ProxyPasswordUnavailable;
        updated.Language = language;
        updated.Theme = ThemeSettings.Normalize(theme);
        updated.KeepDeviceAwake = keepDeviceAwake ?? existing.KeepDeviceAwake;
        return updated;
    }
}
