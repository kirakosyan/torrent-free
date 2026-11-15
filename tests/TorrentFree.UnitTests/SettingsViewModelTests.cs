using System.Collections.ObjectModel;
using System.Globalization;
using TorrentFree.Models;
using TorrentFree.Services;
using TorrentFree.ViewModels;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ProxyDraft_SurvivesReopeningAndFailedReloadWithoutBeingApplied()
    {
        var storage = new SettingsStorage();
        var transfers = new Transfers();
        var vm = Create(storage, transfers);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.ProxyEnabled = true;
        vm.ProxyHost = "proxy.example";
        vm.ProxyPort = 1081;
        vm.ProxyPassword = "draft-secret";
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.True(vm.HasProxyChanges);
        Assert.Equal("proxy.example", vm.ProxyHost);
        Assert.Equal(1081, vm.ProxyPort);
        Assert.Equal("draft-secret", vm.ProxyPassword);
        Assert.False(transfers.ProxyEnabled);
        storage.FailLoad = true;
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.True(vm.HasProxyChanges);
        Assert.Equal("draft-secret", vm.ProxyPassword);
        storage.FailLoad = false;
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.GlobalUploadLimitKbps = 50;
        await vm.FlushPendingSettingsAsync();
        Assert.False(storage.Value.ProxyEnabled);
        await vm.ApplyProxyCommand.ExecuteAsync(null);
        Assert.True(transfers.ProxyEnabled);
        Assert.Equal("draft-secret", storage.Value.ProxyPassword);
    }

    [Fact]
    public async Task FailedLoad_DisablesEditingAndRetryRestoresPersistence()
    {
        var storage = new SettingsStorage { FailLoad = true };
        var transfers = new Transfers();
        var vm = Create(storage, transfers);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsLoaded);
        Assert.True(vm.HasLoadError);
        Assert.Equal(LocalizationResourceManager.Instance["ErrorLoadSettings"], vm.ValidationMessage);
        vm.GlobalDownloadLimitKbps = 123;
        await vm.FlushPendingSettingsAsync();
        Assert.Equal(0, storage.Saves);

        storage.FailLoad = false;
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.True(vm.IsSettingsLoaded);
        Assert.False(vm.HasLoadError);
        vm.GlobalDownloadLimitKbps = 456;
        await vm.FlushPendingSettingsAsync();
        Assert.Equal(456, storage.Value.GlobalDownloadLimitKbps);
    }

    [Fact]
    public async Task ProxyDraft_IsNotAppliedOrSavedByTypingOrUnrelatedAutosaves()
    {
        var storage = new SettingsStorage();
        var transfers = new Transfers();
        var vm = Create(storage, transfers);
        await vm.InitializeCommand.ExecuteAsync(null);
        var initialCalls = transfers.ProxyCalls;
        vm.ProxyHost = " proxy.example ";
        vm.ProxyUsername = "user";
        vm.ProxyPassword = "secret";
        vm.ProxyPort = 1081;
        vm.ProxyEnabled = true;
        Assert.True(vm.HasProxyChanges);
        vm.GlobalUploadLimitKbps = 50;
        await vm.FlushPendingSettingsAsync();
        Assert.False(storage.Value.ProxyEnabled);
        Assert.Equal("", storage.Value.ProxyHost);
        Assert.Equal(initialCalls, transfers.ProxyCalls);

        await vm.ApplyProxyCommand.ExecuteAsync(null);
        Assert.Equal(initialCalls + 1, transfers.ProxyCalls);
        Assert.True(storage.Value.ProxyEnabled);
        Assert.Equal("proxy.example", storage.Value.ProxyHost);
        Assert.Equal(1081, storage.Value.ProxyPort);
        Assert.Equal("secret", storage.Value.ProxyPassword);
        Assert.False(vm.HasProxyChanges);
    }

    [Fact]
    public async Task InvalidOrUnsavedProxy_NeverReplacesRunningConfiguration()
    {
        var storage = new SettingsStorage();
        var transfers = new Transfers();
        var vm = Create(storage, transfers);
        await vm.InitializeCommand.ExecuteAsync(null);
        var initialCalls = transfers.ProxyCalls;
        vm.ProxyEnabled = true;
        await vm.ApplyProxyCommand.ExecuteAsync(null);
        Assert.Equal(LocalizationResourceManager.Instance["ValidationProxy"], vm.ValidationMessage);
        Assert.Equal(initialCalls, transfers.ProxyCalls);
        vm.ProxyHost = "proxy.example";
        storage.FailSave = true;
        await vm.ApplyProxyCommand.ExecuteAsync(null);
        Assert.Equal(LocalizationResourceManager.Instance["ErrorSaveSettings"], vm.ValidationMessage);
        Assert.True(vm.HasProxyChanges);
        Assert.Equal(initialCalls, transfers.ProxyCalls);
        storage.FailSave = false;
        vm.GlobalUploadLimitKbps = 22;
        await vm.FlushPendingSettingsAsync();
        Assert.False(storage.Value.ProxyEnabled);
    }

    private static SettingsViewModel Create(SettingsStorage storage, Transfers transfers)
        => new(storage, transfers, new Associations(), new Localization(), new Folders(), new Theme());

    private sealed class SettingsStorage : IStorageService
    {
        public AppSettings Value = new();
        public int Saves;
        public bool FailLoad, FailSave;
        public Task<AppSettings> LoadSettingsAsync() => FailLoad
            ? Task.FromException<AppSettings>(new IOException("private path and raw detail")) : Task.FromResult(Value.Clone());
        public Task SaveSettingsAsync(AppSettings settings)
        {
            if (FailSave) throw new IOException("private path and raw detail");
            Value = settings.Clone(); Saves++; return Task.CompletedTask;
        }
        public Task<List<TorrentItem>> LoadTorrentsAsync() => Task.FromResult(new List<TorrentItem>());
        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents) => Task.CompletedTask;
        public Task UpdateDesktopWindowStateAsync(bool? value) => Task.CompletedTask;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetDefaultDownloadPath() => Path.GetTempPath();
    }
    private sealed class Associations : IFileAssociationService
    {
        public bool IsSupported => false;
        public Task<bool> IsAssociatedAsync() => Task.FromResult(false);
        public Task<bool> AssociateAsync() => Task.FromResult(false);
        public Task<bool> RemoveAssociationAsync() => Task.FromResult(false);
    }
    private sealed class Folders : IFolderPickerService
    {
        public bool IsSupported => false;
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
    private sealed class Localization : ILocalizationService
    {
        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
        public void SetCulture(CultureInfo culture) { }
    }
    private sealed class Theme : IThemeService { public void Apply(string? code) { } }

    private sealed class Transfers : ITorrentService
    {
        public int ProxyCalls;
        public bool ProxyEnabled;
        public ObservableCollection<TorrentItem> Torrents { get; } = [];
        public Task InitializeAsync() => Task.CompletedTask;
        public Task UpdateWifiOnlyAsync(bool enabled) => Task.CompletedTask;
        public void UpdateKeepDeviceAwake(bool enabled) { }
        public void UpdateGlobalSpeedLimits(int down, int up) { }
        public void UpdateQueueLimits(int downloads, int seeds) { }
        public void UpdateSeedingLimits(double ratio, int minutes) { }
        public void UpdateProxySettings(bool enabled, string host, int port, string username, string password) { ProxyCalls++; ProxyEnabled = enabled; }
        public Task<TorrentItem?> AddTorrentAsync(string link, bool startPaused = false) => throw new NotSupportedException();
        public Task<TorrentItem?> AddTorrentFileAsync(TorrentMetadata metadata, bool startPaused = false) => throw new NotSupportedException();
        public Task<IReadOnlyList<TorrentFileChoice>> GetTorrentFilesAsync(TorrentItem torrent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DownloadExportPlan> CreateDownloadExportPlanAsync(TorrentItem torrent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetTorrentFileSelectionAsync(TorrentItem torrent, IReadOnlyCollection<string> paths) => throw new NotSupportedException();
        public Task StartTorrentAsync(TorrentItem torrent) => throw new NotSupportedException();
        public Task PauseTorrentAsync(TorrentItem torrent) => throw new NotSupportedException();
        public Task StopTorrentAsync(TorrentItem torrent) => throw new NotSupportedException();
        public Task PauseAllForBackgroundTimeoutAsync() => throw new NotSupportedException();
        public void ResumeAfterBackgroundTimeout() { }
        public Task StartQueuedTorrentsAsync() => throw new NotSupportedException();
        public Task<TorrentRemovalResult> RemoveTorrentAsync(TorrentItem torrent, bool deleteTorrentFile = false, bool deleteFiles = false) => throw new NotSupportedException();
        public bool IsValidMagnetLink(string link) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
