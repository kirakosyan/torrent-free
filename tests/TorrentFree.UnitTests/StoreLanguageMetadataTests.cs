using System.Xml.Linq;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StoreLanguageMetadataTests
{
    private static readonly XNamespace AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private static readonly XNamespace WindowsManifestNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    [Fact]
    public void WindowsPackageManifest_DeclaresAllImplementedLanguages()
    {
        var manifestPath = Path.Combine(
            GetRepoRoot(),
            "src", "TorrentFree", "Platforms", "Windows", "Package.appxmanifest");

        var document = XDocument.Load(manifestPath);
        var languages = document.Root?
            .Element(WindowsManifestNamespace + "Resources")?
            .Elements(WindowsManifestNamespace + "Resource")
            .Select(resource => (string?)resource.Attribute("Language"))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(language => language!)
            .ToArray();

        Assert.Equal(ExpectedWindowsStoreLanguages(), languages);
    }

    [Fact]
    public void AndroidManifest_ReferencesLocaleConfig()
    {
        var manifestPath = Path.Combine(
            GetRepoRoot(),
            "src", "TorrentFree", "Platforms", "Android", "AndroidManifest.xml");

        var document = XDocument.Load(manifestPath);
        var application = document.Root?.Element("application");
        var localeConfig = application?.Attribute(AndroidNamespace + "localeConfig")?.Value;
        var label = application?.Attribute(AndroidNamespace + "label")?.Value;

        Assert.Equal("@xml/locale_config", localeConfig);
        Assert.Equal("@string/app_name", label);
    }

    [Fact]
    public void AndroidLocaleConfig_DeclaresAllImplementedLanguages()
    {
        var localeConfigPath = Path.Combine(
            GetRepoRoot(),
            "src", "TorrentFree", "Platforms", "Android", "Resources", "xml", "locale_config.xml");

        var document = XDocument.Load(localeConfigPath);
        var languages = document.Root?
            .Elements("locale")
            .Select(locale => (string?)locale.Attribute(AndroidNamespace + "name"))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(language => language!)
            .ToArray();

        Assert.Equal(ExpectedAndroidStoreLanguages(), languages);
    }

    [Fact]
    public void AndroidAppNameResources_DeclareAllImplementedLanguages()
    {
        foreach (var language in ExpectedAndroidStoreLanguages())
        {
            var stringsPath = Path.Combine(
                GetRepoRoot(),
                "src", "TorrentFree", "Platforms", "Android", "Resources", AndroidValuesDirectoryName(language), "strings.xml");

            Assert.True(File.Exists(stringsPath), $"{stringsPath} does not exist.");

            var document = XDocument.Load(stringsPath);
            var appName = document.Root?
                .Elements("string")
                .SingleOrDefault(element => (string?)element.Attribute("name") == "app_name")
                ?.Value;

            Assert.Equal("Torrent Client App", appName);
        }
    }

    [Fact]
    public void WindowsPriResources_DeclareAllImplementedLanguages()
    {
        foreach (var language in ExpectedWindowsStoreLanguages())
        {
            var resourcePath = Path.Combine(
                GetRepoRoot(),
                "src", "TorrentFree", "Strings", language, "Resources.resw");

            Assert.True(File.Exists(resourcePath), $"{resourcePath} does not exist.");

            var keys = XDocument.Load(resourcePath)
                .Root?
                .Elements("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray()
                ?? [];

            Assert.Contains("AppDisplayName", keys);
            Assert.Contains("AppDescription", keys);
        }
    }

    [Fact]
    public void WindowsPackageLanguageMarkers_DeclareAllImplementedLanguages()
    {
        foreach (var language in ExpectedWindowsStoreLanguages())
        {
            var markerPath = Path.Combine(
                GetRepoRoot(),
                "src", "TorrentFree", "Platforms", "Windows", "PackageLanguages", $"PackageLanguage.language-{language}.txt");

            Assert.True(File.Exists(markerPath), $"{markerPath} does not exist.");
        }
    }

    [Fact]
    public void ProjectDefaultLanguage_MatchesStoreMetadataFallbackLanguage()
    {
        var csprojPath = Path.Combine(GetRepoRoot(), "src", "TorrentFree", "TorrentFree.csproj");
        var document = XDocument.Load(csprojPath);

        var defaultLanguage = document
            .Descendants("DefaultLanguage")
            .FirstOrDefault()
            ?.Value
            .Trim();

        Assert.Equal(ExpectedAndroidStoreLanguages()[0], defaultLanguage);
    }

    [Fact]
    public void ProjectWindowsPackageSettings_KeepLanguagesInMainStorePackage()
    {
        var csprojPath = Path.Combine(GetRepoRoot(), "src", "TorrentFree", "TorrentFree.csproj");
        var document = XDocument.Load(csprojPath);

        Assert.Equal("Scale|DXFeatureLevel", ProjectProperty(document, "AppxBundleAutoResourcePackageQualifiers"));
        Assert.Equal("False", ProjectProperty(document, "AppxSymbolPackageEnabled"));
        Assert.Equal("False", ProjectProperty(document, "GenerateTemporaryStoreCertificate"));
    }

    private static string[] ExpectedAndroidStoreLanguages() =>
        ["en-US", .. ImplementedLocales()];

    private static string[] ExpectedWindowsStoreLanguages()
        => ExpectedAndroidStoreLanguages();

    private static string[] ImplementedLocales()
    {
        return Directory
            .GetFiles(ResourcesDirectory(), "AppResources.*.resx")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(fileName => fileName!["AppResources.".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string AndroidValuesDirectoryName(string language)
    {
        if (language == "en-US")
        {
            return "values";
        }

        var parts = language.Split('-', 2);
        return parts.Length == 1
            ? $"values-{parts[0]}"
            : $"values-{parts[0]}-r{parts[1]}";
    }

    private static string ResourcesDirectory() =>
        Path.Combine(GetRepoRoot(), "src", "TorrentFree", "Resources", "Strings");

    private static string? ProjectProperty(XDocument document, string name) =>
        document
            .Descendants(name)
            .FirstOrDefault()
            ?.Value
            .Trim();

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
}
