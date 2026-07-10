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
        if (torrentFileContent.Length > TorrentFileLimits.MaxFileSizeBytes)
        {
            throw new FormatException(
                $".torrent file exceeds the {TorrentFileLimits.MaxFileSizeBytes / (1024 * 1024)} MB import limit.");
        }

        var document = Bencode.DecodeDocument(torrentFileContent, "info");
        if (document.Root is not BDictionary dict)
        {
            throw new FormatException("Invalid .torrent file (root is not a dictionary).");
        }

        var name = TryGetUtf8String(dict, "info", "name");

        var trackers = new List<string>();
        var trackerEntryCount = 0;
        if (TryGetValue(dict, "announce", out var announce) && announce is BString announceStr)
        {
            CountTrackerEntry(ref trackerEntryCount);
            AddTracker(announceStr, trackers);
        }

        if (TryGetValue(dict, "announce-list", out var announceList) && announceList is BList tiers)
        {
            if (tiers.Items.Count > TorrentFileLimits.MaxTrackerCount)
            {
                throw new FormatException(".torrent file contains too many tracker tiers.");
            }

            foreach (var tier in tiers.Items)
            {
                if (tier is not BList tierList)
                {
                    continue;
                }

                foreach (var urlElement in tierList.Items)
                {
                    CountTrackerEntry(ref trackerEntryCount);
                    if (urlElement is not BString urlStr)
                    {
                        continue;
                    }

                    AddTracker(urlStr, trackers);
                }
            }
        }

        trackers = trackers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var infoHashHex = TryComputeInfoHashHex(torrentFileContent, dict, document);

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

        if (str.Bytes.Length > TorrentFileLimits.MaxTorrentNameBytes)
        {
            throw new FormatException("Torrent name exceeds the supported length.");
        }

        var value = Encoding.UTF8.GetString(str.Bytes);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void CountTrackerEntry(ref int trackerEntryCount)
    {
        if (++trackerEntryCount > TorrentFileLimits.MaxTrackerCount)
        {
            throw new FormatException(".torrent file contains too many tracker entries.");
        }
    }

    private static void AddTracker(BString tracker, List<string> trackers)
    {
        if (tracker.Bytes.Length > TorrentFileLimits.MaxTrackerUrlBytes)
        {
            throw new FormatException(".torrent tracker URL exceeds the supported length.");
        }

        var url = Encoding.UTF8.GetString(tracker.Bytes);
        if (!string.IsNullOrWhiteSpace(url))
        {
            trackers.Add(url);
        }
    }

    private static string? TryComputeInfoHashHex(
        byte[] torrentFileContent,
        BDictionary root,
        BencodeDocument document)
    {
        // Hash the exact bytes of the "info" dictionary as they appear in the file. Decoding
        // and re-encoding can change the bytes (key ordering, non-UTF-8 content) and produce
        // an info-hash that does not match the one peers and trackers use.
        if (!document.HasCapturedValue || document.CapturedValueLength == 0)
        {
            return null;
        }

        var infoBytes = torrentFileContent.AsSpan(
            document.CapturedValueOffset,
            document.CapturedValueLength);

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
