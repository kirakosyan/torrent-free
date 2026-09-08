using System.Threading;

namespace TorrentFree.Services;

/// <summary>
/// Per-key async mutex. Entries are refcounted and removed from the backing dictionary
/// once nobody references them, so the key set does not grow without bound over the life
/// of a long-running process (torrent IDs are never reused).
/// </summary>
internal sealed class AsyncKeyedLocker : IDisposable
{
    private readonly Dictionary<string, Entry> _locks = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    public async ValueTask<Releaser> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var entry = Rent(key);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Return(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private Entry Rent(string key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_locks.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _locks[key] = entry;
            }

            entry.RefCount++;
            return entry;
        }
    }

    /// <summary>
    /// Drops this caller's reference to <paramref name="entry"/>. Once the last reference
    /// is gone, the entry is removed from the dictionary and its semaphore disposed.
    /// </summary>
    private void Return(string key, Entry entry)
    {
        lock (_gate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 && _locks.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _locks.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            // Existing holders and waiters retain their entries until Return. Disposing
            // their semaphores here could strand waiters or race with Release.
            _disposed = true;
        }
    }

    internal sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    internal sealed class Releaser : IDisposable, IAsyncDisposable
    {
        private AsyncKeyedLocker? _owner;
        private readonly string _key;
        private readonly Entry _entry;

        internal Releaser(AsyncKeyedLocker owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            try
            {
                _entry.Semaphore.Release();
            }
            finally
            {
                owner.Return(_key, _entry);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
