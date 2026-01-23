using System.Buffers;
using System.Text;

namespace TorrentFree.Services;

internal abstract record BElement;
internal sealed record BString(byte[] Bytes) : BElement;
internal sealed record BInteger(long Value) : BElement;
internal sealed record BList(IReadOnlyList<BElement> Items) : BElement;
internal sealed record BDictionary(IReadOnlyDictionary<string, BElement> Values) : BElement;

internal static class Bencode
{
    public static BElement Decode(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var element = DecodeElement(data, ref position);

        if (position != data.Length)
        {
            // trailing bytes are allowed in some files but not expected here
        }

        return element;
    }

    private static BElement DecodeElement(ReadOnlySpan<byte> data, ref int position)
    {
        if ((uint)position >= (uint)data.Length)
        {
            throw new FormatException("Unexpected end of data.");
        }

        var current = data[position];
        return current switch
        {
            (byte)'i' => DecodeInteger(data, ref position),
            (byte)'l' => DecodeList(data, ref position),
            (byte)'d' => DecodeDictionary(data, ref position),
            >= (byte)'0' and <= (byte)'9' => DecodeString(data, ref position),
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

        if (!TryParseInt64Ascii(span, out var value))
        {
            throw new FormatException("Invalid integer value.");
        }

        return new BInteger(value);
    }

    private static BString DecodeString(ReadOnlySpan<byte> data, ref int position)
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

            checked
            {
                length = (length * 10) + (c - (byte)'0');
            }
            position++;
        }

        if (position >= data.Length || data[position] != (byte)':')
        {
            throw new FormatException("Invalid string delimiter.");
        }

        position++; // :

        if (length < 0 || position + length > data.Length)
        {
            throw new FormatException("Invalid string length.");
        }

        var bytes = data.Slice(position, length).ToArray();
        position += length;
        return new BString(bytes);
    }

    private static BList DecodeList(ReadOnlySpan<byte> data, ref int position)
    {
        position++; // l
        var items = new List<BElement>();
        while (position < data.Length && data[position] != (byte)'e')
        {
            items.Add(DecodeElement(data, ref position));
        }

        if (position >= data.Length)
        {
            throw new FormatException("Unterminated list.");
        }

        position++; // e
        return new BList(items);
    }

    private static BDictionary DecodeDictionary(ReadOnlySpan<byte> data, ref int position)
    {
        position++; // d
        var dict = new Dictionary<string, BElement>(StringComparer.Ordinal);
        while (position < data.Length && data[position] != (byte)'e')
        {
            var keyElement = DecodeString(data, ref position);
            var key = Encoding.UTF8.GetString(keyElement.Bytes);
            var value = DecodeElement(data, ref position);
            dict[key] = value;
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

        for (; i < span.Length; i++)
        {
            var c = span[i];
            if (c is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            checked
            {
                value = (value * 10) + (c - (byte)'0');
            }
        }

        value *= sign;
        return true;
    }
}
