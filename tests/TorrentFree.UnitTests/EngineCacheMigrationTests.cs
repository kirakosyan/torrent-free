using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class EngineCacheMigrationTests
{
    [Fact]
    public void Migration_PreservesCachesAndIgnoresUnrelatedFiles()
    {
        using var directory = new CoreTestDirectory();
        var source = Path.Combine(directory.Path, "downloads");
        var destination = Path.Combine(directory.Path, "EngineCache");
        var entries = new[] { $"fastresume/{new string('a', 40)}.fresume", $"metadata/{new string('b', 40)}.torrent", $"metadata/{new string('c', 64)}.v2hashes", "dht_nodes.cache" };
        foreach (var entry in entries) Write(Path.Combine(source, entry), entry);
        Write(Path.Combine(source, "metadata", "keep.txt"), "unrelated");

        EngineCacheMigration.Migrate(source, destination);

        foreach (var entry in entries)
        {
            Assert.Equal(entry, File.ReadAllText(Path.Combine(destination, entry)));
            Assert.False(File.Exists(Path.Combine(source, entry)));
        }
        Assert.True(File.Exists(Path.Combine(source, "metadata", "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(source, "fastresume")));
        Assert.True(File.Exists(Path.Combine(destination, EngineCacheMigration.CompletionMarker)));
        // Starting a torrent can remove its fast-resume file. Never restore a stale legacy copy afterward.
        File.Delete(Path.Combine(destination, entries[0]));
        Write(Path.Combine(source, entries[0]), "stale");
        EngineCacheMigration.Migrate(source, destination);
        Assert.False(File.Exists(Path.Combine(destination, entries[0])));
    }

    [Fact]
    public void Migration_ResumesPartialMoveWithoutOverwritingNewerCache()
    {
        using var directory = new CoreTestDirectory();
        var source = Path.Combine(directory.Path, "downloads");
        var destination = Path.Combine(directory.Path, "EngineCache");
        var existing = Path.Combine("metadata", new string('a', 40) + ".torrent");
        Write(Path.Combine(source, existing), "old");
        Write(Path.Combine(destination, existing), "new");
        Write(Path.Combine(source, "dht_nodes.cache"), "nodes");

        EngineCacheMigration.Migrate(source, destination);

        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, existing)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(source, existing)));
        Assert.Equal("nodes", File.ReadAllText(Path.Combine(destination, "dht_nodes.cache")));
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
