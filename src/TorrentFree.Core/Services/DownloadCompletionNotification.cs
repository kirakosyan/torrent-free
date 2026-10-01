using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TorrentFree.Services;

/// <summary>Windows toast content and its download selection arguments.</summary>
public static class DownloadCompletionNotification
{
    public static string CreatePayload(string torrentId, string title, string body)
        => new XElement("toast",
            new XAttribute("launch", $"torrentId={Uri.EscapeDataString(torrentId)}"),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", title), new XElement("text", body))))
            .ToString(SaveOptions.DisableFormatting);

    // Tags must be stable across app restarts and fit Windows' 16-character limit.
    public static string GetTag(string torrentId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(torrentId)))[..16];

    public static string? GetTorrentId(string? arguments)
    {
        if (arguments is null || !arguments.StartsWith("torrentId=", StringComparison.Ordinal)
            || arguments.Contains('&'))
            return null;
        var id = Uri.UnescapeDataString(arguments[10..]);
        return string.IsNullOrWhiteSpace(id) || id.Length > 128 ? null : id;
    }
}
