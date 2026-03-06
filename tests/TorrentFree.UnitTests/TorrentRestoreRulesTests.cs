using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentRestoreRulesTests
{
    [Fact]
    public void InitializationSequence_DoesNotReAddTorrentAlreadyInMemory()
    {
        var existing = new List<TorrentIdentity>
        {
            new("active-id", "ABC123", "magnet:?xt=urn:btih:ABC123")
        };

        var savedTorrents = new[]
        {
            new TorrentIdentity("saved-duplicate", "abc123", "magnet:?xt=urn:btih:abc123"),
            new TorrentIdentity("saved-unique", "DEF456", "magnet:?xt=urn:btih:def456")
        };

        var added = new List<TorrentIdentity>();
        var shouldPersistChanges = false;

        foreach (var savedTorrent in savedTorrents)
        {
            var decision = TorrentRestoreRules.Evaluate(
                savedTorrent,
                torrentFilePath: null,
                existing,
                static link => link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase));

            shouldPersistChanges |= decision.ShouldPersistChanges;

            if (!decision.ShouldAdd)
            {
                continue;
            }

            existing.Add(savedTorrent);
            added.Add(savedTorrent);
        }

        Assert.True(shouldPersistChanges);
        Assert.Single(added);
        Assert.Equal("saved-unique", added[0].Id);
        Assert.Equal(2, existing.Count);
    }

    [Fact]
    public void Evaluate_ClearsMissingTorrentFile_WhenMagnetFallbackIsValid()
    {
        var torrent = new TorrentIdentity("id-1", "abcdef", "magnet:?xt=urn:btih:abcdef");
        var torrentFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".torrent");

        var decision = TorrentRestoreRules.Evaluate(
            torrent,
            torrentFilePath,
            Array.Empty<TorrentIdentity>(),
            static link => link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase));

        Assert.True(decision.ShouldAdd);
        Assert.True(decision.ShouldPersistChanges);
        Assert.True(decision.ClearTorrentFileMetadata);
    }

    [Fact]
    public void Evaluate_SkipsTorrent_WhenMissingFileHasNoValidFallback()
    {
        var torrent = new TorrentIdentity("id-2", "", "not-a-magnet");
        var torrentFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".torrent");

        var decision = TorrentRestoreRules.Evaluate(
            torrent,
            torrentFilePath,
            Array.Empty<TorrentIdentity>(),
            static link => link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase));

        Assert.False(decision.ShouldAdd);
        Assert.True(decision.ShouldPersistChanges);
        Assert.False(decision.ClearTorrentFileMetadata);
    }

    [Fact]
    public void Evaluate_SkipsDuplicateTorrentAlreadyInMemory()
    {
        var existing = new TorrentIdentity("existing-id", "ABC123", "magnet:?xt=urn:btih:ABC123");
        var loaded = new TorrentIdentity("loaded-id", "abc123", "magnet:?xt=urn:btih:abc123");

        var decision = TorrentRestoreRules.Evaluate(
            loaded,
            null,
            new[] { existing },
            static _ => true);

        Assert.False(decision.ShouldAdd);
        Assert.True(decision.ShouldPersistChanges);
        Assert.False(decision.ClearTorrentFileMetadata);
    }
}