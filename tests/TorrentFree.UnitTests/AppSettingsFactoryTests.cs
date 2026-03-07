using System.Text.Json;
using System.Text.Json.Serialization;
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
            globalMaxSeedMinutes: 180,
            proxyEnabled: true,
            proxyHost: "127.0.0.1",
            proxyPort: 9050,
            proxyUsername: "user",
            proxyPassword: "pass",
            language: "fr");

        Assert.True(updated.SortByStatus);
        Assert.Equal(1200, updated.GlobalDownloadLimitKbps);
        Assert.Equal(300, updated.GlobalUploadLimitKbps);
        Assert.Equal(5, updated.MaxActiveDownloads);
        Assert.Equal(7, updated.MaxActiveSeeds);
        Assert.Equal(2.5, updated.GlobalMaxSeedRatio);
        Assert.Equal(180, updated.GlobalMaxSeedMinutes);
        Assert.True(updated.ProxyEnabled);
        Assert.Equal("127.0.0.1", updated.ProxyHost);
        Assert.Equal(9050, updated.ProxyPort);
        Assert.Equal("user", updated.ProxyUsername);
        Assert.Equal("pass", updated.ProxyPassword);
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
            globalMaxSeedMinutes: 0,
            proxyEnabled: false,
            proxyHost: "",
            proxyPort: 1080,
            proxyUsername: "",
            proxyPassword: "",
            language: null);

        Assert.False(updated.SortByStatus);
        Assert.False(updated.ProxyEnabled);
    }

    [Fact]
    public void CreateForSettingsPage_NullCoalescesProxyStrings()
    {
        var existing = new AppSettings { SortByStatus = false };

        var updated = AppSettingsFactory.CreateForSettingsPage(
            existing,
            globalDownloadLimitKbps: 0,
            globalUploadLimitKbps: 0,
            maxActiveDownloads: 2,
            maxActiveSeeds: 2,
            globalMaxSeedRatio: 0,
            globalMaxSeedMinutes: 0,
            proxyEnabled: false,
            proxyHost: null!,
            proxyPort: 0,
            proxyUsername: null!,
            proxyPassword: null!,
            language: "es");

        Assert.Equal(string.Empty, updated.ProxyHost);
        Assert.Equal(1080, updated.ProxyPort);
        Assert.Equal(string.Empty, updated.ProxyUsername);
        Assert.Equal(string.Empty, updated.ProxyPassword);
    }

    [Fact]
    public void OldSettingsJson_DeserializesWithSafeProxyDefaults()
    {
        // Simulate a settings JSON saved by a previous app version that has
        // no proxy fields at all.
        const string oldJson = """
        {
            "globalDownloadLimitKbps": 500,
            "globalUploadLimitKbps": 100,
            "maxActiveDownloads": 3,
            "maxActiveSeeds": 3,
            "globalMaxSeedRatio": 1.5,
            "globalMaxSeedMinutes": 60,
            "sortByStatus": true
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        var settings = JsonSerializer.Deserialize<AppSettings>(oldJson, options);

        Assert.NotNull(settings);
        // Original fields preserved
        Assert.Equal(500, settings.GlobalDownloadLimitKbps);
        Assert.True(settings.SortByStatus);
        // Proxy fields fall back to safe defaults
        Assert.False(settings.ProxyEnabled);
        Assert.Equal(string.Empty, settings.ProxyHost);
        Assert.Equal(1080, settings.ProxyPort);
        Assert.Equal(string.Empty, settings.ProxyUsername);
        Assert.Equal(string.Empty, settings.ProxyPassword);
        // Language falls back to null (system default) when missing from old JSON
        Assert.Null(settings.Language);
    }
}
