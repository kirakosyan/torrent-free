using MonoTorrent.Client;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentManagerStateRulesTests
{
    [Theory]
    [InlineData(TorrentState.Downloading)]
    [InlineData(TorrentState.Paused)]
    [InlineData(TorrentState.Hashing)]
    [InlineData(TorrentState.Metadata)]
    public void RequiresFullStop_ReturnsTrue_ForActiveOrPausedManagers(TorrentState state)
    {
        Assert.True(TorrentManagerStateRules.RequiresFullStop(state));
    }

    [Fact]
    public void RequiresFullStop_ReturnsFalse_WhenManagerAlreadyStopped()
    {
        Assert.False(TorrentManagerStateRules.RequiresFullStop(TorrentState.Stopped));
    }
}
