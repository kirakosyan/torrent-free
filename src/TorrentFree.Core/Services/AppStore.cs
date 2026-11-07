namespace TorrentFree.Services;

public enum AppUpdateAvailability { Unknown, Current, Available }
public enum AppReviewResult { Failed, Canceled, Submitted, StoreOpened }

/// <summary>Store-specific operations. Opening a listing is not proof of a review.</summary>
public interface IAppStore
{
    bool IsSupported { get; }
    string InstalledVersion { get; }
    Task<AppUpdateAvailability> CheckForUpdateAsync(CancellationToken cancellationToken);
    /// <summary>Opens the update destination and closes the Windows app after a successful handoff.</summary>
    Task<bool> OpenUpdateAsync();
    Task<AppReviewResult> RequestReviewAsync();
    /// <summary>Opens the store for a user-initiated rating or written review, independently of automatic prompts.</summary>
    Task<bool> OpenReviewPageAsync();
}

public interface IDownloadCompletionObserver
{
    Task OnDownloadCompletedAsync(Models.TorrentItem torrent);
}
