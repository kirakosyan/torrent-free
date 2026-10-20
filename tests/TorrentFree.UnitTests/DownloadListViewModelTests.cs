using System.Collections.ObjectModel;
using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadListViewModelTests
{
    [Fact]
    public void NewRows_ArePreparedBeforeTheyReachCollectionView()
    {
        var queue = Queue();
        using var model = new PreparingListViewModel(queue);
        var newTorrent = new TorrentItem { Name = "New import" };
        var published = false;
        model.DisplayTorrents.CollectionChanged += (_, e) =>
        {
            if (e.NewItems?.Contains(newTorrent) != true) return;
            Assert.NotNull(newTorrent.ShowErrorDetailsCommand);
            published = true;
        };
        queue.Add(newTorrent);
        Assert.True(published);
    }

    private sealed class PreparingListViewModel(ObservableCollection<TorrentItem> torrents) : DownloadListViewModel(torrents)
    {
        protected override void OnDownloadsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is null) return;
            foreach (TorrentItem torrent in e.NewItems)
                torrent.ShowErrorDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        }
    }

    private static ObservableCollection<TorrentItem> Queue(int count = 6) =>
        new(Enumerable.Range(0, count).Select(i => new TorrentItem { Name = $"Download {i}", Status = DownloadStatus.Paused }));

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void SearchControls_UseTotalQueueCount(int count, bool visible)
    {
        var queue = Queue(count);
        using var model = new DownloadListViewModel(queue);
        Assert.Equal(visible, model.ShowDownloadSearch);
        Assert.Equal(queue, model.DisplayTorrents);
    }

    [Fact]
    public void HiddenControls_CannotFilterAndDoNotReactivateStaleValues()
    {
        var queue = Queue(5);
        using var model = new DownloadListViewModel(queue);
        model.DownloadSearchText = "missing";
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == DownloadFilter.Active);
        Assert.Empty(model.DownloadSearchText);
        Assert.Equal(DownloadFilter.All, model.SelectedDownloadFilter!.Filter);
        Assert.Equal(queue, model.DisplayTorrents);
        queue.Add(new TorrentItem { Name = "sixth" });
        Assert.True(model.ShowDownloadSearch);
        Assert.Equal(queue, model.DisplayTorrents);
    }

    [Fact]
    public void Search_CombinesWithFilterAndNeverHidesControlsBasedOnMatchCount()
    {
        var queue = Queue();
        queue[0].Name = "Ubuntu Desktop";
        queue[0].Status = DownloadStatus.Downloading;
        queue[1].Name = "Ubuntu Server";
        using var model = new DownloadListViewModel(queue);
        model.DownloadSearchText = "  UBUNTU  ";
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == DownloadFilter.Active);
        Assert.Same(queue[0], Assert.Single(model.DisplayTorrents));
        Assert.True(model.ShowDownloadSearch);
        model.DownloadSearchText = "missing";
        Assert.True(model.NoMatchingDownloads);
        Assert.True(model.ShowDownloadSearch);
        model.ClearDownloadFiltersCommand.Execute(null);
        Assert.False(model.NoMatchingDownloads);
        Assert.Equal(queue, model.DisplayTorrents);
        Assert.Equal(DownloadStatus.Downloading, queue[0].Status);
        Assert.Equal(DownloadStatus.Paused, queue[1].Status);
    }

    [Fact]
    public void RemovingSixthTorrent_ClearsSearchAndFilterAndRevealsRemainingItems()
    {
        var queue = Queue();
        using var model = new DownloadListViewModel(queue);
        model.DownloadSearchText = "missing";
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == DownloadFilter.NeedsAttention);
        queue.RemoveAt(5);
        Assert.False(model.ShowDownloadSearch);
        Assert.False(model.NoMatchingDownloads);
        Assert.Empty(model.DownloadSearchText);
        Assert.Equal(DownloadFilter.All, model.SelectedDownloadFilter!.Filter);
        Assert.Equal(queue, model.DisplayTorrents);
    }

    [Theory]
    [InlineData(DownloadFilter.Active, DownloadStatus.Downloading)]
    [InlineData(DownloadFilter.Active, DownloadStatus.Seeding)]
    [InlineData(DownloadFilter.Completed, DownloadStatus.Completed)]
    [InlineData(DownloadFilter.Completed, DownloadStatus.Seeding)]
    [InlineData(DownloadFilter.Paused, DownloadStatus.Paused)]
    [InlineData(DownloadFilter.NeedsAttention, DownloadStatus.Failed)]
    [InlineData(DownloadFilter.NeedsAttention, DownloadStatus.WaitingForWifi)]
    public void StatusChanges_UpdateFilteredListWithoutChangingTheQueue(DownloadFilter filter, DownloadStatus status)
    {
        var queue = Queue();
        foreach (var torrent in queue) torrent.Status = DownloadStatus.Stopped;
        using var model = new DownloadListViewModel(queue);
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == filter);
        Assert.Empty(model.DisplayTorrents);
        queue[0].Status = status;
        Assert.Same(queue[0], Assert.Single(model.DisplayTorrents));
        queue[0].Status = DownloadStatus.Stopped;
        Assert.Empty(model.DisplayTorrents);
        Assert.Equal(6, queue.Count);
    }

    [Fact]
    public void CompletedFilter_IncludesPausedCompletedDownloadsAndTracksCompletionChanges()
    {
        var queue = Queue();
        using var model = new DownloadListViewModel(queue);
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == DownloadFilter.Completed);
        queue[0].Progress = 100;
        queue[1].DateCompleted = DateTime.Now;
        Assert.Equal(new[] { queue[0], queue[1] }, model.DisplayTorrents);
        queue[0].Progress = 50;
        queue[1].DateCompleted = null;
        Assert.Empty(model.DisplayTorrents);
    }

    [Fact]
    public void SearchAndSorting_ReactToNamesAndPreserveSourceOrder()
    {
        var queue = Queue();
        using var model = new DownloadListViewModel(queue) { DownloadSearchText = "linux", SortByStatus = true };
        queue[3].Name = "Linux Z";
        queue[4].Name = "Linux A";
        Assert.Equal(new[] { queue[4], queue[3] }, model.DisplayTorrents);
        queue[3].Status = DownloadStatus.Downloading;
        Assert.Equal(new[] { queue[3], queue[4] }, model.DisplayTorrents);
        queue[3].Name = "Different";
        Assert.Same(queue[4], Assert.Single(model.DisplayTorrents));
        Assert.Equal("Download 0", queue[0].Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Notification_RevealsHiddenTargetEvenWhenItWasAlreadySelected(bool sameSelection)
    {
        var queue = Queue();
        using var model = new DownloadListViewModel(queue);
        var target = queue[0];
        target.Status = DownloadStatus.Completed;
        if (sameSelection) model.SelectedTorrent = target;
        model.ToggleDownloadControlsCommand.Execute(null);
        model.ShowSpecificTorrentLimitsCommand.Execute(target);
        model.DownloadSearchText = "missing";
        model.SelectedDownloadFilter = model.DownloadFilters.Single(f => f.Filter == DownloadFilter.Active);
        model.ShowDownloadsForNotification(target);
        Assert.Contains(target, model.DisplayTorrents);
        Assert.Same(target, model.SelectedTorrent);
        Assert.False(model.ShowSelectedTorrentDetails);
        Assert.True(model.ShowDownloadControls);
        Assert.Empty(model.DownloadSearchText);
        Assert.Equal(DownloadFilter.All, model.SelectedDownloadFilter!.Filter);
    }

    [Fact]
    public void Notification_LeavesMatchingFiltersAndMissingTargetAlone()
    {
        var queue = Queue();
        using var model = new DownloadListViewModel(queue) { DownloadSearchText = "Download 0" };
        model.ShowDownloadsForNotification(null);
        Assert.Null(model.SelectedTorrent);
        model.ShowDownloadsForNotification(queue[0]);
        Assert.Equal("Download 0", model.DownloadSearchText);
    }

    [Fact]
    public void ResetAndDispose_DetachFilteredOutItems()
    {
        var queue = Queue();
        var removed = queue[0];
        var model = new DownloadListViewModel(queue) { DownloadSearchText = "missing" };
        queue.Clear();
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        removed.Name = "Changed after reset";
        Assert.Equal(0, notifications);
        queue.Add(new TorrentItem());
        model.Dispose();
        notifications = 0;
        queue[0].Name = "Changed after disposal";
        queue.Clear();
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void CardExpansion_IsTransientAndErrorHintDoesNotRepeatTechnicalDetails()
    {
        var torrent = new TorrentItem { Status = DownloadStatus.Failed, ErrorMessage = "Technical diagnostic" };
        Assert.False(torrent.ShowDownloadDetails);
        torrent.ToggleDownloadDetailsCommand.Execute(null);
        Assert.True(torrent.ShowDownloadDetails);
        Assert.Equal(LocalizationResourceManager.Instance["HideDownloadDetails"], torrent.DownloadDetailsButtonText);
        Assert.DoesNotContain(torrent.ErrorMessage, torrent.StatusHint);
        var json = JsonSerializer.Serialize(torrent);
        Assert.DoesNotContain("ShowDownloadDetails", json);
        Assert.DoesNotContain("ToggleDownloadDetailsCommand", json);
        Assert.False(JsonSerializer.Deserialize<TorrentItem>(json)!.ShowDownloadDetails);
    }
}
