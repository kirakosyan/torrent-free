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

        var infoHashHex = TryComputeInfoHashHex(torrentFileContent, dict);

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

    private static string? TryComputeInfoHashHex(byte[] torrentFileContent, BDictionary root)
    {
        // Hash the exact bytes of the "info" dictionary as they appear in the file. Decoding
        // and re-encoding can change the bytes (key ordering, non-UTF-8 content) and produce
        // an info-hash that does not match the one peers and trackers use.
        if (!Bencode.TryGetTopLevelRawValue(torrentFileContent, "info", out var infoBytes) || infoBytes.Length == 0)
        {
            return null;
        }

        // A v2-only torrent (BEP 52: "meta version" >= 2 with no v1 "pieces") is identified by
        // the SHA-256 of its info dictionary; v1 and hybrid torrents use SHA-1. BuildMagnetLink
        // selects the matching URN scheme from the resulting hex length (40 = v1, 64 = v2).
        if (IsV2OnlyInfoDictionary(root))
        {
            return Convert.ToHexString(SHA256.HashData(infoBytes)).ToLowerInvariant();
        }

        return Convert.ToHexString(SHA1.HashData(infoBytes)).ToLowerInvariant();
    }

    private static bool IsV2OnlyInfoDictionary(BDictionary root)
    {
        if (!TryGetValue(root, "info", out var info) || info is not BDictionary infoDict)
        {
            return false;
        }

        var hasV1Pieces = infoDict.Values.ContainsKey("pieces");
        var metaVersion = infoDict.Values.TryGetValue("meta version", out var version) && version is BInteger metaInteger
            ? metaInteger.Value
            : 0;

        return metaVersion >= 2 && !hasV1Pieces;
    }
}
