using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AppPromptServiceTests
{
    [Fact]
    public async Task FiveUniqueCompletions_OfferOnlyInForeground_AndPersistOfferBeforeRestart()
    {
        var fixture = new Fixture();
        await fixture.CompleteAsync(0, 5);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Null(fixture.Persistence.State.LastReviewPromptUtc);
        await fixture.Service.SetForegroundAsync(true);
        Assert.True(fixture.Service.IsReviewBannerVisible);
        Assert.Equal(5, fixture.Persistence.State.DownloadsAtLastReviewPrompt);

        var restarted = fixture.Restart();
        await restarted.SetForegroundAsync(true);
        Assert.False(restarted.IsReviewBannerVisible);
    }

    [Fact]
    public async Task DuplicateRechecksReaddedTorrentsAndIncompleteDownloads_DoNotInflateCount()
    {
        var fixture = new Fixture();
        await fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(0, 4);
        await fixture.Service.OnDownloadCompletedAsync(WithId("readded"));
        await fixture.Service.OnDownloadCompletedAsync(new TorrentItem { Id = "partial", Progress = 99 });
        await fixture.Service.OnDownloadCompletedAsync(new TorrentItem { Id = "unverified", Progress = 100 });
        Assert.Equal(4, fixture.Persistence.State.CompletedDownloadIds.Length);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        await fixture.CompleteAsync(4, 1);
        Assert.True(fixture.Service.IsReviewBannerVisible);

        TorrentItem WithId(string id)
        {
            var torrent = Completed(0);
            torrent.Id = id;
            torrent.InfoHash = torrent.InfoHash.ToUpperInvariant();
            return torrent;
        }
    }

    [Fact]
    public async Task Later_RequiresBothTenMoreDownloadsAndThirtyDays()
    {
        var fixture = new Fixture();
        await fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(0, 5);
        await fixture.Service.ReviewLaterCommand.ExecuteAsync(null);
        await fixture.CompleteAsync(5, 10);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        fixture.Clock.Now += TimeSpan.FromDays(29);
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        fixture.Clock.Now += TimeSpan.FromDays(1);
        await fixture.Service.SetForegroundAsync(true);
        Assert.True(fixture.Service.IsReviewBannerVisible);

        await fixture.Service.ReviewLaterCommand.ExecuteAsync(null);
        fixture.Clock.Now += TimeSpan.FromDays(31);
        await fixture.CompleteAsync(15, 9);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        await fixture.CompleteAsync(24, 1);
        Assert.True(fixture.Service.IsReviewBannerVisible);
    }

    [Theory]
    [InlineData(AppReviewResult.Submitted, true)]
    [InlineData(AppReviewResult.StoreOpened, false)]
    public async Task SuccessfulRatingOrListingHandoff_SuppressesPermanently_WithoutInventingSubmission(AppReviewResult result, bool submitted)
    {
        var fixture = new Fixture();
        await fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(0, 5);
        fixture.Store.ReviewResult = result;
        await fixture.Service.RateAppCommand.ExecuteAsync(null);
        Assert.True(fixture.Persistence.State.ReviewPromptsDisabled);
        Assert.Equal(submitted, fixture.Persistence.State.ReviewSubmitted);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        fixture.Clock.Now += TimeSpan.FromDays(90);
        var restarted = fixture.Restart();
        for (var i = 5; i < 30; i++) await restarted.OnDownloadCompletedAsync(Completed(i));
        await restarted.SetForegroundAsync(true);
        Assert.False(restarted.IsReviewBannerVisible);
    }

    [Fact]
    public async Task DontAskAgain_SurvivesRestart()
    {
        var fixture = new Fixture();
        await fixture.CompleteAsync(0, 5);
        await fixture.Service.SetForegroundAsync(true);
        await fixture.Service.DisableReviewsCommand.ExecuteAsync(null);
        Assert.True(fixture.Persistence.State.ReviewPromptsDisabled);
        Assert.False(fixture.Persistence.State.ReviewSubmitted);
        await fixture.Restart().SetForegroundAsync(true);
        Assert.Equal(0, fixture.Store.ReviewRequests);
    }

    [Fact]
    public async Task CancellationDelaysNextOffer_ButFailureKeepsRetryAvailable()
    {
        var fixture = new Fixture();
        await fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(0, 5);
        fixture.Store.ReviewResult = AppReviewResult.Failed;
        await fixture.Service.RateAppCommand.ExecuteAsync(null);
        Assert.True(fixture.Service.IsReviewBannerVisible);
        Assert.True(fixture.Service.HasActionError);
        Assert.False(fixture.Persistence.State.ReviewPromptsDisabled);
        fixture.Store.ReviewResult = AppReviewResult.Canceled;
        await fixture.Service.RateAppCommand.ExecuteAsync(null);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.False(fixture.Service.HasActionError);
        Assert.False(fixture.Persistence.State.ReviewPromptsDisabled);
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.IsReviewBannerVisible);
    }

    [Fact]
    public async Task UpdateBannerTakesPriority_DismissalOffersDueReview_AndNeverInstallsAnything()
    {
        var fixture = new Fixture();
        fixture.Store.Availability = AppUpdateAvailability.Available;
        await fixture.CompleteAsync(0, 5);
        await fixture.Service.SetForegroundAsync(true);
        Assert.True(fixture.Service.IsUpdateBannerVisible);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Null(fixture.Persistence.State.LastReviewPromptUtc);
        await fixture.Service.OpenUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Store.ListingRequests);
        await fixture.Service.DismissUpdateCommand.ExecuteAsync(null);
        Assert.False(fixture.Service.IsUpdateBannerVisible);
        Assert.True(fixture.Service.IsReviewBannerVisible);
    }

    [Fact]
    public async Task UpdateCacheSurvivesRestart_ButInstalledVersionChangeInvalidatesIt()
    {
        var fixture = new Fixture();
        fixture.Store.Availability = AppUpdateAvailability.Available;
        await fixture.Service.SetForegroundAsync(true);
        await fixture.Service.SetForegroundAsync(true);
        var restarted = fixture.Restart();
        await restarted.SetForegroundAsync(true);
        Assert.True(restarted.IsUpdateBannerVisible);
        Assert.Equal(1, fixture.Store.UpdateRequests);

        fixture.Store.InstalledVersion = "2:20";
        fixture.Store.Availability = AppUpdateAvailability.Current;
        await fixture.Restart().SetForegroundAsync(true);
        Assert.Equal(2, fixture.Store.UpdateRequests);
        fixture.Clock.Now += TimeSpan.FromHours(24);
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.IsUpdateBannerVisible);
        Assert.Equal(3, fixture.Store.UpdateRequests);
    }

    [Fact]
    public async Task NetworkFailure_IsQuietAndThrottled_AndCanRecover()
    {
        var fixture = new Fixture();
        fixture.Store.ThrowOnCheck = true;
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.IsUpdateBannerVisible);
        Assert.False(fixture.Service.HasActionError);
        await fixture.Service.SetForegroundAsync(true);
        Assert.Equal(1, fixture.Store.UpdateRequests);
        fixture.Clock.Now += TimeSpan.FromHours(1);
        fixture.Store.ThrowOnCheck = false;
        fixture.Store.Availability = AppUpdateAvailability.Available;
        await fixture.Service.SetForegroundAsync(true);
        Assert.True(fixture.Service.IsUpdateBannerVisible);
        Assert.Equal(2, fixture.Store.UpdateRequests);
    }

    [Fact]
    public async Task ConcurrentCompletions_AreNotLostOrCountedTwice()
    {
        var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => fixture.Service.OnDownloadCompletedAsync(Completed(i % 10))));
        Assert.Equal(10, fixture.Persistence.State.CompletedDownloadIds.Length);
        Assert.Equal(10, fixture.Persistence.State.CompletedDownloadIds.Distinct().Count());
    }

    [Fact]
    public async Task BackgroundingDuringStoreCheck_NeverOffersReviewUntilReturn()
    {
        var fixture = new Fixture();
        await fixture.CompleteAsync(0, 5);
        var pending = new TaskCompletionSource<AppUpdateAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.PendingUpdate = pending.Task;
        var activating = fixture.Service.SetForegroundAsync(true);
        await fixture.Service.SetForegroundAsync(false);
        pending.SetResult(AppUpdateAvailability.Current);
        await activating;
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Null(fixture.Persistence.State.LastReviewPromptUtc);
        await fixture.Service.SetForegroundAsync(true);
        Assert.True(fixture.Service.IsReviewBannerVisible);
    }

    [Fact]
    public async Task UnreadableState_DoesNotResetOptOutOrBreakCompletion()
    {
        var fixture = new Fixture();
        fixture.Persistence.FailReads = true;
        await fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(0, 5);
        Assert.False(fixture.Service.HasBanner);
        Assert.Equal(0, fixture.Persistence.Writes);
    }

    [Fact]
    public async Task PendingUpdateCheck_DoesNotBlockCompletionsOrOfferReviewAheadOfUpdate()
    {
        var fixture = new Fixture();
        await fixture.CompleteAsync(0, 4);
        var pending = new TaskCompletionSource<AppUpdateAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.PendingUpdate = pending.Task;
        var activating = fixture.Service.SetForegroundAsync(true);
        await fixture.CompleteAsync(4, 1);
        await fixture.Service.SetForegroundAsync(true); // A duplicate lifecycle event while checking.
        Assert.Equal(5, fixture.Persistence.State.CompletedDownloadIds.Length);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Null(fixture.Persistence.State.LastReviewPromptUtc);
        pending.SetResult(AppUpdateAvailability.Available);
        await activating;
        Assert.True(fixture.Service.IsUpdateBannerVisible);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Equal(1, fixture.Store.UpdateRequests);
    }

    [Fact]
    public async Task FailedPersistence_DoesNotClaimOfferOrLoseCompletionOnRetry()
    {
        var fixture = new Fixture();
        fixture.Persistence.FailWrites = true;
        await fixture.CompleteAsync(0, 5);
        Assert.Empty(fixture.Persistence.State.CompletedDownloadIds);
        fixture.Persistence.FailWrites = false;
        await fixture.CompleteAsync(0, 5);
        fixture.Persistence.FailWrites = true;
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.IsReviewBannerVisible);
        Assert.Null(fixture.Persistence.State.LastReviewPromptUtc);
    }

    [Fact]
    public async Task UnsupportedBuild_DoesNotCheckStoresOrCountDownloads()
    {
        var fixture = new Fixture();
        fixture.Store.IsSupported = false;
        await fixture.CompleteAsync(0, 5);
        await fixture.Service.SetForegroundAsync(true);
        Assert.False(fixture.Service.HasBanner);
        Assert.Equal(0, fixture.Store.UpdateRequests);
        Assert.Equal(0, fixture.Persistence.Writes);
    }

    [Fact]
    public async Task VerifiedEngineCompletion_IsCountedEvenWhenNotificationFails()
    {
        var fixture = new Fixture();
        await using var engine = new CoreServiceFixture(fixture.Service);
        engine.Notifications.Throw = true;
        await engine.Service.InitializeAsync();
        var torrent = (await engine.Service.AddTorrentFileAsync(await engine.PrepareTorrentAsync()))!;
        await engine.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => engine.Notifications.Calls > 0);
        Assert.Equal(100, torrent.Progress);
        Assert.NotNull(torrent.DateCompleted);
        Assert.Single(fixture.Persistence.State.CompletedDownloadIds);
        await engine.Service.StopTorrentAsync(torrent);
        await engine.Service.StartTorrentAsync(torrent);
        Assert.Single(fixture.Persistence.State.CompletedDownloadIds);
    }

    [Fact]
    public async Task BrokenCompletionObserver_DoesNotBreakSeedingOrNotifications()
    {
        await using var engine = new CoreServiceFixture(new BrokenObserver());
        await engine.Service.InitializeAsync();
        var torrent = (await engine.Service.AddTorrentFileAsync(await engine.PrepareTorrentAsync()))!;
        await engine.Service.StartTorrentAsync(torrent);
        await CoreServiceFixture.WaitUntilAsync(() => engine.Notifications.Calls > 0);
        Assert.NotNull(torrent.DateCompleted);
        Assert.Equal(DownloadStatus.Seeding, torrent.Status);
    }

    private sealed class BrokenObserver : IDownloadCompletionObserver
    {
        public Task OnDownloadCompletedAsync(TorrentItem torrent) => throw new IOException();
    }

    private static TorrentItem Completed(int number) => new()
    {
        Id = Guid.NewGuid().ToString(), InfoHash = number.ToString("x40"),
        Progress = 100, DateCompleted = DateTime.UtcNow, Status = DownloadStatus.Seeding
    };

    private sealed class Fixture
    {
        public FakeStore Store { get; } = new();
        public MemoryStateStore Persistence { get; } = new();
        public Clock Clock { get; } = new();
        public AppPromptService Service { get; }
        public Fixture() => Service = Restart();
        public AppPromptService Restart() => new(Store, Persistence, new Dispatcher(), Clock);
        public async Task CompleteAsync(int start, int count)
        {
            for (var i = start; i < start + count; i++) await Service.OnDownloadCompletedAsync(Completed(i));
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class MemoryStateStore : IAppPromptStateStore
    {
        public AppPromptState State { get; private set; } = new();
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public int Writes { get; private set; }
        public Task<AppPromptState> LoadAsync() => FailReads ? throw new IOException() : Task.FromResult(State);
        public async Task SaveAsync(AppPromptState state)
        {
            if (FailWrites) throw new IOException();
            // Exercise overlapping callers rather than completing every fake write synchronously.
            await Task.Yield();
            State = state;
            Writes++;
        }
    }

    private sealed class FakeStore : IAppStore
    {
        public bool IsSupported { get; set; } = true;
        public string InstalledVersion { get; set; } = "1.13:18";
        public AppUpdateAvailability Availability { get; set; } = AppUpdateAvailability.Current;
        public AppReviewResult ReviewResult { get; set; }
        public bool ThrowOnCheck { get; set; }
        public Task<AppUpdateAvailability>? PendingUpdate { get; set; }
        public int UpdateRequests { get; private set; }
        public int ListingRequests { get; private set; }
        public int ReviewRequests { get; private set; }
        public Task<AppUpdateAvailability> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            UpdateRequests++;
            if (ThrowOnCheck) throw new IOException();
            return PendingUpdate ?? Task.FromResult(Availability);
        }
        public Task<bool> OpenListingAsync() { ListingRequests++; return Task.FromResult(true); }
        public Task<AppReviewResult> RequestReviewAsync() { ReviewRequests++; return Task.FromResult(ReviewResult); }
    }
}
