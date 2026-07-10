using System.Collections;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using MonoTorrent;
using MonoTorrent.Trackers;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentProxyRoutingTests
{
    [Theory]
    [InlineData("ipv4://203.0.113.7:51413", "203.0.113.7")]
    [InlineData("ipv6://[2001:db8::7]:51413", "2001:db8::7")]
    public async Task ProxyFactories_PeerConnectionCreator_RoutesThroughSocks5(string peerUri, string expectedHost)
    {
        using var proxy = new Socks5SocketConnectorTests.FakeSocks5Proxy();
        var factories = TorrentService.CreateProxyFactories("127.0.0.1", proxy.Port, string.Empty, string.Empty);
        var connection = factories.CreatePeerConnection(new Uri(peerUri));
        Assert.NotNull(connection);

        try
        {
            await connection!.ConnectAsync();
        }
        finally
        {
            (connection as IDisposable)?.Dispose();
        }

        Assert.Equal(51413, proxy.RequestedPort);
        Assert.Equal(System.Net.IPAddress.Parse(expectedHost), System.Net.IPAddress.Parse(proxy.RequestedHost!));
    }

    [Fact]
    public async Task ProxyFactories_HttpTrackerCreator_UsesProxiedHttpClient()
    {
        using var proxy = new Socks5SocketConnectorTests.FakeSocks5Proxy();
        var factories = TorrentService.CreateProxyFactories("127.0.0.1", proxy.Port, string.Empty, string.Empty);
        var tracker = factories.CreateTracker(new Uri("http://203.0.113.9:80/announce"));
        Assert.NotNull(tracker);
        using var client = GetHttpClientFromTracker(tracker!);
        client.Timeout = TimeSpan.FromSeconds(2);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.GetAsync(
                "http://203.0.113.9:80/announce",
                TestContext.Current.CancellationToken));

        Assert.Equal("203.0.113.9", proxy.RequestedHost);
        Assert.Equal(80, proxy.RequestedPort);
    }

    [Fact]
    public void ProxyFactories_RejectUdpTrackers_WithoutChangingDefaultFactories()
    {
        var factories = TorrentService.CreateProxyFactories("127.0.0.1", 1080, string.Empty, string.Empty);
        var uri = new Uri("udp://tracker.example:6969/announce");

        Assert.Null(factories.CreateTracker(uri));
        Assert.NotNull(Factories.Default.CreateTracker(uri));
    }

    private static HttpClient GetHttpClientFromTracker(ITracker tracker)
    {
        var connectionsProperty = tracker.GetType().GetProperty(
            "Connections",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(connectionsProperty);
        var connections = Assert.IsAssignableFrom<IEnumerable>(connectionsProperty!.GetValue(tracker));
        var connection = connections.Cast<object>().First();
        var creatorProperty = connection.GetType().GetProperty(
            "ClientCreator",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(creatorProperty);
        var creator = Assert.IsType<Func<AddressFamily, HttpClient>>(creatorProperty!.GetValue(connection));
        return creator(AddressFamily.InterNetwork);
    }
}
