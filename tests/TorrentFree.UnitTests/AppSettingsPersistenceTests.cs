using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AppSettingsPersistenceTests
{
    [Fact]
    public async Task MergeAndSaveAsync_SerializesSettingsSaveAndSortToggle_WithoutLostUpdates()
    {
        var storage = new DelayedStorageService(new AppSettings { SortByStatus = false });

        var settingsSave = AppSettingsPersistence.MergeAndSaveAsync(
            storage,
            existing => AppSettingsFactory.CreateForSettingsPage(
                existing,
                globalDownloadLimitKbps: 1_200,
                globalUploadLimitKbps: 300,
                maxActiveDownloads: 5,
                maxActiveSeeds: 7,
                globalMaxSeedRatio: 2.5,
                globalMaxSeedMinutes: 180,
                downloadToTorrentFolder: false,
                specificDownloadFolder: @"D:\Downloads",
                proxyEnabled: false,
                proxyHost: string.Empty,
                proxyPort: 1080,
                proxyUsername: string.Empty,
                proxyPassword: string.Empty,
                language: "fr",
                theme: ThemeSettings.Dark));

        var sortSave = AppSettingsPersistence.MergeAndSaveAsync(
            storage,
            existing => AppSettingsFactory.CreateWithSortByStatus(existing, sortByStatus: true));

        await Task.WhenAll(settingsSave, sortSave).WaitAsync(TestContext.Current.CancellationToken);

        var persisted = await storage.LoadSettingsAsync();
        Assert.Equal(1_200, persisted.GlobalDownloadLimitKbps);
        Assert.Equal(300, persisted.GlobalUploadLimitKbps);
        Assert.Equal(5, persisted.MaxActiveDownloads);
        Assert.Equal(7, persisted.MaxActiveSeeds);
        Assert.Equal(2.5, persisted.GlobalMaxSeedRatio);
        Assert.Equal(180, persisted.GlobalMaxSeedMinutes);
        Assert.True(persisted.SortByStatus);
        Assert.Equal(1, storage.MaxConcurrentOperations);
    }

    [Fact]
    public async Task RunExclusiveAsync_SerializesWindowStateUpdateWithSettingsMerge()
    {
        var storage = new DelayedStorageService(new AppSettings { DesktopWasMaximized = false });

        var settingsSave = AppSettingsPersistence.MergeAndSaveAsync(
            storage,
            existing => AppSettingsFactory.CreateWithSortByStatus(existing, sortByStatus: true));
        var windowStateSave = AppSettingsPersistence.RunExclusiveAsync(
            () => storage.UpdateDesktopWindowStateAsync(desktopWasMaximized: true));

        await Task.WhenAll(settingsSave, windowStateSave).WaitAsync(TestContext.Current.CancellationToken);

        var persisted = await storage.LoadSettingsAsync();
        Assert.True(persisted.SortByStatus);
        Assert.True(persisted.DesktopWasMaximized);
        Assert.Equal(1, storage.MaxConcurrentOperations);
    }

    private sealed class DelayedStorageService(AppSettings initialSettings) : IStorageService
    {
        private readonly object _sync = new();
        private AppSettings _settings = initialSettings;
        private int _activeOperations;
        private int _maxConcurrentOperations;

        public int MaxConcurrentOperations => Volatile.Read(ref _maxConcurrentOperations);

        public Task<List<TorrentItem>> LoadTorrentsAsync() => Task.FromResult(new List<TorrentItem>());
        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents) => Task.CompletedTask;

        public async Task<AppSettings> LoadSettingsAsync()
        {
            EnterOperation();
            try
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                lock (_sync)
                {
                    return AppSettingsFactory.CreateWithSortByStatus(_settings, _settings.SortByStatus);
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            EnterOperation();
            try
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                lock (_sync)
                {
                    _settings = settings;
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        public async Task UpdateDesktopWindowStateAsync(bool? desktopWasMaximized)
        {
            EnterOperation();
            try
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                lock (_sync)
                {
                    _settings.DesktopWasMaximized = desktopWasMaximized;
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        public string GetDefaultDownloadPath() => string.Empty;

        private void EnterOperation()
        {
            var active = Interlocked.Increment(ref _activeOperations);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxConcurrentOperations);
                if (active <= observed)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref _maxConcurrentOperations, active, observed) != observed);
        }

        private void ExitOperation() => Interlocked.Decrement(ref _activeOperations);
    }
}
