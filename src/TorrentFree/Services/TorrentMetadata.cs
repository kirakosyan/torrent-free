namespace TorrentFree.Services;

public sealed record TorrentMetadata(
    string? Name,
    string? InfoHashHex,
    IReadOnlyList<string> Trackers);
