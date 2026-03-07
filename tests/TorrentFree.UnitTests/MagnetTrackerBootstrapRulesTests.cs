using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class MagnetTrackerBootstrapRulesTests
{
    [Fact]
    public void ShouldAddPublicTrackers_ReturnsTrue_ForTrackerlessMagnet()
    {
        Assert.True(MagnetTrackerBootstrapRules.ShouldAddPublicTrackers(
            "magnet:?xt=urn:btih:50529E48B9B85EB7B881960805C482BF0C598815"));
    }

    [Fact]
    public void ShouldAddPublicTrackers_ReturnsFalse_WhenMagnetAlreadyContainsTrackers()
    {
        Assert.False(MagnetTrackerBootstrapRules.ShouldAddPublicTrackers(
            "magnet:?xt=urn:btih:50529E48B9B85EB7B881960805C482BF0C598815&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce"));
    }
}
