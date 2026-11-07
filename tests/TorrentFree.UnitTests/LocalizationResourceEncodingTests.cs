using Xunit;

namespace TorrentFree.UnitTests;

public sealed class LocalizationResourceEncodingTests
{
    [Fact]
    public void SpanishResourceFile_DoesNotContainMojibake()
    {
        var content = ReadResource("AppResources.es.resx");

        Assert.Contains("datos guardados válidos", content, StringComparison.Ordinal);
        Assert.DoesNotContain("vÃ¡lidos", content, StringComparison.Ordinal);
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
        Assert.Contains("À propos de Torrent Client", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã€ propos de Torrent Client", content, StringComparison.Ordinal);
        Assert.Contains("Version du fichier", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RussianResourceFile_DoesNotContainMojibake()
    {
        var content = ReadResource("AppResources.ru.resx");

        Assert.Contains("Остановить", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ÐžÑ", content, StringComparison.Ordinal);
        Assert.Contains("Существующие файлы состояния сохранены", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Ð˜Ñ", content, StringComparison.Ordinal);
        Assert.Contains("Версия файла", content, StringComparison.Ordinal);
        Assert.Contains("Сборка", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TurkishResourceFile_ContainsExpectedTranslations()
    {
        var content = ReadResource("AppResources.tr.resx");

        Assert.Contains("Mevcut durum dosyaları korundu", content, StringComparison.Ordinal);
        Assert.Contains("Dosya sürümü", content, StringComparison.Ordinal);
        Assert.Contains("Özel indirme klasörü", content, StringComparison.Ordinal);
        Assert.Contains("Evet", content, StringComparison.Ordinal);
    }

    [Fact]
    public void HindiResourceFile_ContainsExpectedTranslations()
    {
        var content = ReadResource("AppResources.hi.resx");

        Assert.Contains("मौजूदा स्थिति फ़ाइलें", content, StringComparison.Ordinal);
        Assert.Contains("फाइल संस्करण", content, StringComparison.Ordinal);
        Assert.Contains("कस्टम डाउनलोड फोल्डर", content, StringComparison.Ordinal);
        Assert.Contains("हाँ", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ArabicResourceFile_ContainsExpectedTranslations()
    {
        var content = ReadResource("AppResources.ar.resx");

        Assert.Contains("التنزيلات", content, StringComparison.Ordinal);
        Assert.Contains("الإعدادات", content, StringComparison.Ordinal);
        Assert.Contains("نعم", content, StringComparison.Ordinal);
        Assert.Contains("تم الاحتفاظ بملفات الحالة الموجودة", content, StringComparison.Ordinal);
    }

    private static string ReadResource(string fileName)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "TorrentFree.Core", "Resources", "Strings", fileName);

        return File.ReadAllText(path);
    }
}
