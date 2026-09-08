using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AsyncKeyedLockerTests
{
    [Fact]
    public async Task Dispose_AllowsExistingWaiterToFinishAndRejectsNewAcquisitions()
    {
        var locker = new AsyncKeyedLocker();
        var first = await locker.AcquireAsync("torrent");
        var waiting = locker.AcquireAsync("torrent").AsTask();
        locker.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await locker.AcquireAsync("other"));
        first.Dispose();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotBreakSubsequentAcquisitions()
    {
        using var locker = new AsyncKeyedLocker();
        var first = await locker.AcquireAsync("torrent");
        using var cancellation = new CancellationTokenSource();
        var waiting = locker.AcquireAsync("torrent", cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        first.Dispose();
        await using var next = await locker.AcquireAsync("torrent").AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentRelease_IsIdempotent()
    {
        using var locker = new AsyncKeyedLocker();
        for (var i = 0; i < 100; i++)
        {
            var handle = await locker.AcquireAsync("torrent");
            await Task.WhenAll(Task.Run(handle.Dispose), Task.Run(handle.Dispose));
            await using var next = await locker.AcquireAsync("torrent").AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AcquireAsync_SerializesAccessForSameKey()
    {
        using var locker = new AsyncKeyedLocker();

        var firstAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await using var firstHandle = await locker.AcquireAsync("torrent-1", TestContext.Current.CancellationToken);
            firstAcquired.SetResult();
            await releaseFirst.Task;
        }, TestContext.Current.CancellationToken);

        await firstAcquired.Task;

        var secondTask = Task.Run(async () =>
        {
            await using var secondHandle = await locker.AcquireAsync("torrent-1", TestContext.Current.CancellationToken);
            secondAcquired.SetResult();
        }, TestContext.Current.CancellationToken);

        var acquiredBeforeRelease = await Task.WhenAny(
            secondAcquired.Task,
            Task.Delay(100, TestContext.Current.CancellationToken));
        Assert.NotSame(secondAcquired.Task, acquiredBeforeRelease);

        releaseFirst.SetResult();

        await Task.WhenAll(firstTask, secondTask);
        Assert.True(secondAcquired.Task.IsCompletedSuccessfully);
    }
}
