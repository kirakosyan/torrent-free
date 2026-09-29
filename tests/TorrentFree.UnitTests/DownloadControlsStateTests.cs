using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadControlsStateTests
{
    [Fact]
    public void StartsCollapsed_AndDoesNotOpenLimitsWithoutSelection()
    {
        var state = new DownloadControlsState();

        Assert.False(state.ShowDownloadControls);
        Assert.False(state.CanShowSelectedTorrentDetails);
        Assert.False(state.CanToggleSelectedTorrentDetails);
        state.ToggleSelectedTorrentDetails();
        Assert.False(state.ShowSelectedTorrentDetails);
    }

    [Fact]
    public void CollapsingControlsClosesLimits()
    {
        var state = new DownloadControlsState();
        state.SetSelection(true);
        Assert.True(state.CanToggleSelectedTorrentDetails);
        state.ToggleDownloadControls();
        state.ToggleSelectedTorrentDetails();

        Assert.True(state.ShowDownloadControls);
        Assert.True(state.CanShowSelectedTorrentDetails);
        state.ToggleDownloadControls();
        Assert.False(state.ShowDownloadControls);
        Assert.False(state.CanShowSelectedTorrentDetails);
    }

    [Fact]
    public void NotificationRevealOpensLimitsWithoutExpandingControls()
    {
        var state = new DownloadControlsState();
        Assert.False(state.RevealSelectedTorrentDetails());
        state.SetSelection(true);

        Assert.True(state.RevealSelectedTorrentDetails());
        Assert.False(state.ShowDownloadControls);
        Assert.True(state.CanShowSelectedTorrentDetails);
    }

    [Fact]
    public void SelectionCanChangeWhileLimitsAreOpen_ButClearingItClosesThem()
    {
        var state = new DownloadControlsState();
        state.SetSelection(true);
        state.ToggleSelectedTorrentDetails();
        state.SetSelection(true);
        Assert.True(state.CanShowSelectedTorrentDetails);

        state.SetSelection(false);
        Assert.False(state.CanToggleSelectedTorrentDetails);
        Assert.False(state.CanShowSelectedTorrentDetails);
    }
}
