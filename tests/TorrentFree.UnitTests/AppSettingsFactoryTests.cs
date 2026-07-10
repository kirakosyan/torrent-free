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
            SortByStatus = true,
            DesktopWasMaximized = true
        };

        var updated = AppSettingsFactory.CreateForSettingsPage(
            existing,
            globalDownloadLimitKbps: 1200,
            globalUploadLimitKbps: 300,
            maxActiveDownloads: 5,
            maxActiveSeeds: 7,
            globalMaxSeedRatio: 2.5,
            globalMaxSeedMinutes: 180,
            downloadToTorrentFolder: false,
            specificDownloadFolder: @"D:\Downloads\Torrents",
            proxyEnabled: true,
            proxyHost: "127.0.0.1",
            proxyPort: 9050,
            proxyUsername: "user",
            proxyPassword: "pass",
            language: "fr",
            theme: "dark");

        Assert.True(updated.SortByStatus);
        Assert.Equal("dark", updated.Theme);
        Assert.Equal(1200, updated.GlobalDownloadLimitKbps);
        Assert.Equal(300, updated.GlobalUploadLimitKbps);
        Assert.Equal(5, updated.MaxActiveDownloads);
        Assert.Equal(7, updated.MaxActiveSeeds);
        Assert.Equal(2.5, updated.GlobalMaxSeedRatio);
        Assert.Equal(180, updated.GlobalMaxSeedMinutes);
        Assert.False(updated.DownloadToTorrentFolder);
        Assert.Equal(@"D:\Downloads\Torrents", updated.SpecificDownloadFolder);
        Assert.True(updated.ProxyEnabled);
        Assert.Equal("127.0.0.1", updated.ProxyHost);
        Assert.Equal(9050, updated.ProxyPort);
        Assert.Equal("user", updated.ProxyUsername);
        Assert.Equal("pass", updated.ProxyPassword);
        Assert.True(updated.DesktopWasMaximized);
    }

    [Fact]
    public void CreateForSettingsPage_KeepsSortByStatusFalseWhenDisabled()
    {
        var existing = new AppSettings
        {
            SortByStatus = false,
            DownloadToTorrentFolder = true,
            SpecificDownloadFolder = @"C:\Existing",
            DesktopWasMaximized = false
        };

        var updated = AppSettingsFactory.CreateForSettingsPage(
            existing,
            globalDownloadLimitKbps: 0,
            globalUploadLimitKbps: 0,
            maxActiveDownloads: 2,
            maxActiveSeeds: 2,
            globalMaxSeedRatio: 0,
            globalMaxSeedMinutes: 0,
            downloadToTorrentFolder: true,
            specificDownloadFolder: string.Empty,
            proxyEnabled: false,
            proxyHost: "",
            proxyPort: 1080,
            proxyUsername: "",
            proxyPassword: "",
            language: null,
            theme: null);

        Assert.False(updated.SortByStatus);
        Assert.True(updated.DownloadToTorrentFolder);
        Assert.Equal(string.Empty, updated.SpecificDownloadFolder);
        Assert.False(updated.ProxyEnabled);
        Assert.False(updated.DesktopWasMaximized);
        // Null/unknown theme normalizes to "follow system".
        Assert.Equal(ThemeSettings.System, updated.Theme);
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
            downloadToTorrentFolder: false,
            specificDownloadFolder: null!,
            proxyEnabled: false,
            proxyHost: null!,
            proxyPort: 0,
            proxyUsername: null!,
            proxyPassword: null!,
            language: "es",
            theme: "LIGHT");

        Assert.False(updated.DownloadToTorrentFolder);
        Assert.Equal(string.Empty, updated.SpecificDownloadFolder);
        Assert.Equal(string.Empty, updated.ProxyHost);
        Assert.Equal(1080, updated.ProxyPort);
        Assert.Equal(string.Empty, updated.ProxyUsername);
        Assert.Equal(string.Empty, updated.ProxyPassword);
        // Mixed-case input is normalized to a canonical theme code.
        Assert.Equal(ThemeSettings.Light, updated.Theme);
    }

    [Fact]
    public void CreateWithSortByStatus_AfterSettingsPageSave_PreservesAllNewerSettings()
    {
        // This is the regression sequence: SettingsPage has saved these newer values while
        // MainViewModel still holds its older startup snapshot, then the user toggles sorting.
        var existing = new AppSettings
        {
            GlobalDownloadLimitKbps = 800,
            GlobalUploadLimitKbps = 120,
            MaxActiveDownloads = 4,
            MaxActiveSeeds = 6,
            GlobalMaxSeedRatio = 1.75,
            GlobalMaxSeedMinutes = 240,
            DownloadToTorrentFolder = false,
            SpecificDownloadFolder = @"C:\Saved",
            ProxyEnabled = true,
            ProxyHost = "127.0.0.1",
            ProxyPort = 9050,
            ProxyUsername = "user",
            ProxyPassword = "pass",
            Language = "fr",
            Theme = "dark",
            DesktopWasMaximized = true
        };

        var updated = AppSettingsFactory.CreateWithSortByStatus(existing, sortByStatus: true);

        Assert.Equal(800, updated.GlobalDownloadLimitKbps);
        Assert.Equal(120, updated.GlobalUploadLimitKbps);
        Assert.Equal(4, updated.MaxActiveDownloads);
        Assert.Equal(6, updated.MaxActiveSeeds);
        Assert.Equal(1.75, updated.GlobalMaxSeedRatio);
        Assert.Equal(240, updated.GlobalMaxSeedMinutes);
        Assert.True(updated.SortByStatus);
        Assert.False(updated.DownloadToTorrentFolder);
        Assert.Equal(@"C:\Saved", updated.SpecificDownloadFolder);

        Assert.True(updated.ProxyEnabled);
        Assert.Equal("127.0.0.1", updated.ProxyHost);
        Assert.Equal(9050, updated.ProxyPort);
        Assert.Equal("user", updated.ProxyUsername);
        Assert.Equal("pass", updated.ProxyPassword);
        Assert.Equal("fr", updated.Language);
        Assert.Equal("dark", updated.Theme);
        Assert.True(updated.DesktopWasMaximized);
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
        Assert.True(settings.DownloadToTorrentFolder);
        Assert.Equal(string.Empty, settings.SpecificDownloadFolder);
        // Proxy fields fall back to safe defaults
        Assert.False(settings.ProxyEnabled);
        Assert.Equal(string.Empty, settings.ProxyHost);
        Assert.Equal(1080, settings.ProxyPort);
        Assert.Equal(string.Empty, settings.ProxyUsername);
        Assert.Equal(string.Empty, settings.ProxyPassword);
        // Language falls back to null (system default) when missing from old JSON
        Assert.Null(settings.Language);
        // Theme falls back to "follow system" when missing from old JSON
        Assert.Equal(ThemeSettings.System, settings.Theme);
        // Desktop window state falls back to null when missing from old JSON
        Assert.Null(settings.DesktopWasMaximized);
    }
}
