using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentItemTests
{
    [Theory]
    [InlineData(DownloadStatus.Queued)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.WaitingForWifi)]
    public void CompletionInputsRefreshFileAvailabilityWithoutAStatusChange(DownloadStatus status)
    {
        var path = Path.GetTempFileName();
        try
        {
            var torrent = new TorrentItem { SavePath = Path.GetDirectoryName(path)!, Name = Path.GetFileName(path), Status = status };
            var notifications = 0;
            torrent.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TorrentItem.CanOpenDownloadedFile)) notifications++; };
            Assert.False(torrent.CanOpenDownloadedFile);
            torrent.Progress = 100;
            Assert.True(torrent.CanOpenDownloadedFile);
            torrent.Progress = 50;
            Assert.False(torrent.CanOpenDownloadedFile);
            torrent.DateCompleted = DateTime.UtcNow;
            Assert.True(torrent.CanOpenDownloadedFile);
            Assert.Equal(3, notifications);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void QueuedDownloadCompletion_RefreshesSeedingLabelAndHint()
    {
        var torrent = new TorrentItem { Status = DownloadStatus.Queued, Progress = 99.9 };
        var resources = LocalizationResourceManager.Instance;
        var changes = new List<string?>();
        torrent.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal(resources["StatusQueued"], torrent.StatusText);
        Assert.Equal(resources["HintQueued"], torrent.StatusHint);

        torrent.Progress = 100;
        AssertSeedingQueue();

        torrent.Progress = 50;
        Assert.Equal(resources["StatusQueued"], torrent.StatusText);
        Assert.Equal(resources["HintQueued"], torrent.StatusHint);

        changes.Clear();
        torrent.DateCompleted = DateTime.UtcNow;
        AssertSeedingQueue();

        torrent.DateCompleted = null;
        Assert.Equal(resources["StatusQueued"], torrent.StatusText);
        Assert.Equal(resources["HintQueued"], torrent.StatusHint);

        void AssertSeedingQueue()
        {
            Assert.Equal(resources["StatusQueuedForSeeding"], torrent.StatusText);
            Assert.Equal(resources["HintQueuedForSeeding"], torrent.StatusHint);
            Assert.Contains(nameof(TorrentItem.StatusText), changes);
            Assert.Contains(nameof(TorrentItem.StatusHint), changes);
            Assert.Equal(DownloadStatus.Queued, torrent.Status);
        }
    }

    [Fact]
    public void CompletedTorrent_QueuedForSeeding_RequiresFileToEnableFolderButton()
    {
        var path = Path.GetTempFileName();
        try
        {
            var torrent = new TorrentItem
            {
                SavePath = Path.GetDirectoryName(path)!, Name = Path.GetFileName(path),
                Status = DownloadStatus.Seeding, Progress = 100
            };
            Assert.True(torrent.CanOpenDownloadedFile);
            torrent.Status = DownloadStatus.Queued;
            Assert.True(torrent.CanOpenDownloadedFile);
            torrent.Name = Guid.NewGuid().ToString();
            Assert.False(torrent.CanOpenDownloadedFile);
        }
        finally { File.Delete(path); }
    }
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
