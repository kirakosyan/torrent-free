using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadSelectionViewModelTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Notification_ShowsCompletedDownloadWithoutLimits(bool controlsExpanded, bool limitsOpen, bool sameTorrent)
    {
        var viewModel = new DownloadSelectionViewModel();
        var completed = new TorrentItem { Status = DownloadStatus.Completed };
        viewModel.SelectedTorrent = sameTorrent ? completed : new TorrentItem();
        if (controlsExpanded) viewModel.ToggleDownloadControlsCommand.Execute(null);
        if (limitsOpen) viewModel.ShowSpecificTorrentLimitsCommand.Execute(viewModel.SelectedTorrent);
        var selectionChanges = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.SelectedTorrent)) selectionChanges++;
        };

        viewModel.ShowDownloadsForNotification(completed);

        Assert.Same(completed, viewModel.SelectedTorrent);
        Assert.False(viewModel.ShowSelectedTorrentDetails);
        Assert.False(viewModel.CanShowSelectedTorrentDetails);
        Assert.Equal(controlsExpanded, viewModel.ShowDownloadControls);
        Assert.Equal(sameTorrent ? 0 : 1, selectionChanges);
        Assert.True(viewModel.ToggleSelectedTorrentDetailsCommand.CanExecute(null));
        viewModel.ShowSpecificTorrentLimitsCommand.Execute(completed);
        Assert.True(viewModel.CanShowSelectedTorrentDetails);
    }

    [Fact]
    public void RepeatedNotificationForSelectedDownload_DoesNotRaiseUnchangedProperties()
    {
        var viewModel = new DownloadSelectionViewModel();
        var torrent = new TorrentItem();
        viewModel.ShowDownloadsForNotification(torrent);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.ShowDownloadsForNotification(torrent);

        Assert.Empty(changes);
    }

    [Fact]
    public void MissingNotificationTarget_DoesNotChangeSelectionOrLimits()
    {
        var viewModel = new DownloadSelectionViewModel();
        var torrent = new TorrentItem();
        viewModel.ShowSpecificTorrentLimitsCommand.Execute(torrent);

        viewModel.ShowDownloadsForNotification(null);

        Assert.Same(torrent, viewModel.SelectedTorrent);
        Assert.True(viewModel.CanShowSelectedTorrentDetails);
    }

    [Fact]
    public void SelectionChanges_UpdateCommandAvailabilityAndNotifyDerivedViewModel()
    {
        var viewModel = new TrackingSelectionViewModel();
        Assert.False(viewModel.ToggleSelectedTorrentDetailsCommand.CanExecute(null));
        viewModel.ShowDownloadsForNotification(new TorrentItem());
        Assert.True(viewModel.ToggleSelectedTorrentDetailsCommand.CanExecute(null));
        Assert.Equal(1, viewModel.SelectionChanges);

        viewModel.SelectedTorrent = null;

        Assert.False(viewModel.ToggleSelectedTorrentDetailsCommand.CanExecute(null));
        Assert.False(viewModel.CanShowSelectedTorrentDetails);
        Assert.Equal(2, viewModel.SelectionChanges);
    }

    private sealed class TrackingSelectionViewModel : DownloadSelectionViewModel
    {
        public int SelectionChanges { get; private set; }
        protected override void OnTorrentSelectionChanged() => SelectionChanges++;
    }
}
