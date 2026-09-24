using MonoTorrent.Client;

namespace TorrentFree.Services;

/// <summary>
/// Encapsulates manager-state rules shared by stop/remove flows.
/// User-facing stop must fully stop the backend manager so later remove/app
/// shutdown do not inherit a half-paused session.
/// </summary>
internal static class TorrentManagerStateRules
{
    public static bool RequiresFullStop(TorrentState state) => state != TorrentState.Stopped;

    /// <summary>
    /// MonoTorrent 3.x only pauses a manager which is downloading or seeding. In every other
    /// running state (metadata retrieval, starting, hashing, error) <c>PauseAsync</c> is a
    /// no-op, and a magnet which later receives its metadata starts downloading by itself.
    /// Those managers must be stopped to honour a pause.
    /// </summary>
    public static bool CanPauseInPlace(TorrentState state)
        => state is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Paused;
}
