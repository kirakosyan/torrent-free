using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadFolderExportCoordinatorTests
{
    [Fact]
    public async Task MissingDownload_ReportsFolderUnavailableBeforeRequestingPermissionOrCopying()
    {
        var result = await DownloadFolderExportCoordinator.TryExportAndOpenAsync(() => false,
            () => throw new InvalidOperationException("Must not request permission"),
            () => throw new InvalidOperationException("Must not copy"), _ => true, () => true);
        Assert.Equal(DownloadFolderExportResult.DownloadUnavailable, result);
        Assert.Equal("ErrorOpenFolder", DownloadFolderExportCoordinator.GetErrorResourceKey(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionFailure_DoesNotAttemptExport(bool throws)
    {
        var result = await DownloadFolderExportCoordinator.TryExportAndOpenAsync(() => true,
            () => throws ? Task.FromException<bool>(new UnauthorizedAccessException()) : Task.FromResult(false),
            () => throw new InvalidOperationException("Must not copy"), _ => true, () => true);
        Assert.Equal(DownloadFolderExportResult.PermissionDenied, result);
        Assert.Equal("ErrorOpenFolder", DownloadFolderExportCoordinator.GetErrorResourceKey(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportFailure_ReportsCopyError(bool throws)
    {
        var result = await DownloadFolderExportCoordinator.TryExportAndOpenAsync(() => true, () => Task.FromResult(true),
            () => throws ? Task.FromException<string?>(new IOException("Storage full")) : Task.FromResult<string?>(null),
            _ => throw new InvalidOperationException("Must not open"), () => throw new InvalidOperationException("Must not open"));
        Assert.Equal(DownloadFolderExportResult.ExportFailed, result);
        Assert.Equal("ErrorExportDownload", DownloadFolderExportCoordinator.GetErrorResourceKey(result));
    }

    [Fact]
    public async Task CompletedExport_HasNoErrorMessage()
    {
        var result = await DownloadFolderExportCoordinator.TryExportAndOpenAsync(() => true, () => Task.FromResult(true),
            () => Task.FromResult<string?>("public/path"), _ => true, () => true);
        Assert.Equal(DownloadFolderExportResult.Completed, result);
        Assert.Null(DownloadFolderExportCoordinator.GetErrorResourceKey(result));
    }

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
