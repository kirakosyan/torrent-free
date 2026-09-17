using System.Collections.Concurrent;

namespace TorrentFree.Services;

/// <summary>Coalesces completion notifications and limits background disk walks.</summary>
public sealed class AudiobookAvailabilityCache(Func<string, bool>? scan = null)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> results = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim scans = new(2);
    private readonly Func<string, bool> scan = scan ?? (path => AudiobookFiles.Enumerate(path).Any());

    public Task<bool> HasAudioAsync(string path) => results.GetOrAdd(path,
        key => new Lazy<Task<bool>>(() => ScanAsync(key))).Value;

    public void Invalidate(string path) => results.TryRemove(path, out _);

    public bool IsCurrent(string path, Task<bool> scanTask) =>
        results.TryGetValue(path, out var result) && result.IsValueCreated && ReferenceEquals(result.Value, scanTask);

    private async Task<bool> ScanAsync(string path)
    {
        await scans.WaitAsync();
        try { return await Task.Run(() => scan(path)); }
        finally { scans.Release(); }
    }
}
