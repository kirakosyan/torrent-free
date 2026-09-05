using System.Reflection;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree.UnitTests;

internal sealed class CoreServiceFixture : IAsyncDisposable
{
    public CoreTestDirectory Directory { get; } = new();
    public StorageService Storage { get; }
    public TorrentService Service { get; }
    public ClientEngine Engine { get; }
    public ManualTimeProvider Clock { get; } = new();
    public TestNotifications Notifications { get; } = new();

    public CoreServiceFixture()
    {
        Storage = new StorageService(Directory.StoragePaths);
        Service = new TorrentService(Storage, Notifications, new Background(), ImmediateDispatcher.Instance, Clock);
        Engine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(Directory.Path, "engine"),
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
            DhtEndPoint = null,
            AutoSaveLoadDhtCache = false,
            ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>()
        }.ToSettings());
        typeof(TorrentService).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Engine);
    }

    public async Task<TorrentMetadata> PrepareTorrentAsync(string name = "payload.bin", string? providerPath = "content://test.provider/document/1")
    {
        var source = Path.Combine(Storage.GetDefaultDownloadPath(), name);
        await File.WriteAllTextAsync(source, "Local regression test data: " + name);
        var torrentPath = Path.Combine(Directory.Path, name + ".torrent");
        await new TorrentCreator(TorrentType.V1Only).CreateAsync(new TorrentFileSource(source), torrentPath);
        return await new TorrentImportService(Directory.StoragePaths, new TorrentFileParser()).PrepareAsync(
            new TorrentPickedFile(name + ".torrent", providerPath, await File.ReadAllBytesAsync(torrentPath)));
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(25, timeout.Token);
    }

    public static Task InvokeAsync(TorrentService service, string name, params object?[] arguments)
        => (Task)typeof(TorrentService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, arguments)!;

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        Storage.Dispose();
        Directory.Dispose();
    }

    internal sealed class TestNotifications : INotificationService
    {
        public int Calls;
        public bool Throw { get; set; }
        public Task EnsurePermissionAsync() => Task.CompletedTask;
        public Task ShowDownloadCompletedAsync(TorrentItem torrent)
        {
            Interlocked.Increment(ref Calls);
            if (Throw) throw new IOException("Injected notification failure");
            return Task.CompletedTask;
        }
    }
    private sealed class Background : IBackgroundDownloadService { public void Start() { } public void Stop() { } }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
    public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
}
