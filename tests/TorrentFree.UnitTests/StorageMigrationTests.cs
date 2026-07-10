using System.Text.Json;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StorageMigrationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "TorrentFree.UnitTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryMigrate_ReplacesEmptyDestinationWithLegacyTorrentState()
    {
        var source = Path.Combine(_testDirectory, "legacy", "torrents.json");
        var destination = Path.Combine(_testDirectory, "current", "torrents.json");
        WriteStorageFile(source, torrentsJson: "[{\"id\":\"legacy-id\"}]");
        WriteStorageFile(destination);

        var migrated = StorageMigration.TryMigrate(destination, new[] { source });

        Assert.True(migrated);
        Assert.Contains("legacy-id", File.ReadAllText(destination), StringComparison.Ordinal);
    }

    [Fact]
    public void TryMigrate_ReplacesEmptyDestinationWithLegacySettings()
    {
        var source = Path.Combine(_testDirectory, "legacy", "torrents.json");
        var destination = Path.Combine(_testDirectory, "current", "torrents.json");
        WriteStorageFile(source, settings: new AppSettings { GlobalDownloadLimitKbps = 512 });
        WriteStorageFile(destination);

        var migrated = StorageMigration.TryMigrate(destination, new[] { source });

        Assert.True(migrated);
        using var document = JsonDocument.Parse(File.ReadAllText(destination));
        Assert.Equal(512, document.RootElement.GetProperty("settings").GetProperty("globalDownloadLimitKbps").GetInt32());
    }

    [Fact]
    public void TryMigrate_ReplacesDestinationContainingOnlyShutdownWindowState()
    {
        var source = Path.Combine(_testDirectory, "legacy", "torrents.json");
        var destination = Path.Combine(_testDirectory, "current", "torrents.json");
        WriteStorageFile(source, torrentsJson: "[{\"id\":\"legacy-id\"}]");
        WriteStorageFile(destination, settings: new AppSettings { DesktopWasMaximized = true });

        var migrated = StorageMigration.TryMigrate(destination, new[] { source });

        Assert.True(migrated);
        Assert.Contains("legacy-id", File.ReadAllText(destination), StringComparison.Ordinal);
    }

    [Fact]
    public void TryMigrate_DoesNotOverwriteMeaningfulDestination()
    {
        var source = Path.Combine(_testDirectory, "legacy", "torrents.json");
        var destination = Path.Combine(_testDirectory, "current", "torrents.json");
        WriteStorageFile(source, torrentsJson: "[{\"id\":\"legacy-id\"}]");
        WriteStorageFile(destination, torrentsJson: "[{\"id\":\"current-id\"}]");

        var migrated = StorageMigration.TryMigrate(destination, new[] { source });

        Assert.False(migrated);
        var contents = File.ReadAllText(destination);
        Assert.Contains("current-id", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-id", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMigrate_DoesNotResurrectLegacyStateAfterQueueIsCleared()
    {
        var source = Path.Combine(_testDirectory, "legacy", "torrents.json");
        var destination = Path.Combine(_testDirectory, "current", "torrents.json");
        WriteStorageFile(source, torrentsJson: "[{\"id\":\"legacy-id\"}]");

        Assert.True(StorageMigration.TryMigrate(destination, new[] { source }));

        WriteStorageFile(destination);
        var migratedAgain = StorageMigration.TryMigrate(destination, new[] { source });

        Assert.False(migratedAgain);
        Assert.DoesNotContain("legacy-id", File.ReadAllText(destination), StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsUserState_TreatsDefaultSettingsAsEmpty()
    {
        var path = Path.Combine(_testDirectory, "torrents.json");
        WriteStorageFile(path);

        Assert.False(StorageMigration.ContainsUserState(path));
    }

    private static void WriteStorageFile(
        string path,
        string torrentsJson = "[]",
        AppSettings? settings = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settingsJson = JsonSerializer.Serialize(
            settings ?? new AppSettings(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(path, $$"""
            {
              "version": "1.0",
              "lastUpdated": "{{DateTime.UtcNow:O}}",
              "torrents": {{torrentsJson}},
              "settings": {{settingsJson}}
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
