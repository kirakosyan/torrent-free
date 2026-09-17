using System.Security.Cryptography;
using System.Text;

namespace TorrentFree.Services;

/// <summary>Reusable immutable copies, retained for delayed imports and expired after seven idle days.</summary>
public sealed class AudiobookHandoffCache(string root, Func<DateTime>? utcNow = null)
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Func<DateTime> utcNow = utcNow ?? (() => DateTime.UtcNow);
    private readonly string root = Path.GetFullPath(root);

    public async Task<string> StageAsync(string source, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(root);
            foreach (var old in Directory.EnumerateDirectories(root))
                if (!File.GetAttributes(old).HasFlag(FileAttributes.ReparsePoint)
                    && Directory.GetLastWriteTimeUtc(old) < utcNow().AddDays(-7)) DeleteBestEffort(old);

            var info = new FileInfo(source);
            var fingerprint = $"{info.FullName}\n{info.Length}\n{info.LastWriteTimeUtc.Ticks}";
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
            var directory = Path.Combine(root, key);
            var staged = Path.Combine(directory, info.Name);
            Directory.CreateDirectory(directory);
            Directory.SetLastWriteTimeUtc(directory, utcNow());
            if (File.Exists(staged) && new FileInfo(staged).Length == info.Length) return staged;
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var input = File.OpenRead(source))
                await using (var output = File.Create(temporary))
                    await input.CopyToAsync(output, cancellationToken);
                var after = new FileInfo(source);
                if (after.Length != info.Length || after.LastWriteTimeUtc != info.LastWriteTimeUtc)
                    throw new IOException("The audiobook changed while preparing the handoff.");
                File.Move(temporary, staged, overwrite: true);
                Directory.SetLastWriteTimeUtc(directory, utcNow());
                return staged;
            }
            catch
            {
                DeleteBestEffort(directory);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private static void DeleteBestEffort(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
