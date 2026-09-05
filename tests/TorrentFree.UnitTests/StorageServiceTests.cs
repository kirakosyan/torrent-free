using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StorageServiceTests
{
    [Fact]
    public async Task FailedRead_ThrowsAndBlocksReplacementUntilSuccessfulReload()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "original" }]);
        var path = Path.Combine(directory.Path, "torrents.json");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => storage.LoadTorrentsAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveTorrentsAsync([new TorrentItem { Id = "replacement" }]));
        var restored = await storage.LoadTorrentsAsync();
        Assert.Equal("original", Assert.Single(restored).Id);
        restored.Add(new TorrentItem { Id = "new" });
        await storage.SaveTorrentsAsync(restored);
        Assert.Equal(2, (await storage.LoadTorrentsAsync()).Count);
    }

    [Fact]
    public async Task FailedWrite_IsObservableAndLeavesPreviousStateIntact()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "original" }]);
        var path = Path.Combine(directory.Path, "torrents.json");
        // Read sharing allows the read/merge step but prevents replacing the destination.
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => storage.SaveTorrentsAsync([new TorrentItem { Id = "new" }]));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal("original", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        Assert.False(File.Exists(path + ".tmp"));
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "new" }]);
        Assert.Equal("new", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        Assert.Contains("original", await File.ReadAllTextAsync(path + ".bak", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task CorruptState_IsNeverTreatedAsAnEmptyInstallation(string contents)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
        using var storage = new StorageService(directory.StoragePaths);
        await Assert.ThrowsAnyAsync<JsonException>(() => storage.LoadTorrentsAsync());
        await Assert.ThrowsAnyAsync<JsonException>(() => storage.LoadSettingsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveTorrentsAsync([]));
        await Assert.ThrowsAnyAsync<JsonException>(() => storage.SaveSettingsAsync(new AppSettings()));
        Assert.Equal(contents, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettingsAndTorrentUpdates_PreserveEachOthersProductionFields()
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        var torrent = new TorrentItem { Id = "kept", SeededSeconds = 40, CachedTorrentFilePath = "metadata.torrent" };
        await storage.SaveTorrentsAsync([torrent]);
        await storage.SaveSettingsAsync(new AppSettings { SortByStatus = true, ProxyHost = "proxy.example", GlobalMaxSeedMinutes = 10 });
        await storage.UpdateDesktopWindowStateAsync(true);
        torrent.SeededSeconds = 60;
        await storage.SaveTorrentsAsync([torrent]);
        var settings = await storage.LoadSettingsAsync();
        Assert.True(settings.SortByStatus);
        Assert.True(settings.DesktopWasMaximized);
        Assert.Equal("proxy.example", settings.ProxyHost);
        var restored = Assert.Single(await storage.LoadTorrentsAsync());
        Assert.Equal(60, restored.SeededSeconds);
        Assert.Equal("metadata.torrent", restored.CachedTorrentFilePath);
    }

    [Fact]
    public async Task LegacyJson_LoadsActualModelAndDefaultsNewFields()
    {
        using var directory = new CoreTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "torrents.json"),
            """{"torrents":[{"id":"legacy","name":"Ubuntu","showInFolderCommand":null,"removedField":true}],"settings":{"sortByStatus":true}}""", TestContext.Current.CancellationToken);
        using var storage = new StorageService(directory.StoragePaths);
        var torrent = Assert.Single(await storage.LoadTorrentsAsync());
        Assert.Equal("legacy", torrent.Id);
        Assert.Null(torrent.CachedTorrentFilePath);
        Assert.Equal(0, torrent.SeededSeconds);
        Assert.True((await storage.LoadSettingsAsync()).SortByStatus);
        await storage.SaveTorrentsAsync([torrent]);
    }
}
