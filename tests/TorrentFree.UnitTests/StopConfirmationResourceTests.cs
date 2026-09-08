using System.Xml.Linq;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StopConfirmationResourceTests
{
    [Fact]
    public void EnglishStopConfirmation_ExplainsResumeInsteadOfReset()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "TorrentFree.Core", "Resources", "Strings", "AppResources.resx"));

        var document = XDocument.Load(path);
        var message = document
            .Descendants("data")
            .Single(static data => string.Equals((string?)data.Attribute("name"), "StopAndResetMessage", StringComparison.Ordinal))
            .Element("value")?
            .Value;

        Assert.NotNull(message);
        Assert.DoesNotContain("reset download progress", message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resume later", message!, StringComparison.OrdinalIgnoreCase);
    }
}
