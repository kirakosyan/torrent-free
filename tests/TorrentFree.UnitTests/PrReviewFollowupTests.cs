using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class PrReviewFollowupTests
{
    [Fact]
    public async Task ForegroundResume_StartsQueuedTorrentWithoutAnotherUserAction()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await fixture.Service.PauseAllForBackgroundTimeoutAsync();
        await CoreServiceFixture.InvokeAsync(fixture.Service, "TryStartQueuedTorrentsAsync");
        Assert.Equal(DownloadStatus.Queued, torrent.Status);
        fixture.Service.ResumeAfterBackgroundTimeout();
        await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
        Assert.Single(fixture.Engine.Torrents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SeedWithinCapacity_DoesNotWaitForItsOperationLock(int limit)
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        fixture.Service.UpdateQueueLimits(limit, limit);
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.Status = DownloadStatus.Seeding;
        var locker = (AsyncKeyedLocker)typeof(TorrentService).GetField("_torrentOperationLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
        await using var held = await locker.AcquireAsync(torrent.Id, TestContext.Current.CancellationToken);
        await CoreServiceFixture.InvokeAsync(fixture.Service, "QueueIfOverCapacityAsync", torrent)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvedDownloadPath_RestoresRegardlessOfJsonPropertyOrder(bool resolvedFirst)
    {
        using var directory = new CoreTestDirectory();
        var savePath = Path.Combine(directory.Path, "downloads");
        var resolvedPath = Path.Combine(savePath, "engine-escaped-name");
        var properties = resolvedFirst
            ? new[] { new { Name = "ResolvedDownloadPath", Value = resolvedPath }, new { Name = "SavePath", Value = savePath } }
            : new[] { new { Name = "SavePath", Value = savePath }, new { Name = "ResolvedDownloadPath", Value = resolvedPath } };
        var json = JsonSerializer.Serialize(properties.ToDictionary(p => p.Name, p => p.Value));
        var torrent = JsonSerializer.Deserialize<TorrentItem>(json)!;
        Assert.Equal(resolvedPath, torrent.DownloadedFilePath);
        torrent.SavePath = Path.Combine(directory.Path, "elsewhere");
        Assert.Null(torrent.ResolvedDownloadPath);
    }

    [Fact]
    public async Task PreparedReimport_RestoresCacheDeletedBeforePublication()
    {
        await using var fixture = new CoreServiceFixture();
        var original = await fixture.PrepareTorrentAsync();
        var torrent = (await fixture.Service.AddTorrentFileAsync(original))!;
        var content = await File.ReadAllBytesAsync(original.CachedFilePath!, TestContext.Current.CancellationToken);
        var prepared = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser())
            .PrepareAsync(new TorrentPickedFile("reimport.torrent", null, content), TestContext.Current.CancellationToken);
        await fixture.Service.RemoveTorrentAsync(torrent);
        Assert.False(File.Exists(prepared.CachedFilePath));
        var reimported = (await fixture.Service.AddTorrentFileAsync(prepared))!;
        Assert.Equal(content, await File.ReadAllBytesAsync(reimported.CachedTorrentFilePath!, TestContext.Current.CancellationToken));
        await fixture.Service.StartTorrentAsync(reimported);
        Assert.True(Assert.Single(fixture.Engine.Torrents).HasMetadata);
    }

    [Fact]
    public async Task SyncDispose_FlushesWithoutPostingToBlockedCallerContext()
    {
        await using var fixture = new CoreServiceFixture();
        var torrent = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        torrent.MaxSeedMinutes = 73;
        var context = new QueuedContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                entered.SetResult();
                fixture.Service.Dispose();
            }
            finally { SynchronizationContext.SetSynchronizationContext(null); }
        }, TestContext.Current.CancellationToken);
        await entered.Task;
        try
        {
            await Task.WhenAny(disposal, context.Posted.Task).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(context.Posted.Task.IsCompleted, "Disposal posted a continuation to its blocked caller.");
            await disposal;
        }
        finally
        {
            // Let a regression unwind rather than leaving a blocked worker behind.
            while (!disposal.IsCompleted)
            {
                while (context.Work.TryDequeue(out var work)) work.Callback(work.State);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal(73, Assert.Single(await fixture.Storage.LoadTorrentsAsync()).MaxSeedMinutes);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        public ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Work { get; } = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback d, object? state)
        {
            Work.Enqueue((d, state));
            Posted.TrySetResult();
        }
    }
}
