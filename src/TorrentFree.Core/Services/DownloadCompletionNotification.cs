using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TorrentFree.Services;

/// <summary>Windows toast content and its download selection arguments.</summary>
public static class DownloadCompletionNotification
{
    public static string CreatePayload(string torrentId, string title, string body)
        => new XElement("toast",
            new XAttribute("launch", $"torrentId={Uri.EscapeDataString(torrentId)}"),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", SanitizeXmlText(title)), new XElement("text", SanitizeXmlText(body)))))
            .ToString(SaveOptions.DisableFormatting);

    private static string SanitizeXmlText(string text)
    {
        var valid = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (XmlConvert.IsXmlChar(text[i])) valid.Append(text[i]);
            else if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // Valid supplementary Unicode (including emoji) uses a surrogate pair.
                valid.Append(text[i]).Append(text[++i]);
            }
        }
        return valid.ToString();
    }

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
