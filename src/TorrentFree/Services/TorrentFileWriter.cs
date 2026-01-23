using System.Text;

namespace TorrentFree.Services;

internal static class TorrentFileWriter
{
    public static byte[] EncodeCanonical(BElement element)
    {
        using var ms = new MemoryStream();
        WriteElement(ms, element);
        return ms.ToArray();
    }

    private static void WriteElement(Stream stream, BElement element)
    {
        switch (element)
        {
            case BInteger i:
                WriteAscii(stream, "i");
                WriteAscii(stream, i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteAscii(stream, "e");
                return;
            case BString s:
                WriteAscii(stream, s.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteAscii(stream, ":");
                stream.Write(s.Bytes);
                return;
            case BList l:
                WriteAscii(stream, "l");
                foreach (var item in l.Items)
                {
                    WriteElement(stream, item);
                }
                WriteAscii(stream, "e");
                return;
            case BDictionary d:
                WriteAscii(stream, "d");
                foreach (var kvp in d.Values.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var keyBytes = Encoding.UTF8.GetBytes(kvp.Key);
                    WriteAscii(stream, keyBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteAscii(stream, ":");
                    stream.Write(keyBytes);
                    WriteElement(stream, kvp.Value);
                }
                WriteAscii(stream, "e");
                return;
            default:
                throw new NotSupportedException($"Unsupported bencode type: {element.GetType().Name}");
        }
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }
}
