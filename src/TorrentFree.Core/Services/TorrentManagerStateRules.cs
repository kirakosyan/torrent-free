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
}
