using System.Security.Cryptography;
using System.Text;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class TorrentFileParserTests
{
    [Fact]
    public void Parse_ComputesV1InfoHash_FromRawInfoBytes_RegardlessOfKeyOrder()
    {
        // The info dictionary keys are intentionally NOT in canonical (sorted) order:
        // "name" precedes "length". A decode-then-canonical-re-encode would reorder them
        // and hash different bytes, so this asserts we hash the original bytes verbatim.
        var infoBytes = Encoding.ASCII.GetBytes("d4:name3:abc6:lengthi123ee");
        var fileBytes = Concat(Encoding.ASCII.GetBytes("d4:info"), infoBytes, Encoding.ASCII.GetBytes("e"));

        var expected = Convert.ToHexString(SHA1.HashData(infoBytes)).ToLowerInvariant();

        var metadata = new TorrentFileParser().Parse(fileBytes);

        Assert.Equal("abc", metadata.Name);
        Assert.Equal(expected, metadata.InfoHashHex);
        Assert.Equal(40, metadata.InfoHashHex!.Length);
    }

    [Fact]
    public void Parse_ComputesV2InfoHash_AsSha256_ForV2OnlyTorrent()
    {
        // BEP 52 v2-only: "meta version" = 2 and no v1 "pieces" key.
        var infoBytes = Encoding.ASCII.GetBytes("d12:meta versioni2e4:name3:abce");
        var fileBytes = Concat(Encoding.ASCII.GetBytes("d4:info"), infoBytes, Encoding.ASCII.GetBytes("e"));

        var expected = Convert.ToHexString(SHA256.HashData(infoBytes)).ToLowerInvariant();

        var metadata = new TorrentFileParser().Parse(fileBytes);

        Assert.Equal(expected, metadata.InfoHashHex);
        Assert.Equal(64, metadata.InfoHashHex!.Length);
    }

    [Fact]
    public void Decode_ThrowsFormatException_OnDeeplyNestedInput_InsteadOfStackOverflow()
    {
        // A hostile file of thousands of nested lists ("lll...eee") would recurse the decoder
        // into a StackOverflowException without the depth cap.
        const int depth = 5000;
        var bytes = new byte[depth * 2];
        Array.Fill(bytes, (byte)'l', 0, depth);
        Array.Fill(bytes, (byte)'e', depth, depth);

        Assert.Throws<FormatException>(() => Bencode.Decode(bytes));
    }

    [Fact]
    public void Decode_ParsesModeratelyNestedInput_WithinDepthLimit()
    {
        const int depth = 50;
        var bytes = new byte[depth * 2];
        Array.Fill(bytes, (byte)'l', 0, depth);
        Array.Fill(bytes, (byte)'e', depth, depth);

        var element = Bencode.Decode(bytes);

        Assert.IsType<BList>(element);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
