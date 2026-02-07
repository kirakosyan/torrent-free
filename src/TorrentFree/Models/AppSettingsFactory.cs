namespace TorrentFree.Models;

/// <summary>
/// Creates persisted app settings from view-model state while preserving unrelated fields.
/// </summary>
public static class AppSettingsFactory
{
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
        int globalMaxSeedMinutes)
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
            SortByStatus = existing.SortByStatus
        };
    }
}
