using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadFolderExportCoordinatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingExport_DoesNotOpenDownloadsOrReportSuccess(string? destination)
    {
        var opened = false;
        var result = await DownloadFolderExportCoordinator.ExportAndOpenAsync(() => Task.FromResult(destination),
            _ => opened = true, () => opened = true);
        Assert.False(result);
        Assert.False(opened);
    }

    [Fact]
    public async Task FailedExport_PropagatesErrorWithoutOpeningDownloads()
    {
        var opened = false;
        await Assert.ThrowsAsync<IOException>(() => DownloadFolderExportCoordinator.ExportAndOpenAsync(
            () => Task.FromException<string?>(new IOException("Storage full")), _ => opened = true, () => opened = true));
        Assert.False(opened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedExport_UsesDownloadsFallbackOnlyIfNeeded(bool folderViewerAvailable)
    {
        var fallbackCalls = 0;
        var result = await DownloadFolderExportCoordinator.ExportAndOpenAsync(() => Task.FromResult<string?>("public/path"),
            path => { Assert.Equal("public/path", path); return folderViewerAvailable; }, () => { fallbackCalls++; return false; });
        Assert.True(result);
        Assert.Equal(folderViewerAvailable ? 0 : 1, fallbackCalls);
    }
}
