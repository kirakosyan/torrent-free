using System.Xml.Linq;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class VersionMetadataConsistencyTests
{
    [Fact]
    public void ProjectAndWindowsManifest_AreAlignedOnVersion16Build6()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

        var csprojPath = Path.Combine(repoRoot, "src", "TorrentFree", "TorrentFree.csproj");
        var manifestPath = Path.Combine(repoRoot, "src", "TorrentFree", "Platforms", "Windows", "Package.appxmanifest");

        var csproj = XDocument.Load(csprojPath);
        var manifest = XDocument.Load(manifestPath);

        Assert.Equal("1.6", GetProjectProperty(csproj, "AppDisplayVersion"));
        Assert.Equal("6", GetProjectProperty(csproj, "AppBuildNumber"));
        Assert.Equal("$(AppDisplayVersion)", GetProjectProperty(csproj, "ApplicationDisplayVersion"));
        Assert.Equal("$(AppBuildNumber)", GetProjectProperty(csproj, "ApplicationVersion"));
        Assert.Equal("$(AppDisplayVersion).0", GetProjectProperty(csproj, "Version"));
        Assert.Equal("$(AppDisplayVersion).0.0", GetProjectProperty(csproj, "AssemblyVersion"));
        Assert.Equal("$(AppDisplayVersion).0.0", GetProjectProperty(csproj, "FileVersion"));
        Assert.Equal("$(AppDisplayVersion)", GetProjectProperty(csproj, "InformationalVersion"));

        var assemblyMetadata = csproj
            .Descendants("AssemblyMetadata")
            .ToDictionary(
                element => (string?)element.Attribute("Include") ?? string.Empty,
                element => (string?)element.Attribute("Value") ?? string.Empty,
                StringComparer.Ordinal);

        Assert.Equal("$(AppDisplayVersion)", assemblyMetadata["DisplayVersion"]);
        Assert.Equal("$(AppBuildNumber)", assemblyMetadata["BuildNumber"]);

        XNamespace manifestNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = manifest.Root?.Element(manifestNs + "Identity");

        Assert.NotNull(identity);
        Assert.Equal("1.6.6.0", (string?)identity!.Attribute("Version"));
    }

    private static string GetProjectProperty(XDocument document, string propertyName)
    {
        var property = document
            .Descendants(propertyName)
            .FirstOrDefault();

        Assert.NotNull(property);
        return property!.Value.Trim();
    }
}
