using System.Security.Cryptography;
using System.Text;

namespace TorrentFree.Services;

public interface ITorrentFileParser
{
    TorrentMetadata Parse(byte[] torrentFileContent);
}

public sealed class TorrentFileParser : ITorrentFileParser
{
    public TorrentMetadata Parse(byte[] torrentFileContent)
    {
        ArgumentNullException.ThrowIfNull(torrentFileContent);

        var root = Bencode.Decode(torrentFileContent);
        if (root is not BDictionary dict)
        {
            throw new FormatException("Invalid .torrent file (root is not a dictionary).");
        }

        var name = TryGetUtf8String(dict, "info", "name");

        var trackers = new List<string>();
        if (TryGetValue(dict, "announce", out var announce) && announce is BString announceStr)
        {
            var t = Encoding.UTF8.GetString(announceStr.Bytes);
            if (!string.IsNullOrWhiteSpace(t))
            {
                trackers.Add(t);
            }
        }

        if (TryGetValue(dict, "announce-list", out var announceList) && announceList is BList tiers)
        {
            foreach (var tier in tiers.Items)
            {
                if (tier is not BList tierList)
                {
                    continue;
                }

                foreach (var urlElement in tierList.Items)
                {
                    if (urlElement is not BString urlStr)
                    {
                        continue;
                    }

                    var url = Encoding.UTF8.GetString(urlStr.Bytes);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        trackers.Add(url);
                    }
                }
            }
        }

        trackers = trackers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var infoHashHex = TryComputeInfoHashHex(dict);

        return new TorrentMetadata(name, infoHashHex, trackers);
    }

    private static bool TryGetValue(BDictionary dict, string key, out BElement value)
        => dict.Values.TryGetValue(key, out value!);

    private static string? TryGetUtf8String(BDictionary dict, string dictKey, string stringKey)
    {
        if (!TryGetValue(dict, dictKey, out var info) || info is not BDictionary infoDict)
        {
            return null;
        }

        if (!infoDict.Values.TryGetValue(stringKey, out var nameElement) || nameElement is not BString str)
        {
            return null;
        }

        var value = Encoding.UTF8.GetString(str.Bytes);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryComputeInfoHashHex(BDictionary dict)
    {
        if (!TryGetValue(dict, "info", out var info) || info is not BDictionary infoDict)
        {
            return null;
        }

        // Re-encode the info dictionary in canonical bencode form.
        // This is required to compute the same info-hash peers use.
        var infoBytes = TorrentFileWriter.EncodeCanonical(infoDict);
        var hash = SHA1.HashData(infoBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
