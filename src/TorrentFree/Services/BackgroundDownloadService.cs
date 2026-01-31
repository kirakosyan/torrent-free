namespace TorrentFree.Services;

/// <summary>
/// Default no-op background service for platforms that don't need it.
/// </summary>
public sealed class BackgroundDownloadService : IBackgroundDownloadService
{
    public void Start()
    {
    }

    public void Stop()
    {
    }
}
