using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class InFlightOperationsTests
{
    [Fact]
    public async Task CancelledWaiter_DoesNotCancelOrDuplicateUnderlyingStop()
    {
        var operations = new InFlightOperations<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task Stop() { calls++; return release.Task; }
        var original = operations.RunAsync("manager", Stop);
        using var waiter = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = operations.WaitAsync("manager", waiter.Token);
        waiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(original.IsCompleted);
        Assert.Same(original, operations.RunAsync("manager", Stop));
        var nextWaiter = operations.WaitAsync("manager", TestContext.Current.CancellationToken);
        Assert.False(nextWaiter.IsCompleted);
        release.SetResult();
        await nextWaiter;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedOperation_IsReportedAndNextCallCanRetry()
    {
        var operations = new InFlightOperations<string>();
        await Assert.ThrowsAsync<IOException>(() => operations.RunAsync("manager", () => throw new IOException("stop failed")));
        var retried = false;
        await operations.RunAsync("manager", () => { retried = true; return Task.CompletedTask; });
        Assert.True(retried);
    }
}
