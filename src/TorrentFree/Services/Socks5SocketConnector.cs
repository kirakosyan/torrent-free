using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent.Connections;
using ReusableTasks;

namespace TorrentFree.Services;

/// <summary>
/// Routes outbound BitTorrent peer (TCP) connections through a SOCKS5 proxy.
/// MonoTorrent 3.x exposes no proxy setting, but it lets us replace the
/// <see cref="ISocketConnector"/> used to establish peer sockets via the engine
/// <c>Factories</c>. We dial the proxy, run the SOCKS5 CONNECT handshake
/// (RFC 1928, with optional username/password auth per RFC 1929), and hand the
/// tunnelled socket back so the rest of the wire protocol is unchanged.
///
/// Only TCP peer connections are proxied. UDP traffic (DHT and UDP trackers) is
/// not tunnelled — SOCKS5 UDP ASSOCIATE is not implemented — so for full privacy
/// the engine should also rely on TCP/HTTP trackers.
/// </summary>
internal sealed class Socks5SocketConnector : ISocketConnector
{
    private const byte SocksVersion = 0x05;
    private const byte AuthVersion = 0x01;
    private const byte MethodNoAuth = 0x00;
    private const byte MethodUsernamePassword = 0x02;
    private const byte MethodNoneAcceptable = 0xFF;
    private const byte CommandConnect = 0x01;
    private const byte AddressTypeIPv4 = 0x01;
    private const byte AddressTypeDomain = 0x03;
    private const byte AddressTypeIPv6 = 0x04;
    private const byte ReplySucceeded = 0x00;

    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;

    public Socks5SocketConnector(string host, int port, string? username, string? password)
    {
        _host = host;
        _port = port;
        _username = username ?? string.Empty;
        _password = password ?? string.Empty;
    }

    public async ReusableTask<Socket> ConnectAsync(Uri uri, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // A dual-mode socket lets the proxy be reached over IPv4 or IPv6, by host or IP.
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(_host, _port, token).ConfigureAwait(false);
            await NegotiateAuthenticationAsync(socket, token).ConfigureAwait(false);
            await SendConnectRequestAsync(socket, uri, token).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async ValueTask NegotiateAuthenticationAsync(Socket socket, CancellationToken token)
    {
        var hasCredentials = _username.Length > 0;
        byte[] greeting = hasCredentials
            ? [SocksVersion, 0x02, MethodNoAuth, MethodUsernamePassword]
            : [SocksVersion, 0x01, MethodNoAuth];

        await SendAllAsync(socket, greeting, token).ConfigureAwait(false);

        var response = new byte[2];
        await ReceiveAllAsync(socket, response, token).ConfigureAwait(false);

        if (response[0] != SocksVersion)
        {
            throw new IOException($"Unexpected SOCKS version in greeting reply: {response[0]}.");
        }

        switch (response[1])
        {
            case MethodNoAuth:
                return;
            case MethodUsernamePassword when hasCredentials:
                await AuthenticateAsync(socket, token).ConfigureAwait(false);
                return;
            case MethodNoneAcceptable:
                throw new IOException("SOCKS5 proxy rejected all offered authentication methods.");
            default:
                throw new IOException($"SOCKS5 proxy selected an unsupported authentication method: {response[1]}.");
        }
    }

    private async ValueTask AuthenticateAsync(Socket socket, CancellationToken token)
    {
        var user = Encoding.UTF8.GetBytes(_username);
        var pass = Encoding.UTF8.GetBytes(_password);
        if (user.Length > 255 || pass.Length > 255)
        {
            throw new IOException("SOCKS5 username or password exceeds the 255-byte limit.");
        }

        var request = new byte[3 + user.Length + pass.Length];
        var offset = 0;
        request[offset++] = AuthVersion;
        request[offset++] = (byte)user.Length;
        user.CopyTo(request, offset);
        offset += user.Length;
        request[offset++] = (byte)pass.Length;
        pass.CopyTo(request, offset);

        await SendAllAsync(socket, request, token).ConfigureAwait(false);

        var response = new byte[2];
        await ReceiveAllAsync(socket, response, token).ConfigureAwait(false);
        if (response[1] != ReplySucceeded)
        {
            throw new IOException("SOCKS5 username/password authentication was rejected.");
        }
    }

    private static async ValueTask SendConnectRequestAsync(Socket socket, Uri uri, CancellationToken token)
    {
        var port = uri.Port;
        if (port is < 0 or > 65535)
        {
            throw new IOException($"Peer URI has an invalid port: {uri}.");
        }

        // DnsSafeHost is bracket-free for IPv6 literals (Uri.Host keeps the brackets),
        // which keeps both the IP-literal and domain-name paths unambiguous.
        var targetHost = uri.DnsSafeHost;

        byte[] request;
        if (IPAddress.TryParse(targetHost, out var address))
        {
            var addressBytes = address.GetAddressBytes();
            var addressType = address.AddressFamily == AddressFamily.InterNetworkV6
                ? AddressTypeIPv6
                : AddressTypeIPv4;

            request = new byte[6 + addressBytes.Length];
            var offset = WriteRequestHeader(request, addressType);
            addressBytes.CopyTo(request, offset);
            offset += addressBytes.Length;
            WritePort(request, offset, port);
        }
        else
        {
            var host = Encoding.UTF8.GetBytes(targetHost);
            if (host.Length > 255)
            {
                throw new IOException("Peer host name exceeds the 255-byte limit.");
            }

            request = new byte[7 + host.Length];
            var offset = WriteRequestHeader(request, AddressTypeDomain);
            request[offset++] = (byte)host.Length;
            host.CopyTo(request, offset);
            offset += host.Length;
            WritePort(request, offset, port);
        }

        await SendAllAsync(socket, request, token).ConfigureAwait(false);
        await ReadConnectReplyAsync(socket, token).ConfigureAwait(false);
    }

    private static int WriteRequestHeader(byte[] request, byte addressType)
    {
        request[0] = SocksVersion;
        request[1] = CommandConnect;
        request[2] = 0x00; // reserved
        request[3] = addressType;
        return 4;
    }

    private static void WritePort(byte[] buffer, int offset, int port)
    {
        buffer[offset] = (byte)(port >> 8);
        buffer[offset + 1] = (byte)(port & 0xFF);
    }

    private static async ValueTask ReadConnectReplyAsync(Socket socket, CancellationToken token)
    {
        // Reply layout: VER, REP, RSV, ATYP, BND.ADDR (variable), BND.PORT (2 bytes).
        var header = new byte[4];
        await ReceiveAllAsync(socket, header, token).ConfigureAwait(false);

        if (header[0] != SocksVersion)
        {
            throw new IOException($"Unexpected SOCKS version in CONNECT reply: {header[0]}.");
        }

        if (header[1] != ReplySucceeded)
        {
            throw new IOException($"SOCKS5 proxy refused the connection (reply code {header[1]}).");
        }

        var bytesToDiscard = header[3] switch
        {
            AddressTypeIPv4 => 4 + 2,
            AddressTypeIPv6 => 16 + 2,
            AddressTypeDomain => await ReadDomainBoundLengthAsync(socket, token).ConfigureAwait(false) + 2,
            _ => throw new IOException($"SOCKS5 CONNECT reply used an unknown address type: {header[3]}.")
        };

        if (bytesToDiscard > 0)
        {
            var discard = new byte[bytesToDiscard];
            await ReceiveAllAsync(socket, discard, token).ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> ReadDomainBoundLengthAsync(Socket socket, CancellationToken token)
    {
        var lengthBuffer = new byte[1];
        await ReceiveAllAsync(socket, lengthBuffer, token).ConfigureAwait(false);
        return lengthBuffer[0];
    }

    private static async ValueTask SendAllAsync(Socket socket, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var sent = await socket.SendAsync(buffer.AsMemory(offset), SocketFlags.None, token).ConfigureAwait(false);
            if (sent <= 0)
            {
                throw new IOException("SOCKS5 proxy closed the connection while sending the handshake.");
            }

            offset += sent;
        }
    }

    private static async ValueTask ReceiveAllAsync(Socket socket, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None, token).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new IOException("SOCKS5 proxy closed the connection during the handshake.");
            }

            offset += read;
        }
    }
}
