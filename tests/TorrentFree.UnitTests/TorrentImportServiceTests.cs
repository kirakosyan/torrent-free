using MonoTorrent.BEncoding;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentImportServiceTests
{
    [Fact]
    public async Task AbandonedPreparedImport_DoesNotLeaveCacheFile()
    {
        await using var fixture = new CoreServiceFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        Assert.False(File.Exists(metadata.CachedFilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(metadata.CachedFilePath)));
        Assert.NotEmpty(metadata.CachedContent!);
    }

    [Fact]
    public async Task DuplicateImport_DoesNotReplaceExistingCachedMetadata()
    {
        await using var fixture = new CoreServiceFixture();
        var metadata = await fixture.PrepareTorrentAsync();
        await fixture.Service.AddTorrentFileAsync(metadata);
        var cachePath = metadata.CachedFilePath!;
        var original = await File.ReadAllBytesAsync(cachePath, TestContext.Current.CancellationToken);
        // The comment changes the file bytes without changing the torrent info hash.
        var altered = BEncodedValue.Decode<BEncodedDictionary>(original);
        altered["comment"] = new BEncodedString("duplicate metadata");
        var duplicate = await new TorrentImportService(fixture.Directory.StoragePaths, new TorrentFileParser())
            .PrepareAsync(new TorrentPickedFile("duplicate.torrent", null, altered.Encode()), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DuplicateTorrentException>(() => fixture.Service.AddTorrentFileAsync(duplicate));
        Assert.Equal(original, await File.ReadAllBytesAsync(cachePath, TestContext.Current.CancellationToken));
    }
}
