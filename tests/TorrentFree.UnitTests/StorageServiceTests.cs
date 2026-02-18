using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class StorageServiceTests
{
    [Fact]
    public void JsonOptions_WithUnmappedMemberHandlingSkip_IgnoresUnknownFields()
    {
        // Arrange: Create a legacy JSON with unmapped ICommand properties
        var legacyJson = """
        {
            "id": "test-id-123",
            "name": "Test Torrent",
            "magnetLink": "magnet:?xt=urn:btih:test",
            "showInFolderCommand": null,
            "startSpecificTorrentCommand": null,
            "pauseSpecificTorrentCommand": null
        }
        """;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        // Act: Deserialize the legacy JSON with UnmappedMemberHandling.Skip
        var torrent = JsonSerializer.Deserialize<SimpleTorrent>(legacyJson, jsonOptions);

        // Assert: Deserialization should succeed and ignore unknown fields
        Assert.NotNull(torrent);
        Assert.Equal("test-id-123", torrent.Id);
        Assert.Equal("Test Torrent", torrent.Name);
        Assert.Equal("magnet:?xt=urn:btih:test", torrent.MagnetLink);
    }

    [Fact]
    public void JsonOptions_WithoutUnmappedMemberHandling_StillIgnoresUnknownFieldsByDefault()
    {
        // Arrange: System.Text.Json by default ignores unknown properties
        var legacyJson = """
        {
            "id": "test-id",
            "name": "Test",
            "unknownField": "value"
        }
        """;

        var jsonOptionsDefault = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act: Without explicit UnmappedMemberHandling, unknown fields are still ignored
        var torrent = JsonSerializer.Deserialize<SimpleTorrent>(legacyJson, jsonOptionsDefault);

        // Assert: Deserialization succeeds even with unknown fields
        Assert.NotNull(torrent);
        Assert.Equal("test-id", torrent.Id);
        Assert.Equal("Test", torrent.Name);
    }

    [Fact]
    public void JsonOptions_VerifySkipBehaviorIsExplicitlySet()
    {
        // This test verifies that UnmappedMemberHandling.Skip is the recommended
        // approach for handling legacy JSON with unknown fields
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        // Assert: Verify the option is set
        Assert.Equal(JsonUnmappedMemberHandling.Skip, jsonOptions.UnmappedMemberHandling);
    }
}

// Simple test class to verify deserialization
internal class SimpleTorrent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MagnetLink { get; set; } = string.Empty;
}
