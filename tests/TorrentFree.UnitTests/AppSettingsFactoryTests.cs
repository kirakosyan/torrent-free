using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AppSettingsFactoryTests
{
    [Fact]
    public void CreateForSettingsPage_PreservesSortByStatusFromExistingSettings()
    {
        var existing = new AppSettings
        {
            SortByStatus = true
        };

        var updated = AppSettingsFactory.CreateForSettingsPage(
            existing,
            globalDownloadLimitKbps: 1200,
            globalUploadLimitKbps: 300,
            maxActiveDownloads: 5,
            maxActiveSeeds: 7,
            globalMaxSeedRatio: 2.5,
            globalMaxSeedMinutes: 180);

        Assert.True(updated.SortByStatus);
        Assert.Equal(1200, updated.GlobalDownloadLimitKbps);
        Assert.Equal(300, updated.GlobalUploadLimitKbps);
        Assert.Equal(5, updated.MaxActiveDownloads);
        Assert.Equal(7, updated.MaxActiveSeeds);
        Assert.Equal(2.5, updated.GlobalMaxSeedRatio);
        Assert.Equal(180, updated.GlobalMaxSeedMinutes);
    }

    [Fact]
    public void CreateForSettingsPage_KeepsSortByStatusFalseWhenDisabled()
    {
        var existing = new AppSettings
        {
            SortByStatus = false
        };

        var updated = AppSettingsFactory.CreateForSettingsPage(
            existing,
            globalDownloadLimitKbps: 0,
            globalUploadLimitKbps: 0,
            maxActiveDownloads: 2,
            maxActiveSeeds: 2,
            globalMaxSeedRatio: 0,
            globalMaxSeedMinutes: 0);

        Assert.False(updated.SortByStatus);
    }
}
