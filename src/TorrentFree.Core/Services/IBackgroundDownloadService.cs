namespace TorrentFree.Services;

/// <summary>
/// Keeps downloads running when the app is minimized/backgrounded.
/// </summary>
public interface IBackgroundDownloadService
{
    /// <summary>
    /// Requests background execution for active transfers.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the platform refused the request (for example Android 12+
    /// refusing to start a foreground service from the background); the caller retries later.
    /// </returns>
    bool Start();

    void Stop();

    /// <summary>Keeps the CPU awake while background execution is active, where supported.</summary>
    void SetKeepDeviceAwake(bool enabled)
    {
    }
}
