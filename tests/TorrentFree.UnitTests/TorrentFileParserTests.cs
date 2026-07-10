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

    [Fact]
    public void Decode_ThrowsFormatException_WhenSingleContainerExceedsEntryLimit()
    {
        var bytes = CreateIntegerList(TorrentFileLimits.MaxBencodeEntriesPerContainer + 1);

        var exception = Assert.Throws<FormatException>(() => Bencode.Decode(bytes));

        Assert.Contains("too many entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_ThrowsFormatException_WhenDocumentExceedsNodeLimit()
    {
        // The nested lists keep every individual container below its own entry limit while
        // the aggregate document crosses the node ceiling.
        var remainingNodes = TorrentFileLimits.MaxBencodeNodes - 4;
        var firstGroup = remainingNodes / 3;
        var secondGroup = remainingNodes / 3;
        var thirdGroup = remainingNodes - firstGroup - secondGroup + 1;
        var bytes = CreateGroupedIntegerLists(firstGroup, secondGroup, thirdGroup);

        var exception = Assert.Throws<FormatException>(() => Bencode.Decode(bytes));

        Assert.Contains("too many elements", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_ThrowsFormatException_WhenDocumentExceedsStringLimit()
    {
        var firstGroup = TorrentFileLimits.MaxBencodeStrings / 2;
        var secondGroup = TorrentFileLimits.MaxBencodeStrings - firstGroup + 1;
        var bytes = CreateGroupedEmptyStringLists(firstGroup, secondGroup);

        var exception = Assert.Throws<FormatException>(() => Bencode.Decode(bytes));

        Assert.Contains("too many strings", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AllowsMaximumTrackerCount()
    {
        var bytes = CreateTorrentWithTrackerCount(TorrentFileLimits.MaxTrackerCount);

        var metadata = new TorrentFileParser().Parse(bytes);

        Assert.Single(metadata.Trackers);
        Assert.Equal("udp://tracker", metadata.Trackers[0]);
    }

    [Fact]
    public void Parse_RejectsTrackerCountAboveLimit()
    {
        var bytes = CreateTorrentWithTrackerCount(TorrentFileLimits.MaxTrackerCount + 1);

        var exception = Assert.Throws<FormatException>(() => new TorrentFileParser().Parse(bytes));

        Assert.Contains("too many tracker", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsTrackerUrlAboveLengthLimit()
    {
        var tracker = new string('a', TorrentFileLimits.MaxTrackerUrlBytes + 1);
        var bytes = Encoding.ASCII.GetBytes(
            $"d8:announce{tracker.Length}:{tracker}4:infod4:name1:aee");

        var exception = Assert.Throws<FormatException>(() => new TorrentFileParser().Parse(bytes));

        Assert.Contains("tracker URL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsFileAboveSizeLimitBeforeDecoding()
    {
        var bytes = new byte[TorrentFileLimits.MaxFileSizeBytes + 1];

        var exception = Assert.Throws<FormatException>(() => new TorrentFileParser().Parse(bytes));

        Assert.Contains("import limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContentReader_AllowsFileAtExactSizeLimit()
    {
        var bytes = new byte[TorrentFileLimits.MaxFileSizeBytes];
        await using var stream = new MemoryStream(bytes, writable: false);

        var content = await TorrentFileContentReader.ReadAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(TorrentFileLimits.MaxFileSizeBytes, content.Length);
    }

    [Fact]
    public async Task ContentReader_RejectsKnownOversizeStreamBeforeReading()
    {
        await using var stream = new LengthReportingStream(TorrentFileLimits.MaxFileSizeBytes + 1);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => TorrentFileContentReader.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Contains("import limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, stream.ReadCount);
    }

    [Fact]
    public async Task ContentReader_StopsUnknownLengthStreamAtSizeLimit()
    {
        await using var stream = new GeneratedNonSeekableStream(TorrentFileLimits.MaxFileSizeBytes + 1L);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => TorrentFileContentReader.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Contains("import limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TorrentFileLimits.MaxFileSizeBytes + 1L, stream.BytesRead);
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

    private static byte[] CreateIntegerList(int itemCount)
    {
        var bytes = new byte[checked((itemCount * 3) + 2)];
        bytes[0] = (byte)'l';
        var position = 1;
        for (var i = 0; i < itemCount; i++)
        {
            bytes[position++] = (byte)'i';
            bytes[position++] = (byte)'0';
            bytes[position++] = (byte)'e';
        }

        bytes[position] = (byte)'e';
        return bytes;
    }

    private static byte[] CreateGroupedIntegerLists(params int[] groupSizes)
        => CreateGroupedLists(groupSizes, [(byte)'i', (byte)'0', (byte)'e']);

    private static byte[] CreateGroupedEmptyStringLists(params int[] groupSizes)
        => CreateGroupedLists(groupSizes, [(byte)'0', (byte)':']);

    private static byte[] CreateGroupedLists(int[] groupSizes, byte[] itemBytes)
    {
        var itemCount = groupSizes.Sum();
        var bytes = new byte[checked(2 + (groupSizes.Length * 2) + (itemCount * itemBytes.Length))];
        var position = 0;
        bytes[position++] = (byte)'l';
        foreach (var groupSize in groupSizes)
        {
            bytes[position++] = (byte)'l';
            for (var i = 0; i < groupSize; i++)
            {
                itemBytes.CopyTo(bytes, position);
                position += itemBytes.Length;
            }

            bytes[position++] = (byte)'e';
        }

        bytes[position] = (byte)'e';
        return bytes;
    }

    private static byte[] CreateTorrentWithTrackerCount(int trackerCount)
    {
        const string tracker = "udp://tracker";
        var builder = new StringBuilder("d4:infod4:name1:ae13:announce-listl");
        for (var i = 0; i < trackerCount; i++)
        {
            builder.Append('l')
                .Append(tracker.Length)
                .Append(':')
                .Append(tracker)
                .Append('e');
        }

        builder.Append("ee");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class LengthReportingStream(long length) : Stream
    {
        public int ReadCount { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class GeneratedNonSeekableStream(long length) : Stream
    {
        private long _remaining = length;

        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = (int)Math.Min(count, _remaining);
            Array.Clear(buffer, offset, bytesRead);
            _remaining -= bytesRead;
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..bytesRead].Clear();
            _remaining -= bytesRead;
            BytesRead += bytesRead;
            return ValueTask.FromResult(bytesRead);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
