using Xunit;

namespace TorrentFree.UnitTests;

public sealed class LocalizationResourceEncodingTests
{
    [Fact]
    public void SpanishResourceFile_DoesNotContainMojibake()
    {
        var content = ReadResource("AppResources.es.resx");

        Assert.Contains("Código fuente", content, StringComparison.Ordinal);
        Assert.DoesNotContain("CÃ³digo fuente", content, StringComparison.Ordinal);
        Assert.Contains("Sí", content, StringComparison.Ordinal);
        Assert.DoesNotContain("SÃ­", content, StringComparison.Ordinal);
        Assert.Contains("Versión del archivo", content, StringComparison.Ordinal);
        Assert.Contains("Compilación", content, StringComparison.Ordinal);
    }

    [Fact]
    public void FrenchResourceFile_DoesNotContainMojibake()
    {
        var content = ReadResource("AppResources.fr.resx");

        Assert.Contains("Arrêter", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ArrÃªter", content, StringComparison.Ordinal);
        Assert.Contains("À propos de Torrent Free", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã€ propos de Torrent Free", content, StringComparison.Ordinal);
        Assert.Contains("Version du fichier", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RussianResourceFile_DoesNotContainMojibake()
    {
        var content = ReadResource("AppResources.ru.resx");

        Assert.Contains("Остановить", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ÐžÑ", content, StringComparison.Ordinal);
        Assert.Contains("Исходный код", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Ð˜Ñ", content, StringComparison.Ordinal);
        Assert.Contains("Версия файла", content, StringComparison.Ordinal);
        Assert.Contains("Сборка", content, StringComparison.Ordinal);
    }

    private static string ReadResource(string fileName)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "TorrentFree", "Resources", "Strings", fileName);

        return File.ReadAllText(path);
    }
}
