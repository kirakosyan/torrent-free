using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AsyncKeyedLockerTests
{
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
