namespace TorrentFree.Services;

/// <summary>
/// Keeps downloads running when the app is minimized/backgrounded.
/// </summary>
public interface IBackgroundDownloadService
{
    void Start();
    void Stop();
}
