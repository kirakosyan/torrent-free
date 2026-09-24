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
            PickerTitle = LocalizationResourceManager.Instance["SelectTorrentFile"],
            FileTypes = torrentFileType
        };

        var result = await FilePicker.Default.PickAsync(pickOptions);
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        var content = await TorrentFileContentReader.ReadAsync(stream, cancellationToken);
#if WINDOWS
        var sourcePath = result.FullPath;
#else
        // Android, iOS and Mac Catalyst pickers return a temporary copy in the app's cache,
        // not the user's file. Treating that as the source folder placed downloads in a cache
        // the OS may clear, and "delete the .torrent file" only removed the copy.
        string? sourcePath = null;
#endif
        return new TorrentPickedFile(result.FileName, sourcePath, content);
    }
}
