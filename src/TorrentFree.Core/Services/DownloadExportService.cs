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
public sealed class DownloadExportService(string stateDirectory, IDownloadExportStore store, Func<long?>? availableBytes = null)
{
    private const int CopyBufferSize = 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> ExportAsync(string ownerId, string sourcePath, string relativeDirectory,
        CancellationToken cancellationToken = default, IProgress<DownloadExportProgress>? progress = null)
    {
        await _gate.WaitAsync(cancellationToken);
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
                    : await HashAsync(source, sourceLength, cancellationToken, progress);
                if (existing.Digest == digest && await DestinationMatchesAsync(existing.Id, digest, sourceLength, cancellationToken, progress))
                    return existing.Id;
                source.Position = 0;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (availableBytes?.Invoke() is { } free && free < sourceLength) throw new DownloadExportSpaceException();
            var target = await store.CreateAsync(relativeDirectory, Path.GetFileName(sourcePath));
            string exportedDigest;
            try
            {
                // Hash while copying: a first export reads the source once.
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[CopyBufferSize];
                long copied = 0;
                progress?.Report(new(DownloadExportPhase.Copying, 0, sourceLength));
                await using (target.Stream)
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await target.Stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        copied += read;
                        progress?.Report(new(DownloadExportPhase.Copying, copied, sourceLength));
                    }
                }

                exportedDigest = Convert.ToHexString(hash.GetHashAndReset());
                cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<bool> DestinationMatchesAsync(string id, string digest, long length,
        CancellationToken cancellationToken, IProgress<DownloadExportProgress>? progress)
    {
        try
        {
            await using var destination = await store.OpenReadAsync(id);
            return destination is not null && await HashAsync(destination, length, cancellationToken, progress) == digest;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Previous export is unavailable: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> HashAsync(Stream stream, long length, CancellationToken cancellationToken,
        IProgress<DownloadExportProgress>? progress)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSize];
        long completed = 0;
        progress?.Report(new(DownloadExportPhase.Verifying, 0, length));
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            completed += read;
            progress?.Report(new(DownloadExportPhase.Verifying, completed, length));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<ExportRecord?> ReadManifestAsync(string path)
    {
        try { return JsonSerializer.Deserialize<ExportRecord>(await File.ReadAllTextAsync(path)); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    // Manifests written before the source stamp existed deserialize with null stamps.
    private sealed record ExportRecord(string Id, string Digest, long? SourceLength = null, long? SourceWriteTicks = null);
}
