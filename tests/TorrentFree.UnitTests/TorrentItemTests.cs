using System.Text.Json;
using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentItemTests
{
    [Fact]
    public void GeneratedStatusChange_RefreshesCommandsAndLocalizedProperties()
    {
        var torrent = new TorrentItem();
        var changes = new List<string?>();
        torrent.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        torrent.Status = DownloadStatus.Seeding;
        Assert.False(torrent.CanStart);
        Assert.True(torrent.CanPause);
        Assert.True(torrent.CanStop);
        Assert.Contains(nameof(TorrentItem.CanPause), changes);
        Assert.Contains(nameof(TorrentItem.CanStop), changes);
        Assert.Contains(nameof(TorrentItem.StatusText), changes);
        Assert.False(string.IsNullOrWhiteSpace(torrent.StatusText));
    }

    [Fact]
    public void PersistedModel_RoundTripsSessionDataWithoutUiCommandsOrHistory()
    {
        var torrent = new TorrentItem { SeededSeconds = 123.5, CachedTorrentFilePath = "retained.torrent" };
        torrent.AddSpeedSample(1024, 2048);
        var json = JsonSerializer.Serialize(torrent);
        var restored = JsonSerializer.Deserialize<TorrentItem>(json)!;
        Assert.Equal(123.5, restored.SeededSeconds);
        Assert.Equal("retained.torrent", restored.CachedTorrentFilePath);
        Assert.DoesNotContain("Command", json);
        Assert.Empty(restored.DownloadSpeedHistory);
        Assert.Single(torrent.DownloadSpeedHistory);
        Assert.Equal(1, torrent.DownloadSpeedHistory[0]);
    }
}
