using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>Persistent review scheduling and cached update checks, independent of the platform UI.</summary>
public partial class AppPromptService(
    IAppStore store, IAppPromptStateStore stateStore, IUiDispatcher dispatcher,
    TimeProvider? timeProvider = null) : ObservableObject, IDownloadCompletionObserver
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private AppPromptState? _state;
    private volatile bool _foreground;
    private volatile bool _updateCheckInProgress;
    private bool _reviewPending;
    private bool _updateDismissed;
    private int _actionInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial bool IsUpdateBannerVisible { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial bool IsReviewBannerVisible { get; private set; }

    public bool HasBanner => IsUpdateBannerVisible || IsReviewBannerVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    public partial bool IsActionInProgress { get; private set; }

    public bool CanAct => !IsActionInProgress;

    [ObservableProperty]
    public partial bool HasActionError { get; private set; }

    public Task SetForegroundAsync(bool foreground)
    {
        _foreground = foreground;
        return RunSafelyAsync(async () =>
        {
            if (!foreground)
            {
                await _stateLock.WaitAsync();
                try
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (!_foreground) IsReviewBannerVisible = false;
                    });
                }
                finally { _stateLock.Release(); }
                return;
            }

            if (!store.IsSupported) return;
            // Show cached availability immediately. Network work never blocks app initialization.
            await RefreshVisibilityAsync(allowNewReview: false);
            await CheckForUpdatesAsync();
            await RefreshVisibilityAsync();
        });
    }

    public Task OnDownloadCompletedAsync(TorrentItem torrent)
    {
        // The engine can enter Seeding after Progress was sampled. Its completion marker wins.
        if (!store.IsSupported || torrent.DateCompleted is null)
            return Task.CompletedTask;
        var identity = string.IsNullOrWhiteSpace(torrent.InfoHash)
            ? "id:" + torrent.Id
            : "hash:" + torrent.InfoHash.Trim().ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return RunSafelyAsync(async () =>
        {
            await ChangeStateAsync(state => state.ReviewPromptsDisabled || state.CompletedDownloadIds.Contains(key)
                ? state
                : state with { CompletedDownloadIds = [.. state.CompletedDownloadIds, key] });
            await RefreshVisibilityAsync();
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!await _updateLock.WaitAsync(0)) return;
        _updateCheckInProgress = true;
        try
        {
            var state = await ChangeStateAsync(state => state);
            var sameVersion = state.CheckedInstalledVersion == store.InstalledVersion;
            var interval = state.UpdateAvailability == AppUpdateAvailability.Unknown
                ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24);
            var age = _clock.GetUtcNow() - state.LastUpdateCheckUtc;
            if (sameVersion && age >= TimeSpan.Zero && age < interval) return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            AppUpdateAvailability result;
            try { result = await store.CheckForUpdateAsync(timeout.Token).WaitAsync(timeout.Token); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Store update check unavailable: {ex.Message}");
                result = AppUpdateAvailability.Unknown;
            }
            await ChangeStateAsync(current => current with
            {
                CheckedInstalledVersion = store.InstalledVersion,
                LastUpdateCheckUtc = _clock.GetUtcNow(),
                UpdateAvailability = result
            });
        }
        finally
        {
            _updateCheckInProgress = false;
            _updateLock.Release();
        }
    }

    private async Task RefreshVisibilityAsync(bool allowNewReview = true, bool dismissReview = false, bool dismissUpdate = false)
    {
        await _stateLock.WaitAsync();
        try
        {
            // Apply user choices after any pending offer has finished saving/showing.
            if (dismissReview) _reviewPending = false;
            if (dismissUpdate) _updateDismissed = true;
            _state ??= await stateStore.LoadAsync();
            var now = _clock.GetUtcNow();
            if (allowNewReview && _foreground && !_state.ReviewPromptsDisabled && _state.LastReviewPromptUtc > now)
            {
                // A corrected device clock starts a fresh cooldown, without prompting immediately.
                var corrected = _state with { LastReviewPromptUtc = now };
                await stateStore.SaveAsync(corrected);
                _state = corrected;
            }
            var updateVisible = !_updateDismissed
                && _state.CheckedInstalledVersion == store.InstalledVersion
                && _state.UpdateAvailability == AppUpdateAvailability.Available;
            if (allowNewReview && _foreground && !_updateCheckInProgress
                && !updateVisible && !_reviewPending && IsReviewDue(_state, now))
            {
                // Persist when offered, not when dismissed, so closing the app does not cause nagging.
                var next = _state with
                {
                    LastReviewPromptUtc = now,
                    DownloadsAtLastReviewPrompt = _state.CompletedDownloadIds.Length
                };
                await stateStore.SaveAsync(next);
                _state = next;
                _reviewPending = true;
            }
            var reviewVisible = _foreground && _reviewPending && !updateVisible && !_state.ReviewPromptsDisabled;
            await dispatcher.InvokeAsync(() =>
            {
                IsUpdateBannerVisible = updateVisible;
                // Read at execution time: the page may have stopped while this callback was queued.
                IsReviewBannerVisible = _foreground && reviewVisible;
            });
        }
        finally { _stateLock.Release(); }
    }

    internal static bool IsReviewDue(AppPromptState state, DateTimeOffset now) =>
        !state.ReviewPromptsDisabled && state.CompletedDownloadIds.Length >= 5
        && (state.LastReviewPromptUtc is null
            || (state.CompletedDownloadIds.Length - state.DownloadsAtLastReviewPrompt >= 10
                && now - state.LastReviewPromptUtc >= TimeSpan.FromDays(30)));

    [RelayCommand]
    private Task DismissUpdateAsync() => RunSafelyAsync(async () =>
    {
        await dispatcher.InvokeAsync(() => HasActionError = false);
        await RefreshVisibilityAsync(dismissUpdate: true);
    });

    [RelayCommand]
    private Task ReviewLaterAsync() => RunSafelyAsync(async () =>
    {
        await dispatcher.InvokeAsync(() => HasActionError = false);
        await RefreshVisibilityAsync(allowNewReview: false, dismissReview: true);
    });

    [RelayCommand]
    private Task DisableReviewsAsync() => RunActionAsync(async () =>
    {
        await ChangeStateAsync(state => state with { ReviewPromptsDisabled = true });
        await RefreshVisibilityAsync(allowNewReview: false, dismissReview: true);
    });

    [RelayCommand]
    private Task OpenUpdateAsync() => RunActionAsync(async () =>
    {
        if (!await store.OpenListingAsync())
            await dispatcher.InvokeAsync(() => HasActionError = true);
    });

    [RelayCommand]
    private Task RateAppAsync() => RunActionAsync(async () =>
    {
        var result = await store.RequestReviewAsync();
        if (result == AppReviewResult.Failed)
        {
            await dispatcher.InvokeAsync(() => HasActionError = true);
            return;
        }
        if (result is AppReviewResult.Submitted or AppReviewResult.StoreOpened)
        {
            await ChangeStateAsync(state => state with
            {
                ReviewPromptsDisabled = true,
                ReviewSubmitted = result == AppReviewResult.Submitted
            });
        }
        await RefreshVisibilityAsync(allowNewReview: false, dismissReview: true);
    });

    private async Task RunActionAsync(Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref _actionInProgress, 1, 0) != 0) return;
        try
        {
            await dispatcher.InvokeAsync(() => { IsActionInProgress = true; HasActionError = false; });
            await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Store prompt action failed: {ex.Message}");
            await RunSafelyAsync(() => dispatcher.InvokeAsync(() => HasActionError = true));
        }
        finally
        {
            Interlocked.Exchange(ref _actionInProgress, 0);
            await RunSafelyAsync(() => dispatcher.InvokeAsync(() => IsActionInProgress = false));
        }
    }

    private async Task<AppPromptState> ChangeStateAsync(Func<AppPromptState, AppPromptState> update)
    {
        await _stateLock.WaitAsync();
        try
        {
            _state ??= await stateStore.LoadAsync();
            var next = update(_state);
            if (!ReferenceEquals(next, _state))
            {
                await stateStore.SaveAsync(next);
                _state = next;
            }
            return _state;
        }
        finally { _stateLock.Release(); }
    }

    private static async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Store prompts unavailable: {ex.Message}"); }
    }
}
