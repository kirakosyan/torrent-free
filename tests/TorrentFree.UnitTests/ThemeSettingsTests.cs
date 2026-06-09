using TorrentFree.Models;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class ThemeSettingsTests
{
    [Theory]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    [InlineData("system", "system")]
    [InlineData("LIGHT", "light")]
    [InlineData("  Dark  ", "dark")]
    public void Normalize_ReturnsCanonicalCode_ForKnownValues(string input, string expected)
    {
        Assert.Equal(expected, ThemeSettings.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("midnight")]
    public void Normalize_FallsBackToSystem_ForUnknownOrBlank(string? input)
    {
        Assert.Equal(ThemeSettings.System, ThemeSettings.Normalize(input));
    }
}
