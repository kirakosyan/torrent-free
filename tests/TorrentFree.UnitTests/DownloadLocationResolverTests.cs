using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadLocationResolverTests
{
    [Fact]
    public void ResolveSavePath_UsesTorrentFolder_WhenTorrentFolderModeIsEnabled()
    {
        var settings = new AppSettings
        {
            DownloadToTorrentFolder = true,
            SpecificDownloadFolder = @"D:\Ignored"
        };

        var path = DownloadLocationResolver.ResolveSavePath(settings, @"C:\Torrents\ubuntu.torrent", @"E:\Fallback");

        Assert.Equal(@"C:\Torrents", path);
    }

    [Fact]
    public void ResolveSavePath_UsesSpecificFolder_WhenConfigured()
    {
        var settings = new AppSettings
        {
            DownloadToTorrentFolder = false,
            SpecificDownloadFolder = @"D:\Media\Downloads"
        };

        var path = DownloadLocationResolver.ResolveSavePath(settings, @"C:\Torrents\ubuntu.torrent", @"E:\Fallback");

        Assert.Equal(@"D:\Media\Downloads", path);
    }

    [Fact]
    public void ResolveSavePath_FallsBack_WhenSpecificFolderIsEmpty()
    {
        var settings = new AppSettings
        {
            DownloadToTorrentFolder = false,
            SpecificDownloadFolder = "   "
        };

        var path = DownloadLocationResolver.ResolveSavePath(settings, sourceTorrentFilePath: null, fallbackDownloadPath: @"E:\Fallback");

        Assert.Equal(@"E:\Fallback", path);
    }

    [Fact]
    public void ResolveSavePath_FallsBack_WhenTorrentPathIsUnavailable()
    {
        var settings = new AppSettings
        {
            DownloadToTorrentFolder = true
        };

        var path = DownloadLocationResolver.ResolveSavePath(settings, sourceTorrentFilePath: null, fallbackDownloadPath: @"E:\Fallback");

        Assert.Equal(@"E:\Fallback", path);
    }
}