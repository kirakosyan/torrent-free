namespace TorrentFree.Services;

public sealed class MauiTorrentFilePicker : ITorrentFilePicker
{
    public async Task<TorrentPickedFile?> PickTorrentFileAsync(CancellationToken cancellationToken = default)
    {
        var torrentFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/x-bittorrent", "application/octet-stream" } },
            { DevicePlatform.iOS, new[] { "org.bittorrent.torrent", "public.data" } },
            { DevicePlatform.MacCatalyst, new[] { "torrent", "public.data" } },
            { DevicePlatform.WinUI, new[] { ".torrent" } },
        });

        var pickOptions = new PickOptions
        {
            PickerTitle = "Select a .torrent file",
            FileTypes = torrentFileType
        };

        var result = await FilePicker.Default.PickAsync(pickOptions);
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return new TorrentPickedFile(result.FileName, result.FullPath, ms.ToArray());
    }
}
