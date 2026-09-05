using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TorrentFree.Services;

public sealed record ExportWriteTarget(string Id, Stream Stream);

/// <summary>The platform creates a new destination, never replacing another export.</summary>
public interface IDownloadExportStore
{
    Task<Stream?> OpenReadAsync(string id);
    Task<ExportWriteTarget> CreateAsync(string relativeDirectory, string fileName);
    Task CompleteAsync(string id);
    Task AbortAsync(string id);
}

/// <summary>Tracks export ownership and verifies content before reusing a destination.</summary>
public sealed class DownloadExportService(string stateDirectory, IDownloadExportStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> ExportAsync(string ownerId, string sourcePath, string relativeDirectory)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(stateDirectory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new[] { ownerId, relativeDirectory, Path.GetFileName(sourcePath) }))));
            var manifestPath = Path.Combine(stateDirectory, key + ".json");
            await using var source = File.OpenRead(sourcePath);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(source));
            var existing = await ReadManifestAsync(manifestPath);
            if (existing is not null && existing.Digest == digest)
            {
                try
                {
                    await using var destination = await store.OpenReadAsync(existing.Id);
                    if (destination is not null && Convert.ToHexString(await SHA256.HashDataAsync(destination)) == digest)
                        return existing.Id;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Previous export is unavailable: {ex.Message}"); }
            }

            source.Position = 0;
            var target = await store.CreateAsync(relativeDirectory, Path.GetFileName(sourcePath));
            try
            {
                await using (target.Stream)
                    await source.CopyToAsync(target.Stream);
                await store.CompleteAsync(target.Id);
            }
            catch
            {
                try { await store.AbortAsync(target.Id); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Incomplete export cleanup failed: {ex.Message}"); }
                throw;
            }

            // A manifest failure must not remove the completed user-visible export.
            var tempPath = manifestPath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(new ExportRecord(target.Id, digest)));
                File.Move(tempPath, manifestPath, overwrite: true);
            }
            finally { File.Delete(tempPath); }
            return target.Id;
        }
        finally { _gate.Release(); }
    }

    private static async Task<ExportRecord?> ReadManifestAsync(string path)
    {
        try { return JsonSerializer.Deserialize<ExportRecord>(await File.ReadAllTextAsync(path)); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private sealed record ExportRecord(string Id, string Digest);
}
