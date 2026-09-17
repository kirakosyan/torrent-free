using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AudiobookHandoffCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "handoff-cache-" + Guid.NewGuid());

    [Fact]
    public async Task RetriesReuseCopiesAndCleanupPreservesRecentPendingImports()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "book.mp3");
        File.WriteAllText(source, "original");
        var now = DateTime.UtcNow;
        var cache = new AudiobookHandoffCache(Path.Combine(root, "cache"), () => now);
        var first = await cache.StageAsync(source, TestContext.Current.CancellationToken);
        now = now.AddHours(1);
        Assert.Equal(first, await cache.StageAsync(source, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "cache")));
        var other = Path.Combine(root, "second.mp3");
        File.WriteAllText(other, "second");
        var second = await cache.StageAsync(other, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(first)); // A second intent must not invalidate a pending first import.
        now = now.AddDays(8);
        await cache.StageAsync(other, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal("original", File.ReadAllText(source));
    }

    [Fact]
    public async Task ChangedSourceUsesNewCopyWithoutOverwritingAnExistingGrant()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "book.mp3");
        File.WriteAllText(source, "first");
        var cache = new AudiobookHandoffCache(Path.Combine(root, "cache"));
        var first = await cache.StageAsync(source, TestContext.Current.CancellationToken);
        File.WriteAllText(source, "different content");
        var second = await cache.StageAsync(source, TestContext.Current.CancellationToken);
        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("different content", File.ReadAllText(second));
    }

    [Fact]
    public async Task FailedCopyPreservesOriginalExceptionAndCanBeRetried()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "book.mp3");
        File.WriteAllText(source, "original");
        var cache = new AudiobookHandoffCache(Path.Combine(root, "cache"));
        using (var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => cache.StageAsync(source, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "cache"), "*", SearchOption.AllDirectories));
        Assert.True(File.Exists(await cache.StageAsync(source, TestContext.Current.CancellationToken)));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
