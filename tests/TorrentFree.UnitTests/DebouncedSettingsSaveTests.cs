using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DebouncedSettingsSaveTests
{
    [Fact]
    public async Task RapidEdits_FlushLatestValueOnceWithoutWaitingForDelay()
    {
        var value = 0;
        var saved = new List<int>();
        var saver = new DebouncedSettingsSave(() =>
        {
            saved.Add(value);
            return Task.CompletedTask;
        }, Timeout.InfiniteTimeSpan);
        var pending = new List<Task>();
        for (value = 1; value <= 10; value++) pending.Add(saver.ScheduleAsync());
        value = 10;

        Assert.Empty(saved);
        await saver.FlushAsync();
        await Task.WhenAll(pending);
        await saver.FlushAsync();
        Assert.Equal([10], saved);
    }

    [Fact]
    public async Task EditDuringWrite_IsIncludedInFlushWithoutOverlappingWrites()
    {
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var value = 1;
        var saved = new List<int>();
        var saver = new DebouncedSettingsSave(async () =>
        {
            saved.Add(value);
            if (saved.Count == 1)
            {
                firstWrite.SetResult();
                await releaseWrite.Task;
            }
        }, Timeout.InfiniteTimeSpan);
        var first = saver.ScheduleAsync();
        var flush = saver.FlushAsync();
        await firstWrite.Task;
        value = 2;
        var second = saver.ScheduleAsync();
        releaseWrite.SetResult();
        await flush;
        await saver.FlushAsync();
        await Task.WhenAll(first, second);
        Assert.Equal([1, 2], saved);
    }

    [Fact]
    public async Task FailedWrite_RemainsPendingForNextFlush()
    {
        var attempts = 0;
        var saver = new DebouncedSettingsSave(() =>
        {
            if (++attempts == 1) throw new IOException("Injected write failure");
            return Task.CompletedTask;
        }, Timeout.InfiniteTimeSpan);
        var pending = saver.ScheduleAsync();
        await Assert.ThrowsAsync<IOException>(() => saver.FlushAsync());
        await saver.FlushAsync();
        await pending;
        Assert.Equal(2, attempts);
    }
}
