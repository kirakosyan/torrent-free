using System.Reflection;
using System.Text.Json;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class WifiTransferTests
{
    [Fact]
    public async Task ConnectivityEventsAlone_PauseAndResumeWithoutSettingsRefresh()
    {
        await using var fixture = new Fixture();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        fixture.Network.IsWifiConnected = false;
        fixture.Network.Notify();
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.WaitingForWifi);
        fixture.Network.IsWifiConnected = true;
        fixture.Network.Notify();
        await CoreServiceFixture.WaitUntilAsync(() => fixture.Service.Starts == 2);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
    }

    [Fact]
    public async Task RapidNetworkChanges_LeaveOneSessionOnTheLatestNetworkState()
    {
        await using var fixture = new Fixture();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        fixture.Network.IsWifiConnected = false;
        fixture.Network.Notify();
        fixture.Network.IsWifiConnected = true;
        fixture.Network.Notify();
        fixture.Network.IsWifiConnected = false;
        fixture.Network.Notify();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Null(fixture.Service.CurrentEngine);
        await fixture.SetWifiAsync(true);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Single(fixture.Service.CurrentEngine!.Torrents);
    }

    [Fact]
    public async Task EnabledOffline_BlocksNewTransfersWithoutCreatingEngine()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Equal(0, fixture.Service.Starts);
        Assert.Null(fixture.Service.CurrentEngine);
        Assert.True(torrent.CanPause);
        Assert.True(torrent.CanStop);
        Assert.Null(torrent.ErrorMessage);
    }

    [Fact]
    public async Task WifiLoss_ClosesEngineAndReconnectResumesOnlyWaitingTransfers()
    {
        await using var fixture = new Fixture();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var active = await fixture.AddAsync();
        var paused = await fixture.AddAsync();
        var stopped = await fixture.AddAsync();
        paused.Status = DownloadStatus.Paused;
        stopped.Status = DownloadStatus.Stopped;
        await fixture.Service.StartTorrentAsync(active);
        await fixture.SetWifiAsync(false);
        Assert.Equal(DownloadStatus.WaitingForWifi, active.Status);
        Assert.Equal(0, active.DownloadSpeed);
        Assert.Equal(0, active.UploadSpeed);
        Assert.Null(fixture.Service.CurrentEngine);
        await fixture.SetWifiAsync(true);
        Assert.Equal(DownloadStatus.Downloading, active.Status);
        Assert.Equal(DownloadStatus.Paused, paused.Status);
        Assert.Equal(DownloadStatus.Stopped, stopped.Status);
        Assert.Equal(2, fixture.Service.Starts);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("stop")]
    [InlineData("remove")]
    public async Task ManualActionWhileWaiting_CancelsAutomaticResume(string action)
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        if (action == "pause") await fixture.Service.PauseTorrentAsync(torrent);
        else if (action == "stop") await fixture.Service.StopTorrentAsync(torrent);
        else await fixture.Service.RemoveTorrentAsync(torrent);
        await fixture.SetWifiAsync(true);
        Assert.Equal(0, fixture.Service.Starts);
        if (action == "remove") Assert.Empty(fixture.Service.Torrents);
        else Assert.Equal(action == "pause" ? DownloadStatus.Paused : DownloadStatus.Stopped, torrent.Status);
    }

    [Fact]
    public async Task Reconnect_RespectsQueueCapacityAndDrainsWhenSlotIsFreed()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        fixture.Service.UpdateQueueLimits(1, 1);
        var first = await fixture.AddAsync();
        var second = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(first);
        await fixture.Service.StartTorrentAsync(second);
        await fixture.SetWifiAsync(true);
        Assert.Equal(DownloadStatus.Downloading, first.Status);
        Assert.Equal(DownloadStatus.Queued, second.Status);
        Assert.Equal(1, fixture.Service.Starts);
        await fixture.Service.PauseTorrentAsync(first);
        Assert.Equal(DownloadStatus.Downloading, second.Status);
        Assert.Equal(2, fixture.Service.Starts);
    }

    [Fact]
    public async Task DisablingWifiOnly_ResumesWaitingTransfersOnOtherNetworks()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        await fixture.Service.UpdateWifiOnlyAsync(false);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Equal(1, fixture.Service.Starts);
        fixture.Network.Notify();
        await fixture.Service.UpdateWifiOnlyAsync(false);
        Assert.Equal(1, fixture.Service.Starts);
    }

    [Fact]
    public async Task RestoredWaitingTransfer_ResumesOnWifiButManualPauseSurvivesRestart()
    {
        await using var fixture = new Fixture();
        await fixture.Storage.SaveSettingsAsync(new AppSettings { WifiOnly = true });
        var waiting = fixture.CreateItem(DownloadStatus.WaitingForWifi);
        var paused = fixture.CreateItem(DownloadStatus.Paused);
        await fixture.Storage.LoadTorrentsAsync();
        await fixture.Storage.SaveTorrentsAsync([waiting, paused]);
        await fixture.Service.InitializeAsync();
        Assert.Equal(DownloadStatus.Downloading, fixture.Service.Torrents.Single(t => t.Id == waiting.Id).Status);
        Assert.Equal(DownloadStatus.Paused, fixture.Service.Torrents.Single(t => t.Id == paused.Id).Status);
        Assert.Equal(1, fixture.Service.Starts);
    }

    [Fact]
    public async Task RestoredWifiSetting_BlocksQueueBeforeAnyStart()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Storage.SaveSettingsAsync(new AppSettings { WifiOnly = true });
        await fixture.Storage.LoadTorrentsAsync();
        await fixture.Storage.SaveTorrentsAsync([fixture.CreateItem(DownloadStatus.WaitingForWifi), fixture.CreateItem(DownloadStatus.Queued)]);
        await fixture.Service.InitializeAsync();
        Assert.All(fixture.Service.Torrents, t => Assert.Equal(DownloadStatus.WaitingForWifi, t.Status));
        Assert.Null(fixture.Service.CurrentEngine);
    }

    [Fact]
    public async Task ManualStopDuringReconnect_WinsOverStaleResumeSnapshot()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        object?[] args = [null];
        var barrier = (TaskCompletionSource)typeof(TorrentService).GetMethod("BeginEngineRebuild", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Service, args)!;
        try
        {
            fixture.Network.IsWifiConnected = true;
            fixture.Network.Notify();
            await fixture.Service.StopTorrentAsync(torrent);
        }
        finally
        {
            typeof(TorrentService).GetMethod("EndEngineRebuild", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Service, [barrier]);
        }
        await fixture.Service.UpdateWifiOnlyAsync(true);
        Assert.Equal(DownloadStatus.Stopped, torrent.Status);
        Assert.Equal(0, fixture.Service.Starts);
    }

    [Fact]
    public async Task ProxyChangeWhileOffline_CannotRestartWaitingTransfers()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        fixture.Service.UpdateProxySettings(true, "127.0.0.1", 1080, "", "");
        await CoreServiceFixture.InvokeAsync(fixture.Service, "WaitForEngineRebuildAsync");
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Equal(0, fixture.Service.Starts);
        Assert.Null(fixture.Service.CurrentEngine);
    }

    [Fact]
    public async Task ReconnectDuringBackgroundTimeout_WaitsUntilForeground()
    {
        await using var fixture = new Fixture(wifi: false);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = await fixture.AddAsync();
        await fixture.Service.StartTorrentAsync(torrent);
        await fixture.Service.PauseAllForBackgroundTimeoutAsync();
        await fixture.SetWifiAsync(true);
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Equal(0, fixture.Service.Starts);
        fixture.Service.ResumeAfterBackgroundTimeout();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
    }

    [Fact]
    public async Task SeedingWifiLossAndResume_PreservesCompletionAndActiveTime()
    {
        var network = new TestNetwork();
        await using var fixture = new CoreServiceFixture(networkMonitor: network);
        await fixture.Service.UpdateWifiOnlyAsync(true);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateSeedingStarted is not null);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        network.IsWifiConnected = false;
        network.Notify();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        Assert.Equal(DownloadStatus.WaitingForWifi, torrent.Status);
        Assert.Equal(100, torrent.Progress);
        Assert.NotNull(torrent.DateCompleted);
        Assert.Null(torrent.DateSeedingStarted);
        Assert.Equal(20, torrent.SeededSeconds);
        Assert.True(torrent.CanOpenDownloadedFile);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        // Install a local-only engine after teardown, as the standard Core fixture does.
        var engine = Fixture.CreateLocalEngine(fixture.Directory.Path);
        typeof(TorrentService).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(fixture.Service, engine);
        network.IsWifiConnected = true;
        network.Notify();
        await fixture.Service.UpdateWifiOnlyAsync(true);
        await CoreServiceFixture.WaitUntilAsync(() => torrent.DateSeedingStarted is not null);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
        Assert.Equal(20, torrent.SeededSeconds);
    }

    [Fact]
    public void WifiSettingAndWaitingStatus_RoundTripWithoutChangingExistingEnumValues()
    {
        Assert.False(new AppSettings().WifiOnly);
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WifiOnly\":true}")!;
        Assert.True(AppSettingsFactory.CreateWithSortByStatus(settings, true).WifiOnly);
        var torrent = new TorrentItem { Status = DownloadStatus.WaitingForWifi };
        Assert.Equal(DownloadStatus.WaitingForWifi, JsonSerializer.Deserialize<TorrentItem>(JsonSerializer.Serialize(torrent))!.Status);
        Assert.Equal(6, (int)DownloadStatus.Stopped);
        Assert.Equal(7, (int)DownloadStatus.WaitingForWifi);
        Assert.NotEqual("StatusWaitingForWifi", torrent.StatusText);
        Assert.NotEqual("HintWaitingForWifi", torrent.StatusHint);
    }

    private sealed class TestNetwork : ITransferNetworkMonitor
    {
        public bool IsWifiConnected { get; set; } = true;
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CoreTestDirectory _directory = new();
        public TestNetwork Network { get; }
        public StorageService Storage { get; }
        public LocalTorrentService Service { get; }

        public Fixture(bool wifi = true)
        {
            Network = new TestNetwork { IsWifiConnected = wifi };
            Storage = new StorageService(_directory.StoragePaths);
            Service = new LocalTorrentService(Storage, Network, _directory.Path);
        }

        public TorrentItem CreateItem(DownloadStatus status)
        {
            var hash = Guid.NewGuid().ToString("N") + "01234567";
            return new TorrentItem
            {
                InfoHash = hash,
                MagnetLink = $"magnet:?xt=urn:btih:{hash}&dn=test&tr=http%3A%2F%2F127.0.0.1%3A1%2Fannounce",
                Status = status,
                SavePath = Storage.GetDefaultDownloadPath()
            };
        }

        public async Task<TorrentItem> AddAsync()
        {
            var hash = Guid.NewGuid().ToString("N") + "01234567";
            return (await Service.AddTorrentAsync($"magnet:?xt=urn:btih:{hash}&dn=test&tr=http%3A%2F%2F127.0.0.1%3A1%2Fannounce"))!;
        }

        public async Task SetWifiAsync(bool wifi)
        {
            Network.IsWifiConnected = wifi;
            Network.Notify();
            await Service.UpdateWifiOnlyAsync(true);
        }

        public static ClientEngine CreateLocalEngine(string path) => new(new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(path, "engine"),
            AllowPortForwarding = false, AllowLocalPeerDiscovery = false,
            DhtEndPoint = null, AutoSaveLoadDhtCache = false,
            ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>()
        }.ToSettings());

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Storage.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class LocalTorrentService(IStorageService storage, TestNetwork network, string path)
        : TorrentService(storage, new CoreServiceFixture.TestNotifications(), new Background(), ImmediateDispatcher.Instance, networkMonitor: network)
    {
        public int Starts { get; private set; }
        public ClientEngine? CurrentEngine => (ClientEngine?)typeof(TorrentService).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(this);
        protected override ClientEngine CreateEngine() => Fixture.CreateLocalEngine(path);
        protected override Task StartManagerAsync(TorrentManager manager)
        {
            Starts++;
            return base.StartManagerAsync(manager);
        }
    }

    private sealed class Background : IBackgroundDownloadService
    {
        public void Start() { }
        public void Stop() { }
    }
}
