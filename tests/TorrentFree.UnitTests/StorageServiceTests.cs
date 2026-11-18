using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StorageServiceTests
{
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("")]
    public async Task TornFirstSave_ArchivesEvidenceAndAllowsNewState(string contents)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path + ".tmp", contents, TestContext.Current.CancellationToken);
        using var storage = new StorageService(directory.StoragePaths);
        Assert.Empty(await storage.LoadTorrentsAsync());
        Assert.Equal(contents, File.ReadAllText(Assert.Single(Directory.GetFiles(directory.Path, "*.corrupt-*"))));
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "new" }]);
        Assert.Equal("new", Assert.Single(await storage.LoadTorrentsAsync()).Id);
    }

    [Fact]
    public async Task CorruptRecoveryCopies_ReportNewestFailureAndPreserveBoth()
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        File.WriteAllText(path + ".tmp", "null");
        File.WriteAllText(path + ".bak", "{broken");
        using var storage = new StorageService(directory.StoragePaths);
        var error = await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.LoadTorrentsAsync());
        Assert.Contains("state is null", error.InnerException!.Message);
        Assert.Equal("null", File.ReadAllText(path + ".tmp"));
        Assert.Equal("{broken", File.ReadAllText(path + ".bak"));
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("{broken", "null")]
    public async Task MissingPrimaryWithCorruptRecoveryFiles_PreservesEvidenceAndBlocksSaves(string? temporary, string? backup)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        if (temporary is not null) await File.WriteAllTextAsync(path + ".tmp", temporary, TestContext.Current.CancellationToken);
        if (backup is not null) await File.WriteAllTextAsync(path + ".bak", backup, TestContext.Current.CancellationToken);
        using var storage = new StorageService(directory.StoragePaths);
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.LoadTorrentsAsync());
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.LoadSettingsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveTorrentsAsync([]));
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.SaveSettingsAsync(new AppSettings()));
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.UpdateDesktopWindowStateAsync(true));
        Assert.False(File.Exists(path));
        if (temporary is not null) Assert.Equal(temporary, await File.ReadAllTextAsync(path + ".tmp", TestContext.Current.CancellationToken));
        if (backup is not null) Assert.Equal(backup, await File.ReadAllTextAsync(path + ".bak", TestContext.Current.CancellationToken));

        // Restoring a valid backup makes the same service retryable without restarting.
        await File.WriteAllTextAsync(path + ".bak", """{"torrents":[{"id":"restored"}]}""", TestContext.Current.CancellationToken);
        Assert.Equal("restored", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "restored" }]);
    }

    [Theory]
    [InlineData(".tmp", ".bak")]
    [InlineData(".bak", ".tmp")]
    public async Task MissingPrimary_UsesValidRecoveryCopyEvenWhenOtherCopyIsCorrupt(string validSuffix, string corruptSuffix)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path + validSuffix, """{"torrents":[{"id":"kept"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path + corruptSuffix, "{broken", TestContext.Current.CancellationToken);
        using var storage = new StorageService(directory.StoragePaths);
        Assert.Equal("kept", Assert.Single(await storage.LoadTorrentsAsync()).Id);
        Assert.Equal("{broken", await File.ReadAllTextAsync(path + corruptSuffix, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnrelatedSettingsEdits_DoNotRewriteStoredPassword()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new CountingSecretStore();
        using var storage = new StorageService(directory.StoragePaths, secrets);
        var settings = new AppSettings { ProxyPassword = "saved-password" };
        await storage.SaveSettingsAsync(settings);
        for (var i = 1; i <= 10; i++)
        {
            settings = await storage.LoadSettingsAsync();
            settings.GlobalDownloadLimitKbps = i;
            await storage.SaveSettingsAsync(settings);
        }
        Assert.Equal(1, secrets.Writes);

        settings.ProxyPassword = "changed-password";
        await storage.SaveSettingsAsync(settings);
        Assert.Equal(2, secrets.Writes);
        settings.ProxyPassword = string.Empty;
        await storage.SaveSettingsAsync(settings);
        await storage.SaveSettingsAsync(settings);
        Assert.Equal(4, secrets.Writes);
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task FailedSecretWrite_IsRetriedEvenWhenPasswordDoesNotChange()
    {
        using var directory = new CoreTestDirectory();
        var secrets = new CountingSecretStore { FailNextWrite = true };
        using var storage = new StorageService(directory.StoragePaths, secrets);
        var settings = new AppSettings { ProxyPassword = "retry-password" };
        await storage.SaveSettingsAsync(settings);
        await storage.SaveSettingsAsync(settings);
        Assert.Equal(2, secrets.Writes);
        Assert.Equal(settings.ProxyPassword, Assert.Single(secrets.Values).Value);
        Assert.DoesNotContain(settings.ProxyPassword,
            await File.ReadAllTextAsync(Path.Combine(directory.Path, "torrents.json"), TestContext.Current.CancellationToken));
    }

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
        // Unix allows replacing an open file, so this sharing-violation case is Windows-specific.
        if (!OperatingSystem.IsWindows()) return;

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
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.LoadTorrentsAsync());
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.LoadSettingsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveTorrentsAsync([]));
        await Assert.ThrowsAsync<SavedStateRecoveryException>(() => storage.SaveSettingsAsync(new AppSettings()));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedProxyChange_AndBackupRecovery_KeepThePasswordWithItsEndpoint(bool failWrite)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        var secrets = new CountingSecretStore();
        var first = new AppSettings { ProxyEnabled = true, ProxyHost = "a.example", ProxyUsername = "one", ProxyPassword = "first" };
        var second = new AppSettings { ProxyEnabled = true, ProxyHost = "b.example", ProxyUsername = "two", ProxyPassword = "second" };
        using (var writer = new StorageService(directory.StoragePaths, secrets))
        {
            await writer.SaveSettingsAsync(first);
            if (failWrite)
            {
                // Deterministic across filesystems: the temporary file cannot replace a directory.
                Directory.CreateDirectory(path + ".tmp");
                await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => writer.SaveSettingsAsync(second));
                Directory.Delete(path + ".tmp");
                Assert.Equal("first", Assert.Single(secrets.Values).Value);
            }
            else
            {
                await writer.SaveSettingsAsync(second);
                Assert.Equal("second", (await writer.LoadSettingsAsync()).ProxyPassword);
                await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
            }
        }

        using var reopened = new StorageService(directory.StoragePaths, secrets);
        var restored = await reopened.LoadSettingsAsync();
        Assert.Equal("a.example", restored.ProxyHost);
        Assert.Equal("one", restored.ProxyUsername);
        Assert.Equal("first", restored.ProxyPassword);
    }

    [Theory]
    [InlineData(".bak", true)]
    [InlineData(".tmp", false)]
    public async Task LegacyRecovery_RequestsReentryOnlyForUnboundBackups(string suffix, bool reentry)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path + suffix, """{"settings":{"proxyEnabled":true,"proxyHost":"previous.example","proxyUsername":"old"}}""",
            TestContext.Current.CancellationToken);
        var secrets = new CountingSecretStore();
        await secrets.SetAsync(StorageService.ProxyPasswordSecretKey, "different-provider-password");
        using (var storage = new StorageService(directory.StoragePaths, secrets))
        {
            var restored = await storage.LoadSettingsAsync();
            Assert.Equal("previous.example", restored.ProxyHost);
            Assert.Equal(reentry, restored.ProxyPasswordUnavailable);
            Assert.Equal(reentry ? string.Empty : "different-provider-password", restored.ProxyPassword);
        }
        using var reopened = new StorageService(directory.StoragePaths, secrets);
        Assert.Equal(reentry, (await reopened.LoadSettingsAsync()).ProxyPasswordUnavailable);
    }

    [Fact]
    public async Task LegacySecurePassword_MigratesBeforeSwitchingEndpoints()
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "torrents.json");
        await File.WriteAllTextAsync(path, """{"settings":{"proxyHost":"legacy.example","proxyUsername":"old"}}""",
            TestContext.Current.CancellationToken);
        var secrets = new CountingSecretStore();
        await secrets.SetAsync(StorageService.ProxyPasswordSecretKey, "legacy");
        using var storage = new StorageService(directory.StoragePaths, secrets);
        Assert.Equal("legacy", (await storage.LoadSettingsAsync()).ProxyPassword);
        Assert.False(secrets.Values.ContainsKey(StorageService.ProxyPasswordSecretKey));
        await storage.SaveSettingsAsync(new AppSettings { ProxyHost = "new.example", ProxyPassword = "new" });
        File.Delete(path);
        using var reopened = new StorageService(directory.StoragePaths, secrets);
        var recovered = await reopened.LoadSettingsAsync();
        Assert.Equal("legacy.example", recovered.ProxyHost);
        Assert.Equal("legacy", recovered.ProxyPassword);
    }

    private sealed class CountingSecretStore : ISecretStore
    {
        public int Writes { get; private set; }
        public Dictionary<string, string> Values { get; } = [];
        public bool FailNextWrite { get; set; }
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string? value)
        {
            Writes++;
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("Injected secret write failure");
            }
            if (value is null) Values.Remove(key);
            else Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
