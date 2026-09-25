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
    private const int CopyBufferSize = 1024 * 1024;
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
            var sourceInfo = new FileInfo(sourcePath);
            var sourceLength = sourceInfo.Length;
            var sourceWriteTicks = sourceInfo.LastWriteTimeUtc.Ticks;
            await using var source = File.OpenRead(sourcePath);
            var existing = await ReadManifestAsync(manifestPath);
            if (existing is not null)
            {
                // Downloads can be many gigabytes. An unchanged source (same length and write
                // time) keeps its recorded digest, so only the destination is read to verify it.
                var digest = existing.SourceLength == sourceLength && existing.SourceWriteTicks == sourceWriteTicks
                    ? existing.Digest
                    : Convert.ToHexString(await SHA256.HashDataAsync(source));
                if (existing.Digest == digest && await DestinationMatchesAsync(existing.Id, digest))
                    return existing.Id;
                source.Position = 0;
            }

            var target = await store.CreateAsync(relativeDirectory, Path.GetFileName(sourcePath));
            string exportedDigest;
            try
            {
                // Hash while copying: a first export reads the source once.
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[CopyBufferSize];
                await using (target.Stream)
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await target.Stream.WriteAsync(buffer.AsMemory(0, read));
                    }
                }

                exportedDigest = Convert.ToHexString(hash.GetHashAndReset());
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
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(
                    new ExportRecord(target.Id, exportedDigest, sourceLength, sourceWriteTicks)));
                File.Move(tempPath, manifestPath, overwrite: true);
            }
            finally { File.Delete(tempPath); }
            return target.Id;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> DestinationMatchesAsync(string id, string digest)
    {
        try
        {
            await using var destination = await store.OpenReadAsync(id);
            return destination is not null && Convert.ToHexString(await SHA256.HashDataAsync(destination)) == digest;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Previous export is unavailable: {ex.Message}");
            return false;
        }
    }

    private static async Task<ExportRecord?> ReadManifestAsync(string path)
    {
        try { return JsonSerializer.Deserialize<ExportRecord>(await File.ReadAllTextAsync(path)); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    // Manifests written before the source stamp existed deserialize with null stamps.
    private sealed record ExportRecord(string Id, string Digest, long? SourceLength = null, long? SourceWriteTicks = null);
}
