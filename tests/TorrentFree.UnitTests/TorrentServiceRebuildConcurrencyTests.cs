using System.Reflection;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentServiceRebuildConcurrencyTests
{
    [Fact]
    public async Task Rebuild_WaitsForStartWhichEnteredBeforeBarrier()
    {
        var storage = new StubStorageService();
        await using var service = new BlockingStartTorrentService(storage);
        var torrent = CreateTorrent(storage);
        service.Torrents.Add(torrent);

        var startTask = service.StartTorrentAsync(torrent);
        await service.ManagerCreationEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var rebuildTask = InvokePrivateTask(service, "RebuildEngineAsync");

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(rebuildTask.IsCompleted);

        service.ReleaseManagerCreation.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => startTask);
        await rebuildTask.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Start_RechecksBarrierAfterWaitingForTorrentLock()
    {
        var storage = new StubStorageService();
        await using var service = new BlockingStartTorrentService(storage);
        var torrent = CreateTorrent(storage);
        service.Torrents.Add(torrent);
        var locker = GetPrivateField<AsyncKeyedLocker>(service, "_torrentOperationLock");
        var heldLock = await locker.AcquireAsync(torrent.Id, TestContext.Current.CancellationToken);

        var startTask = service.StartTorrentAsync(torrent);
        await Task.Delay(25, TestContext.Current.CancellationToken);
        var (barrier, startsDrained) = BeginStartBarrier(service);
        Assert.True(startsDrained.IsCompleted);
        await heldLock.DisposeAsync();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(service.ManagerCreationEntered.Task.IsCompleted);

        EndStartBarrier(service, barrier);
        await service.ManagerCreationEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        service.ReleaseManagerCreation.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => startTask);
    }

    [Fact]
    public async Task RapidProxyDisableEnable_CancelsObsoleteDirectRestart()
    {
        var storage = new StubStorageService();
        await using var service = new ProxyTransitionTorrentService(storage);
        var torrent = CreateTorrent(storage);
        var externalTorrent = CreateTorrent(
            storage,
            "89abcdef0123456789abcdef0123456789abcdef");
        service.Torrents.Add(torrent);
        service.Torrents.Add(externalTorrent);

        service.UpdateProxySettings(true, "127.0.0.1", 1080, string.Empty, string.Empty);
        await WaitForRebuildToSettleAsync(service);
        await service.StartTorrentAsync(torrent);
        await service.InitialStartEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.UpdateProxySettings(false, string.Empty, 1080, string.Empty, string.Empty);
        await service.ObsoleteDirectStartEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.UpdateProxySettings(true, "127.0.0.1", 1080, string.Empty, string.Empty);
        var externalStart = service.StartTorrentAsync(externalTorrent);
        Assert.False(externalStart.IsCompleted);
        Assert.Equal(2, service.StartManagerCallCount);
        service.ReleaseObsoleteDirectStart.TrySetResult();

        await service.LatestProxyStartEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await externalStart.WaitAsync(TestContext.Current.CancellationToken);
        await service.ExternalStartEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await WaitForRebuildToSettleAsync(service);

        Assert.Equal(4, service.StartManagerCallCount);
        Assert.Equal(DownloadStatus.Downloading, torrent.Status);
        Assert.Equal(DownloadStatus.Downloading, externalTorrent.Status);
        var pending = GetPrivateField<System.Collections.Concurrent.ConcurrentDictionary<string, byte>>(
            service,
            "_proxyRebuildPendingResumeIds");
        Assert.Empty(pending);
    }

    private static TorrentItem CreateTorrent(
        StubStorageService storage,
        string infoHash = "0123456789abcdef0123456789abcdef01234567")
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Concurrency test",
            MagnetLink = $"magnet:?xt=urn:btih:{infoHash}",
            InfoHash = infoHash,
            SavePath = storage.GetDefaultDownloadPath(),
            Status = DownloadStatus.Queued
        };

    private static Task InvokePrivateTask(object instance, string methodName)
    {
        var method = instance.GetType().BaseType!.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method!.Invoke(instance, null));
    }

    private static (TaskCompletionSource Barrier, Task StartsDrained) BeginStartBarrier(TorrentService service)
    {
        var method = typeof(TorrentService).GetMethod(
            "BeginEngineRebuild",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = [null];
        var barrier = Assert.IsType<TaskCompletionSource>(method!.Invoke(service, arguments));
        var startsDrained = Assert.IsAssignableFrom<Task>(arguments[0]);
        return (barrier, startsDrained);
    }

    private static void EndStartBarrier(TorrentService service, TaskCompletionSource barrier)
    {
        var method = typeof(TorrentService).GetMethod(
            "EndEngineRebuild",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(service, [barrier]);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = typeof(TorrentService).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static async Task WaitForRebuildToSettleAsync(TorrentService service)
    {
        var activeRebuildField = typeof(TorrentService).GetField(
            "_activeEngineRebuild",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(activeRebuildField);
        var rebuildLock = GetPrivateField<SemaphoreSlim>(service, "_engineRebuildLock");

        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (activeRebuildField!.GetValue(service) is not null || rebuildLock.CurrentCount == 0)
        {
            Assert.True(DateTime.UtcNow < timeoutAt, "Proxy rebuild did not settle within five seconds.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class BlockingStartTorrentService(IStorageService storageService)
        : TorrentService(storageService, new StubNotificationService(), new StubBackgroundDownloadService())
    {
        public TaskCompletionSource ManagerCreationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseManagerCreation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<TorrentManager> GetOrCreateManagerAsync(TorrentItem torrent)
        {
            ManagerCreationEntered.TrySetResult();
            await ReleaseManagerCreation.Task;
            throw new InvalidOperationException("simulated manager creation failure");
        }
    }

    private sealed class ProxyTransitionTorrentService(IStorageService storageService)
        : TorrentService(storageService, new StubNotificationService(), new StubBackgroundDownloadService())
    {
        private int _startManagerCallCount;

        public int StartManagerCallCount => Volatile.Read(ref _startManagerCallCount);

        public TaskCompletionSource InitialStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ObsoleteDirectStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseObsoleteDirectStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource LatestProxyStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ExternalStartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task StartManagerAsync(TorrentManager manager)
        {
            switch (Interlocked.Increment(ref _startManagerCallCount))
            {
                case 1:
                    InitialStartEntered.TrySetResult();
                    return;
                case 2:
                    ObsoleteDirectStartEntered.TrySetResult();
                    await ReleaseObsoleteDirectStart.Task;
                    return;
                case 3:
                    LatestProxyStartEntered.TrySetResult();
                    return;
                case 4:
                    ExternalStartEntered.TrySetResult();
                    return;
                default:
                    throw new InvalidOperationException("Unexpected extra manager start.");
            }
        }
    }

    private sealed class StubStorageService : IStorageService
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "torrent-free-tests",
            Guid.NewGuid().ToString("N"));

        public Task<List<TorrentItem>> LoadTorrentsAsync() => Task.FromResult(new List<TorrentItem>());

        public Task SaveTorrentsAsync(IEnumerable<TorrentItem> torrents) => Task.CompletedTask;

        public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;

        public string GetDefaultDownloadPath()
        {
            Directory.CreateDirectory(_path);
            return _path;
        }
    }

    private sealed class StubNotificationService : INotificationService
    {
        public Task EnsurePermissionAsync() => Task.CompletedTask;

        public Task ShowDownloadCompletedAsync(TorrentItem torrent) => Task.CompletedTask;
    }

    private sealed class StubBackgroundDownloadService : IBackgroundDownloadService
    {
        public void Start()
        {
        }

        public void Stop()
        {
        }
    }
}
