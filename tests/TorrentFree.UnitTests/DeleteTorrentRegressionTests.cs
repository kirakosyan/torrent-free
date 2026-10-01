using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DeleteTorrentRegressionTests
{
    [Fact]
    public void DeleteDialog_SelectsBothFileDeletionOptionsByDefault()
    {
        var dialog = new DeleteTorrentDialogViewModel("payload.bin");

        Assert.Equal("payload.bin", dialog.TorrentName);
        Assert.True(dialog.DeleteTorrentFile);
        Assert.True(dialog.DeleteDownloadedFiles);
    }

    [Fact]
    public void DeleteDialog_NewDialogResetsOptionsAfterUserUncheckedThem()
    {
        var previousDialog = new DeleteTorrentDialogViewModel("first.bin")
        {
            DeleteTorrentFile = false,
            DeleteDownloadedFiles = false
        };
        Assert.True(previousDialog.CanDelete);

        var nextDialog = new DeleteTorrentDialogViewModel("next.bin");

        Assert.True(nextDialog.DeleteTorrentFile);
        Assert.True(nextDialog.DeleteDownloadedFiles);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task RemoveTorrentAsync_DeletesOnlyFilesSelectedInDialog(
        bool deleteTorrentFile, bool deleteDownloadedFiles, bool startTorrent)
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var metadata = await fixture.PrepareTorrentAsync();
        var sourcePath = Path.Combine(fixture.Directory.Path, "payload.bin.torrent");
        var payloadPath = Path.Combine(fixture.Storage.GetDefaultDownloadPath(), "payload.bin");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        var payloadBytes = await File.ReadAllBytesAsync(payloadPath, TestContext.Current.CancellationToken);
        var torrent = (await fixture.Service.AddTorrentFileAsync(metadata with { SourceFilePath = sourcePath }))!;
        var unrelatedPath = Path.Combine(torrent.SavePath, "keep.txt");
        await File.WriteAllTextAsync(unrelatedPath, "unrelated", TestContext.Current.CancellationToken);

        if (startTorrent)
        {
            await fixture.Service.StartTorrentAsync(torrent);
            await CoreServiceFixture.WaitUntilAsync(() => torrent.Status == DownloadStatus.Seeding);
            Assert.Single(fixture.Engine.Torrents);
        }

        var dialog = new DeleteTorrentDialogViewModel(torrent.Name)
        {
            DeleteTorrentFile = deleteTorrentFile,
            DeleteDownloadedFiles = deleteDownloadedFiles
        };
        var result = new DeleteTorrentDialogResult(dialog.DeleteTorrentFile, dialog.DeleteDownloadedFiles);
        var removal = await fixture.Service.RemoveTorrentAsync(torrent, result.DeleteTorrentFile, result.DeleteDownloadedFiles);
        Assert.True(removal.Removed);
        Assert.False(removal.DownloadedFilesLeftInPlace);
        Assert.False(removal.TorrentFileLeftInPlace);

        Assert.Empty(fixture.Service.Torrents);
        Assert.Empty(fixture.Engine.Torrents);
        Assert.Empty(await fixture.Storage.LoadTorrentsAsync());
        Assert.Equal(!deleteTorrentFile, File.Exists(sourcePath));
        Assert.Equal(!deleteDownloadedFiles, File.Exists(payloadPath));
        if (!deleteTorrentFile)
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
        if (!deleteDownloadedFiles)
            Assert.Equal(payloadBytes, await File.ReadAllBytesAsync(payloadPath, TestContext.Current.CancellationToken));
        Assert.Equal("unrelated", await File.ReadAllTextAsync(unrelatedPath, TestContext.Current.CancellationToken));
    }
}
