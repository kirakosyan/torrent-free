using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AppPromptStateStoreTests
{
    [Fact]
    public async Task OptOutAndCounts_SurviveRestartAndIndependentTorrentSettingsWrites()
    {
        using var directory = new CoreTestDirectory();
        var store = new AppPromptStateStore(directory.StoragePaths);
        Assert.Empty((await store.LoadAsync()).CompletedDownloadIds);
        var state = new AppPromptState
        {
            CompletedDownloadIds = ["hash1", "hash2"], ReviewPromptsDisabled = true,
            ReviewSubmitted = true, LastReviewPromptUtc = DateTimeOffset.UtcNow,
            DownloadsAtLastReviewPrompt = 5, CheckedInstalledVersion = "1.13:18",
            LastUpdateCheckUtc = DateTimeOffset.UtcNow, UpdateAvailability = AppUpdateAvailability.Available
        };
        await store.SaveAsync(state);
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        await storage.SaveSettingsAsync(new AppSettings { SortByStatus = true });
        await storage.SaveTorrentsAsync([new TorrentItem { Id = "new-download" }]);
        var restarted = await new AppPromptStateStore(directory.StoragePaths).LoadAsync();
        Assert.Equal(state.CompletedDownloadIds, restarted.CompletedDownloadIds);
        Assert.True(restarted.ReviewPromptsDisabled);
        Assert.True(restarted.ReviewSubmitted);
        Assert.Equal(state.LastReviewPromptUtc, restarted.LastReviewPromptUtc);
        Assert.Equal(state.LastUpdateCheckUtc, restarted.LastUpdateCheckUtc);
        Assert.Equal(state.UpdateAvailability, restarted.UpdateAvailability);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"CompletedDownloadIds\":null}")]
    public async Task CorruptState_IsNotSilentlyReplaced(string contents)
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "store-prompts.json");
        await File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
        var store = new AppPromptStateStore(directory.StoragePaths);
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync());
        Assert.Equal(contents, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedReplacement_PreservesExistingOptOutAndCleansTemporaryFile()
    {
        using var directory = new CoreTestDirectory();
        var store = new AppPromptStateStore(directory.StoragePaths);
        await store.SaveAsync(new AppPromptState { ReviewPromptsDisabled = true });
        var path = Path.Combine(directory.Path, "store-prompts.json");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(new AppPromptState()));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.True((await store.LoadAsync()).ReviewPromptsDisabled);
            Assert.False(File.Exists(path + ".tmp"));
        }
    }
}
