namespace TorrentFree.Services;

/// <summary>
/// Thrown when attempting to add a torrent that already exists in the list.
/// </summary>
public sealed class DuplicateTorrentException : InvalidOperationException
{
    public DuplicateTorrentException(string message) : base(message)
    {
    }
}
