using System.Text.Json;
using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Copies persisted state from a legacy location without replacing state which
/// already contains user data.
/// </summary>
internal static class StorageMigration
{
    private const string CompletionMarkerSuffix = ".msix-migration-v2.complete";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyDictionary<string, JsonElement> DefaultSettings =
        CreateDefaultSettings();

    // Window state is written during a normal shutdown even when the new storage
    // file has no queue or user-configured settings. It must not make that
    // placeholder win over meaningful state in the legacy package sandbox.
    private static readonly HashSet<string> IncidentalSettings =
        new(StringComparer.OrdinalIgnoreCase) { "desktopWasMaximized" };

    public static bool TryMigrate(string destinationFile, IEnumerable<string> sourceFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        var completionMarker = destinationFile + CompletionMarkerSuffix;
        if (File.Exists(completionMarker))
        {
            return false;
        }

        if (ContainsUserState(destinationFile))
        {
            WriteCompletionMarker(completionMarker);
            return false;
        }

        var sourceFile = sourceFiles
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !PathsReferToSameFile(path, destinationFile))
            .Where(ContainsUserState)
            .OrderByDescending(GetLastWriteTimeUtcSafely)
            .FirstOrDefault();

        if (sourceFile is null)
        {
            return false;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var tempFile = destinationFile + ".migration.tmp";
        var moved = false;
        try
        {
            File.Copy(sourceFile, tempFile, overwrite: true);
            File.Move(tempFile, destinationFile, overwrite: true);
            moved = true;
            WriteCompletionMarker(completionMarker);
            return true;
        }
        finally
        {
            if (!moved)
            {
                TryDelete(tempFile);
            }
        }
    }

    private static void WriteCompletionMarker(string markerFile)
    {
        var markerDirectory = Path.GetDirectoryName(markerFile);
        if (!string.IsNullOrEmpty(markerDirectory))
        {
            Directory.CreateDirectory(markerDirectory);
        }

        var tempFile = markerFile + ".tmp";
        var moved = false;
        try
        {
            File.WriteAllText(tempFile, $"completedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
            File.Move(tempFile, markerFile, overwrite: true);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                TryDelete(tempFile);
            }
        }
    }

    internal static bool ContainsUserState(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetProperty(document.RootElement, "torrents", out var torrents) &&
                torrents.ValueKind == JsonValueKind.Array &&
                torrents.GetArrayLength() > 0)
            {
                return true;
            }

            return TryGetProperty(document.RootElement, "settings", out var settings) &&
                SettingsContainUserState(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to inspect persisted state at {path}: {ex.Message}");
            return false;
        }
    }

    private static bool SettingsContainUserState(JsonElement settings)
    {
        if (settings.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        foreach (var property in settings.EnumerateObject())
        {
            if (IncidentalSettings.Contains(property.Name))
            {
                continue;
            }

            if (!DefaultSettings.TryGetValue(property.Name, out var defaultValue) ||
                !JsonElement.DeepEquals(property.Value, defaultValue))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateDefaultSettings()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new AppSettings(), JsonOptions));
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => property.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool PathsReferToSameFile(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static DateTime GetLastWriteTimeUtcSafely(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to remove migration temporary file {path}: {ex.Message}");
        }
    }
}
