using System.Net;
using System.Net.Sockets;
using System.Text;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class Socks5SocketConnectorTests
{
    [Fact]
    public async Task ConnectAsync_NoAuth_CompletesHandshakeAndTunnelsData()
    {
        using var proxy = new FakeSocks5Proxy();
        var connector = new Socks5SocketConnector("127.0.0.1", proxy.Port, username: null, password: null);

        var socket = await connector.ConnectAsync(new Uri("ipv4://203.0.113.7:51413"), CancellationToken.None);

        try
        {
            // Greeting offered exactly one method: no-auth.
            Assert.Equal([0x05, 0x01, 0x00], proxy.Greeting);
            Assert.Null(proxy.Username);

            // CONNECT request targeted the requested peer.
            Assert.Equal(FakeSocks5Proxy.AddressTypeIPv4, proxy.RequestedAddressType);
            Assert.Equal("203.0.113.7", proxy.RequestedHost);
            Assert.Equal(51413, proxy.RequestedPort);

            // The returned socket is a live tunnel: the fake proxy echoes payload bytes.
            await SendAsync(socket, "ping");
            Assert.Equal("ping", await ReceiveAsync(socket, 4));
        }
        finally
        {
            socket.Dispose();
        }
    }

    [Fact]
    public async Task ConnectAsync_WithCredentials_PerformsUsernamePasswordAuth()
    {
        using var proxy = new FakeSocks5Proxy { RequireAuth = true };
        var connector = new Socks5SocketConnector("127.0.0.1", proxy.Port, "alice", "s3cret");

        var socket = await connector.ConnectAsync(new Uri("ipv4://198.51.100.4:6881"), CancellationToken.None);

        try
        {
            // Greeting must advertise both no-auth and username/password.
            Assert.Equal([0x05, 0x02, 0x00, 0x02], proxy.Greeting);
            Assert.Equal("alice", proxy.Username);
            Assert.Equal("s3cret", proxy.Password);
            Assert.Equal("198.51.100.4", proxy.RequestedHost);
            Assert.Equal(6881, proxy.RequestedPort);
        }
        finally
        {
            socket.Dispose();
        }
    }

    [Fact]
    public async Task ConnectAsync_IPv6Peer_SendsIPv6AddressType()
    {
        using var proxy = new FakeSocks5Proxy();
        var connector = new Socks5SocketConnector("127.0.0.1", proxy.Port, null, null);

        var socket = await connector.ConnectAsync(new Uri("ipv6://[2001:db8::abcd]:6881"), CancellationToken.None);

        try
        {
            Assert.Equal(FakeSocks5Proxy.AddressTypeIPv6, proxy.RequestedAddressType);
            Assert.Equal(IPAddress.Parse("2001:db8::abcd"), IPAddress.Parse(proxy.RequestedHost!));
            Assert.Equal(6881, proxy.RequestedPort);
        }
        finally
        {
            socket.Dispose();
        }
    }

    [Fact]
    public async Task ConnectAsync_WhenProxyRejectsAuthMethods_Throws()
    {
        using var proxy = new FakeSocks5Proxy { RejectMethods = true };
        var connector = new Socks5SocketConnector("127.0.0.1", proxy.Port, null, null);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await connector.ConnectAsync(new Uri("ipv4://203.0.113.7:6881"), CancellationToken.None));
    }

    [Fact]
    public async Task ConnectAsync_WhenConnectReplyIsError_Throws()
    {
        using var proxy = new FakeSocks5Proxy { ConnectReplyCode = 0x05 }; // connection refused
        var connector = new Socks5SocketConnector("127.0.0.1", proxy.Port, null, null);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await connector.ConnectAsync(new Uri("ipv4://203.0.113.7:6881"), CancellationToken.None));
    }

    private static async Task SendAsync(Socket socket, string text)
    {
        var buffer = Encoding.ASCII.GetBytes(text);
        var offset = 0;
        while (offset < buffer.Length)
        {
            offset += await socket.SendAsync(buffer.AsMemory(offset), SocketFlags.None);
        }
    }

    private static async Task<string> ReceiveAsync(Socket socket, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.ASCII.GetString(buffer, 0, offset);
    }

    /// <summary>
    /// Minimal in-process SOCKS5 server that performs the handshake against a single
    /// client, records what it observed, and then echoes tunnelled payload bytes.
    /// </summary>
    internal sealed class FakeSocks5Proxy : IDisposable
    {
        public const byte AddressTypeIPv4 = 0x01;
        public const byte AddressTypeDomain = 0x03;
        public const byte AddressTypeIPv6 = 0x04;

        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        public bool RequireAuth { get; init; }
        public bool RejectMethods { get; init; }
        public byte ConnectReplyCode { get; init; } = 0x00;

        public byte[]? Greeting { get; private set; }
        public string? Username { get; private set; }
        public string? Password { get; private set; }
        public byte RequestedAddressType { get; private set; }
        public string? RequestedHost { get; private set; }
        public int RequestedPort { get; private set; }

        public FakeSocks5Proxy()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serverTask = Task.Run(RunAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();

                var head = await ReadExactAsync(stream, 2);
                var methods = await ReadExactAsync(stream, head[1]);
                Greeting = [head[0], head[1], .. methods];

                if (RejectMethods)
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0xFF });
                    return;
                }

                if (RequireAuth)
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0x02 });
                    var authHead = await ReadExactAsync(stream, 2); // ver, ulen
                    var user = await ReadExactAsync(stream, authHead[1]);
                    var plen = await ReadExactAsync(stream, 1);
                    var pass = await ReadExactAsync(stream, plen[0]);
                    Username = Encoding.UTF8.GetString(user);
                    Password = Encoding.UTF8.GetString(pass);
                    await stream.WriteAsync(new byte[] { 0x01, 0x00 });
                }
                else
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0x00 });
                }

                var request = await ReadExactAsync(stream, 4); // ver, cmd, rsv, atyp
                RequestedAddressType = request[3];
                RequestedHost = request[3] switch
                {
                    AddressTypeIPv4 => new IPAddress(await ReadExactAsync(stream, 4)).ToString(),
                    AddressTypeIPv6 => new IPAddress(await ReadExactAsync(stream, 16)).ToString(),
                    AddressTypeDomain => Encoding.UTF8.GetString(await ReadExactAsync(stream, (await ReadExactAsync(stream, 1))[0])),
                    _ => null
                };
                var portBytes = await ReadExactAsync(stream, 2);
                RequestedPort = (portBytes[0] << 8) | portBytes[1];

                // Reply: VER, REP, RSV, ATYP=IPv4, BND.ADDR(4)=0, BND.PORT(2)=0
                await stream.WriteAsync(new byte[] { 0x05, ConnectReplyCode, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
                if (ConnectReplyCode != 0x00)
                {
                    return;
                }

                // Tunnel established: echo whatever the client sends.
                var buffer = new byte[256];
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            catch
            {
                // Connection torn down by the test; nothing to do.
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset));
                if (read <= 0)
                {
                    throw new IOException("Fake proxy: client closed early.");
                }

                offset += read;
            }

            return buffer;
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // ignore
            }

            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore
            }
        }
    }
}
