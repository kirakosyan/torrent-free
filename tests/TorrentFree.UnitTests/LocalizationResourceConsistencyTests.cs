using System.Xml.Linq;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class LocalizationResourceConsistencyTests
{
    [Fact]
    public void AllLocalizedResourceFiles_MatchBaseResourceKeys()
    {
        var resourcesDirectory = Path.Combine(
            GetRepoRoot(),
            "src", "TorrentFree.Core", "Resources", "Strings");

        var baseResourcePath = Path.Combine(resourcesDirectory, "AppResources.resx");
        var baseKeys = ReadKeys(baseResourcePath);

        foreach (var resourcePath in Directory.GetFiles(resourcesDirectory, "AppResources*.resx"))
        {
            var resourceKeys = ReadKeys(resourcePath);

            var missingKeys = baseKeys.Except(resourceKeys, StringComparer.Ordinal).ToArray();
            var extraKeys = resourceKeys.Except(baseKeys, StringComparer.Ordinal).ToArray();

            Assert.True(
                missingKeys.Length == 0,
                $"{Path.GetFileName(resourcePath)} is missing keys: {string.Join(", ", missingKeys)}");
            Assert.True(
                extraKeys.Length == 0,
                $"{Path.GetFileName(resourcePath)} has unexpected keys: {string.Join(", ", extraKeys)}");
        }
    }

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    private static string[] ReadKeys(string path) =>
        XDocument.Load(path)
            .Root?
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray()
        ?? [];
}
