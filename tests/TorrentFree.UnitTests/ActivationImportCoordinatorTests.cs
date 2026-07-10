using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class ActivationImportCoordinatorTests
{
    [Fact]
    public async Task ImportAsync_InitializesBeforeImportingPaths()
    {
        var events = new List<string>();

        await ActivationImportCoordinator.ImportAsync(
            new[] { "first.torrent", "second.torrent" },
            async () =>
            {
                events.Add("initialize:start");
                await Task.Yield();
                events.Add("initialize:end");
            },
            async path =>
            {
                events.Add($"import:{path}");
                await Task.Yield();
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                "initialize:start",
                "initialize:end",
                "import:first.torrent",
                "import:second.torrent"
            },
            events);
    }

    [Fact]
    public async Task ImportAsync_DeduplicatesPathsBeforeImport()
    {
        var importedPaths = new List<string>();

        await ActivationImportCoordinator.ImportAsync(
            new[] { "same.torrent", "SAME.torrent", "other.torrent" },
            static () => Task.CompletedTask,
            path =>
            {
                importedPaths.Add(path);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "same.torrent", "other.torrent" }, importedPaths);
    }

    [Fact]
    public async Task ImportAsync_SkipsInitializationWhenThereAreNoPaths()
    {
        var initializeCalled = false;
        var importCalled = false;

        await ActivationImportCoordinator.ImportAsync(
            Array.Empty<string>(),
            () =>
            {
                initializeCalled = true;
                return Task.CompletedTask;
            },
            _ =>
            {
                importCalled = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.False(initializeCalled);
        Assert.False(importCalled);
    }

    [Fact]
    public async Task ImportAsync_SerializesConcurrentActivationSources()
    {
        var firstImportStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstImport = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondImportStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;

        async Task ImportAsync(string _)
        {
            if (Interlocked.Increment(ref invocation) == 1)
            {
                firstImportStarted.SetResult(true);
                await releaseFirstImport.Task;
                return;
            }

            secondImportStarted.SetResult(true);
        }

        var first = ActivationImportCoordinator.ImportAsync(
            new[] { "same.torrent" },
            static () => Task.CompletedTask,
            ImportAsync,
            TestContext.Current.CancellationToken);

        await firstImportStarted.Task;

        var second = ActivationImportCoordinator.ImportAsync(
            new[] { "same.torrent" },
            static () => Task.CompletedTask,
            ImportAsync,
            TestContext.Current.CancellationToken);

        Assert.False(secondImportStarted.Task.IsCompleted);

        releaseFirstImport.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.True(secondImportStarted.Task.IsCompletedSuccessfully);
    }
}
