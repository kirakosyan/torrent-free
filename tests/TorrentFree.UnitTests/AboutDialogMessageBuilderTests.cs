using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AboutDialogMessageBuilderTests
{
    [Fact]
    public void Build_IncludesVersionBuildFileVersionAndSource()
    {
        var message = AboutDialogMessageBuilder.Build(
            "Torrent Free is free to use.",
            "Version",
            "1.4",
            "Build",
            "4",
            "File version",
            "1.4.0.0",
            "Source",
            "https://github.com/kirakosyan/torrent-free",
            "n/a");

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Torrent Free is free to use.",
                string.Empty,
                "Version: 1.4",
                "Build: 4",
                "File version: 1.4.0.0",
                string.Empty,
                "Source: https://github.com/kirakosyan/torrent-free"),
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
            "Source",
            "https://example.test",
            "n/a");

        Assert.Contains("Version: n/a", message, StringComparison.Ordinal);
        Assert.Contains("Build: n/a", message, StringComparison.Ordinal);
        Assert.Contains("File version: n/a", message, StringComparison.Ordinal);
    }
}
