namespace TorrentFree.Services;

public sealed record TorrentMetadata(
    string? Name,
    string? InfoHashHex,
    IReadOnlyList<string> Trackers)
{
    public string? SourceFilePath { get; init; }
    public string? SourceFileName { get; init; }
    public string? CachedFilePath { get; init; }
    public string? DownloadSourcePath { get; init; }
}
