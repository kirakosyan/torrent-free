using System.Text;
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
    public void MicrosoftStoreListingImport_DeclaresAllImplementedLanguages()
    {
        var listingsPath = Path.Combine(
            GetRepoRoot(),
            "store", "microsoft-store", "listings.csv");

        Assert.True(File.Exists(listingsPath), $"{listingsPath} does not exist.");

        var rows = ReadCsvRows(listingsPath);
        Assert.NotEmpty(rows);

        var header = rows[0];
        Assert.Equal("Field", header[0]);
        Assert.Equal("ID", header[1]);
        Assert.Equal("Type (Type)", header[2]);
        Assert.Equal("default", header[3]);

        var listingLanguages = header
            .Skip(4)
            .ToArray();

        Assert.Equal(ExpectedMicrosoftStoreListingLanguages(), listingLanguages);
    }

    [Fact]
    public void MicrosoftStoreListingImport_HasRequiredTextForEveryLanguage()
    {
        var listingsPath = Path.Combine(
            GetRepoRoot(),
            "store", "microsoft-store", "listings.csv");

        var rows = ReadCsvRows(listingsPath);
        var header = rows[0];
        var languageColumns = ExpectedMicrosoftStoreListingLanguages()
            .ToDictionary(
                language => language,
                language => Array.IndexOf(header, language));

        foreach (var requiredField in ExpectedMicrosoftStoreListingTextFields())
        {
            var row = rows.SingleOrDefault(columns => columns[0] == requiredField);
            Assert.NotNull(row);

            foreach (var (language, columnIndex) in languageColumns)
            {
                Assert.True(columnIndex >= 0, $"{language} is missing from the Store listing import.");
                Assert.True(
                    columnIndex < row.Length && !string.IsNullOrWhiteSpace(row[columnIndex]),
                    $"{requiredField} is missing Store listing text for {language}.");
            }
        }
    }

    [Fact]
    public void MicrosoftStoreListingImport_ReferencesExpectedStoreLogoAssets()
    {
        var listingsPath = Path.Combine(
            GetRepoRoot(),
            "store", "microsoft-store", "listings.csv");

        var rows = ReadCsvRows(listingsPath);
        var header = rows[0];
        var languageColumns = ExpectedMicrosoftStoreListingLanguages()
            .Prepend("default")
            .ToDictionary(
                language => language,
                language => Array.IndexOf(header, language));

        foreach (var (field, width, height) in ExpectedMicrosoftStoreLogoAssets())
        {
            var row = rows.SingleOrDefault(columns => columns[0] == field);
            Assert.NotNull(row);

            foreach (var (language, columnIndex) in languageColumns)
            {
                Assert.True(columnIndex >= 0, $"{language} is missing from the Store listing import.");
                Assert.True(
                    columnIndex < row.Length && !string.IsNullOrWhiteSpace(row[columnIndex]),
                    $"{field} is missing a Store logo path for {language}.");

                var assetPath = ResolveStoreListingAssetPath(row[columnIndex]);
                Assert.True(File.Exists(assetPath), $"{assetPath} does not exist.");

                var (actualWidth, actualHeight) = PngSize(assetPath);
                Assert.Equal(width, actualWidth);
                Assert.Equal(height, actualHeight);
            }
        }
    }

    [Fact]
    public void StoreDescriptionDocument_HasLocalizedDescriptionsAndTenFeatures()
    {
        var descriptionPath = Path.Combine(GetRepoRoot(), "STORE_DESCRIPTION.md");
        var description = File.ReadAllText(descriptionPath);

        foreach (var language in ExpectedWindowsStoreLanguages())
        {
            var section = MarkdownSection(description, $"## {language}");

            Assert.Contains("### Short description", section);
            Assert.Contains("### Full description", section);
            Assert.Contains("### Features", section);

            foreach (var featureIndex in Enumerable.Range(1, 10))
            {
                Assert.Contains($"{featureIndex}. ", section);
            }
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
        Assert.Equal("False", ProjectProperty(document, "GenerateTemporaryStoreCertificate"));
    }

    private static string[] ExpectedAndroidStoreLanguages() =>
        ["en-US", .. ImplementedLocales()];

    private static string[] ExpectedWindowsStoreLanguages()
        => ExpectedAndroidStoreLanguages();

    private static string[] ExpectedMicrosoftStoreListingLanguages()
        => ExpectedWindowsStoreLanguages()
            .Select(language => language.ToLowerInvariant())
            .ToArray();

    private static string[] ExpectedMicrosoftStoreListingTextFields()
        => ["Title", "Description", "ShortDescription", .. Enumerable.Range(1, 10).Select(index => $"Feature{index}")];

    private static (string Field, int Width, int Height)[] ExpectedMicrosoftStoreLogoAssets() =>
        [
            ("StoreLogo300x300", 300, 300),
            ("StoreLogoOverride150x150", 150, 150),
            ("StoreLogoOverride71x71", 71, 71),
        ];

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
        Path.Combine(GetRepoRoot(), "src", "TorrentFree.Core", "Resources", "Strings");

    private static string? ProjectProperty(XDocument document, string name) =>
        document
            .Descendants(name)
            .FirstOrDefault()
            ?.Value
            .Trim();

    private static string[][] ReadCsvRows(string path)
    {
        var text = File.ReadAllText(path);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (current == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && current == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!inQuotes && current is '\r' or '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
                continue;
            }

            field.Append(current);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows.ToArray();
    }

    private static string ResolveStoreListingAssetPath(string csvPath) =>
        Path.Combine(GetRepoRoot(), "store", csvPath.Replace('/', Path.DirectorySeparatorChar));

    private static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length >= 24, $"{path} is not a valid PNG.");
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);

        var width = ReadBigEndianInt32(bytes, 16);
        var height = ReadBigEndianInt32(bytes, 20);
        return (width, height);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        bytes[offset] << 24
        | bytes[offset + 1] << 16
        | bytes[offset + 2] << 8
        | bytes[offset + 3];

    private static string MarkdownSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{heading} is missing.");

        var next = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0
            ? markdown[start..]
            : markdown[start..next];
    }

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
}
