namespace TorrentFree.Services;

public interface ITorrentFilePicker
{
    Task<TorrentPickedFile?> PickTorrentFileAsync(CancellationToken cancellationToken = default);
}

public sealed record TorrentPickedFile(string FileName, string? FullPath, byte[] Content);
