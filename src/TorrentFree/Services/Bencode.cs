using System.Text;

namespace TorrentFree.Services;

internal abstract record BElement;
internal sealed record BString(byte[] Bytes) : BElement;
internal sealed record BInteger(long Value) : BElement;
internal sealed record BList(IReadOnlyList<BElement> Items) : BElement;
internal sealed record BDictionary(IReadOnlyDictionary<string, BElement> Values) : BElement;

internal readonly record struct BencodeDocument(
    BElement Root,
    int CapturedValueOffset,
    int CapturedValueLength)
{
    public bool HasCapturedValue => CapturedValueOffset >= 0;
}

internal static class Bencode
{
    public static BElement Decode(ReadOnlySpan<byte> data)
        => DecodeDocument(data, topLevelValueKey: null).Root;

    /// <summary>
    /// Decodes a bencoded document and, as part of the same traversal, records the exact
    /// byte range occupied by a requested top-level value. This lets callers hash the raw
    /// info dictionary without reparsing every top-level value.
    /// </summary>
    public static BencodeDocument DecodeDocument(ReadOnlySpan<byte> data, string? topLevelValueKey)
    {
        var position = 0;
        var state = new DecoderState();
        var capture = new RawValueCapture(topLevelValueKey);
        var root = DecodeElement(data, ref position, depth: 0, isTopLevel: true, state, ref capture);

        if (position != data.Length)
        {
            throw new FormatException("Unexpected trailing bencode data.");
        }

        return new BencodeDocument(root, capture.Offset, capture.Length);
    }

    private static BElement DecodeElement(
        ReadOnlySpan<byte> data,
        ref int position,
        int depth,
        bool isTopLevel,
        DecoderState state,
        ref RawValueCapture capture)
    {
        if (depth > TorrentFileLimits.MaxBencodeDepth)
        {
            throw new FormatException("Bencode nesting exceeds the maximum supported depth.");
        }

        if ((uint)position >= (uint)data.Length)
        {
            throw new FormatException("Unexpected end of data.");
        }

        state.AddNode();

        var current = data[position];
        return current switch
        {
            (byte)'i' => DecodeInteger(data, ref position),
            (byte)'l' => DecodeList(data, ref position, depth, state, ref capture),
            (byte)'d' => DecodeDictionary(data, ref position, depth, isTopLevel, state, ref capture),
            >= (byte)'0' and <= (byte)'9' => DecodeString(data, ref position, state),
            _ => throw new FormatException("Invalid bencode prefix.")
        };
    }

    private static BInteger DecodeInteger(ReadOnlySpan<byte> data, ref int position)
    {
        position++; // i
        var start = position;
        while (position < data.Length && data[position] != (byte)'e')
        {
            position++;
        }

        if (position >= data.Length)
        {
            throw new FormatException("Unterminated integer.");
        }

        var span = data[start..position];
        position++; // e

        if (span.Length == 0)
        {
            throw new FormatException("Empty integer.");
        }

        // BEP 0003: leading zeros are not allowed (except "i0e" for zero).
        if (span.Length > 1 && span[0] == (byte)'0')
        {
            throw new FormatException("Leading zeros in integer are not allowed.");
        }

        if (span.Length > 2 && span[0] == (byte)'-' && span[1] == (byte)'0')
        {
            throw new FormatException("Leading zeros in negative integer are not allowed.");
        }

        if (!TryParseInt64Ascii(span, out var value))
        {
            throw new FormatException("Invalid integer value.");
        }

        if (value == 0 && span[0] == (byte)'-')
        {
            throw new FormatException("Negative zero is not allowed.");
        }

        return new BInteger(value);
    }

    private static BString DecodeString(ReadOnlySpan<byte> data, ref int position, DecoderState state)
    {
        var length = 0;
        while (position < data.Length)
        {
            var c = data[position];
            if (c == (byte)':')
            {
                break;
            }

            if (c is < (byte)'0' or > (byte)'9')
            {
                throw new FormatException("Invalid string length.");
            }

            try
            {
                checked
                {
                    length = (length * 10) + (c - (byte)'0');
                }
            }
            catch (OverflowException)
            {
                throw new FormatException("String length overflow.");
            }

            position++;
        }

        if (position >= data.Length || data[position] != (byte)':')
        {
            throw new FormatException("Invalid string delimiter.");
        }

        position++; // :

        if (length > data.Length - position)
        {
            throw new FormatException("Invalid string length.");
        }

        state.AddString(length);
        var bytes = data.Slice(position, length).ToArray();
        position += length;
        return new BString(bytes);
    }

    private static BList DecodeList(
        ReadOnlySpan<byte> data,
        ref int position,
        int depth,
        DecoderState state,
        ref RawValueCapture capture)
    {
        position++; // l
        var items = new List<BElement>();
        var entryCount = 0;
        while (position < data.Length && data[position] != (byte)'e')
        {
            state.AddContainerEntry(ref entryCount);
            items.Add(DecodeElement(data, ref position, depth + 1, isTopLevel: false, state, ref capture));
        }

        if (position >= data.Length)
        {
            throw new FormatException("Unterminated list.");
        }

        position++; // e
        return new BList(items);
    }

    private static BDictionary DecodeDictionary(
        ReadOnlySpan<byte> data,
        ref int position,
        int depth,
        bool isTopLevel,
        DecoderState state,
        ref RawValueCapture capture)
    {
        position++; // d
        var dict = new Dictionary<string, BElement>(StringComparer.Ordinal);
        var entryCount = 0;
        while (position < data.Length && data[position] != (byte)'e')
        {
            state.AddContainerEntry(ref entryCount);
            var keyElement = DecodeString(data, ref position, state);
            if (keyElement.Bytes.Length > TorrentFileLimits.MaxDictionaryKeyBytes)
            {
                throw new FormatException("Bencode dictionary key exceeds the supported length.");
            }

            var key = Encoding.UTF8.GetString(keyElement.Bytes);
            var valueStart = position;
            var value = DecodeElement(data, ref position, depth + 1, isTopLevel: false, state, ref capture);

            if (isTopLevel)
            {
                capture.TryCapture(key, valueStart, position - valueStart);
            }

            if (!dict.TryAdd(key, value))
            {
                throw new FormatException("Duplicate bencode dictionary key.");
            }
        }

        if (position >= data.Length)
        {
            throw new FormatException("Unterminated dictionary.");
        }

        position++; // e
        return new BDictionary(dict);
    }

    private static bool TryParseInt64Ascii(ReadOnlySpan<byte> span, out long value)
    {
        value = 0;
        var sign = 1;
        var i = 0;

        if (span[0] == (byte)'-')
        {
            sign = -1;
            i = 1;
            if (span.Length == 1)
            {
                return false;
            }
        }

        try
        {
            checked
            {
                for (; i < span.Length; i++)
                {
                    var c = span[i];
                    if (c is < (byte)'0' or > (byte)'9')
                    {
                        return false;
                    }

                    value = (value * 10) + (c - (byte)'0');
                }

                value *= sign;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private sealed class DecoderState
    {
        private int _nodeCount;
        private int _stringCount;
        private int _containerEntryCount;
        private int _stringBytes;

        public void AddNode()
        {
            if (++_nodeCount > TorrentFileLimits.MaxBencodeNodes)
            {
                throw new FormatException("Bencode document contains too many elements.");
            }
        }

        public void AddString(int byteCount)
        {
            if (++_stringCount > TorrentFileLimits.MaxBencodeStrings)
            {
                throw new FormatException("Bencode document contains too many strings.");
            }

            try
            {
                checked
                {
                    _stringBytes += byteCount;
                }
            }
            catch (OverflowException)
            {
                throw new FormatException("Bencode string data exceeds the supported size.");
            }

            if (_stringBytes > TorrentFileLimits.MaxFileSizeBytes)
            {
                throw new FormatException("Bencode string data exceeds the supported size.");
            }
        }

        public void AddContainerEntry(ref int localEntryCount)
        {
            localEntryCount++;
            if (localEntryCount > TorrentFileLimits.MaxBencodeEntriesPerContainer)
            {
                throw new FormatException("Bencode container contains too many entries.");
            }

            if (++_containerEntryCount > TorrentFileLimits.MaxBencodeContainerEntries)
            {
                throw new FormatException("Bencode document contains too many container entries.");
            }
        }
    }

    private struct RawValueCapture(string? key)
    {
        private readonly string? _key = key;

        public int Offset { get; private set; } = -1;
        public int Length { get; private set; }

        public void TryCapture(string key, int offset, int length)
        {
            if (Offset < 0 && string.Equals(key, _key, StringComparison.Ordinal))
            {
                Offset = offset;
                Length = length;
            }
        }
    }
}
