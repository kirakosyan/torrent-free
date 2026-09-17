using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AudiobookAvailabilityCacheTests
{
    [Fact]
    public async Task DuplicateNotificationsShareOneScanAndNegativeResultsStayCached()
    {
        var count = 0;
        var cache = new AudiobookAvailabilityCache(_ => { Interlocked.Increment(ref count); return false; });
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => cache.HasAudioAsync("same-path")));
        Assert.False(await cache.HasAudioAsync("same-path"));
        Assert.Equal(1, count);
        cache.Invalidate("same-path");
        await cache.HasAudioAsync("same-path");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task InvalidatedResultsCannotOverwriteANewerScan()
    {
        var cache = new AudiobookAvailabilityCache(_ => true);
        var old = cache.HasAudioAsync("path");
        cache.Invalidate("path");
        var current = cache.HasAudioAsync("path");
        await Task.WhenAll(old, current);
        Assert.False(cache.IsCurrent("path", old));
        Assert.True(cache.IsCurrent("path", current));
    }
}
