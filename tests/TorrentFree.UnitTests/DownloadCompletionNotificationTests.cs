using System.Xml.Linq;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadCompletionNotificationTests
{
    [Fact]
    public void Toast_RemovesInvalidXmlCharactersAndPreservesUnicodePairs()
    {
        var title = "Done\u0001\u0000\uFFFE\uFFFF\uD800!";
        var body = "日本語\uDC00 / \uD83D\uDE80 & <ready>\t\n";
        var payload = XElement.Parse(DownloadCompletionNotification.CreatePayload("id", title, body));
        Assert.Equal(new[] { "Done!", "日本語 / 🚀 & <ready>\t\n" }, payload.Descendants("text").Select(t => t.Value));
    }

    [Fact]
    public void Toast_PreservesLocalizedTextAndSafelyRoundTripsTorrentIdentity()
    {
        var id = "torrent & /?= #日本語";
        var title = "Download <complete> & ready";
        var body = "Файл \"book\" — 日本語 & <audio>";
        var payload = XElement.Parse(DownloadCompletionNotification.CreatePayload(id, title, body));
        Assert.Equal("ToastGeneric", (string?)payload.Descendants("binding").Single().Attribute("template"));
        Assert.Equal(new[] { title, body }, payload.Descendants("text").Select(t => t.Value));
        Assert.Equal(id, DownloadCompletionNotification.GetTorrentId((string?)payload.Attribute("launch")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("action=delete")]
    [InlineData("id=torrent")]
    [InlineData("torrentId=torrent&delete=true")]
    [InlineData("torrentId=")]
    [InlineData("torrentId=%20")]
    public void UnrelatedOrInvalidActivation_IsIgnored(string? value)
        => Assert.Null(DownloadCompletionNotification.GetTorrentId(value));

    [Fact]
    public void ToastTag_IsStableAndDoesNotReplaceAnotherTorrentsNotification()
    {
        var id = Guid.NewGuid().ToString();
        Assert.Equal(DownloadCompletionNotification.GetTag(id), DownloadCompletionNotification.GetTag(id));
        Assert.Equal(16, DownloadCompletionNotification.GetTag(id).Length);
        Assert.NotEqual(DownloadCompletionNotification.GetTag(id), DownloadCompletionNotification.GetTag(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void PackagedNotifications_RegisterMatchingActivatorAndExecutable()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var manifest = XDocument.Load(Path.Combine(root, "src", "TorrentFree", "Platforms", "Windows", "Package.appxmanifest"));
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
        var clsid = (string)manifest.Descendants(desktop + "ToastNotificationActivation").Single().Attribute("ToastActivatorCLSID")!;
        Assert.True(Guid.TryParse(clsid, out _));
        Assert.Equal(clsid, (string?)manifest.Descendants(com + "Class").Single().Attribute("Id"));
        var server = manifest.Descendants(com + "ExeServer").Single();
        Assert.Equal("----AppNotificationActivated:", (string?)server.Attribute("Arguments"));
        // MAUI substitutes its executable token only on Application, not com:ExeServer.
        // A token left here packages successfully but cannot launch a notification click.
        Assert.Equal("TorrentFree.exe", (string?)server.Attribute("Executable"));
    }
}
