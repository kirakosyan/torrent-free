using MonoTorrent;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public class TorrentAvailabilityTests
{
    [Fact]
    public void GetAvailabilityInfo_CombinesPiecesHeldByPartialPeers()
    {
        var peers = new[]
        {
            new ReadOnlyBitField([true, true, false, false]),
            new ReadOnlyBitField([false, false, true, true])
        };

        var result = TorrentService.GetAvailabilityInfo(peers, pieceCount: 4);

        Assert.Equal(100, result.Percent);
        Assert.Equal("100%", result.Label);
    }

    [Fact]
    public void GetAvailabilityInfo_ReportsActualCoverageInsteadOfPeerCounts()
    {
        var peers = new[]
        {
            new ReadOnlyBitField([true, false, false, false]),
            new ReadOnlyBitField([false, true, false, false])
        };

        var result = TorrentService.GetAvailabilityInfo(peers, pieceCount: 4);

        Assert.Equal(50, result.Percent);
        Assert.Equal("50%", result.Label);
    }

    [Fact]
    public void GetAvailabilityInfo_WithoutMetadataUsesUnknownLabel()
    {
        var result = TorrentService.GetAvailabilityInfo([], pieceCount: 0);

        Assert.Equal(0, result.Percent);
        Assert.Equal("—", result.Label);
    }
}
