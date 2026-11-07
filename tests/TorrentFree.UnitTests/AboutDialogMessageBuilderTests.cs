using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AboutDialogMessageBuilderTests
{
    [Fact]
    public void Build_IncludesOnlyIntroAndVersionInformation()
    {
        var message = AboutDialogMessageBuilder.Build(
            "Torrent Free is free to use.",
            "Version",
            "1.4",
            "Build",
            "4",
            "File version",
            "1.4.0.0",
            "n/a");

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Torrent Free is free to use.",
                string.Empty,
                "Version: 1.4",
                "Build: 4",
                "File version: 1.4.0.0"),
            message);
    }

    [Fact]
    public void Build_UsesUnavailableFallbackForMissingValues()
    {
        var message = AboutDialogMessageBuilder.Build(
            "About",
            "Version",
            null,
            "Build",
            "",
            "File version",
            " ",
            "n/a");

        Assert.Contains("Version: n/a", message, StringComparison.Ordinal);
        Assert.Contains("Build: n/a", message, StringComparison.Ordinal);
        Assert.Contains("File version: n/a", message, StringComparison.Ordinal);
    }
}
