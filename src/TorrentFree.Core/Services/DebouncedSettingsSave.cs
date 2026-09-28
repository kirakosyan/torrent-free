namespace TorrentFree.Services;

/// <summary>Coalesces edits and lets navigation or suspension flush the latest change.</summary>
internal sealed class DebouncedSettingsSave(Func<Task> save, TimeSpan delay)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _pending;
    private long _requestedVersion;
    private long _savedVersion;

    public async Task ScheduleAsync()
    {
        CancellationTokenSource pending;
        lock (_sync)
        {
            _pending?.Cancel();
            _pending = pending = new CancellationTokenSource();
            _requestedVersion++;
        }

        try
        {
            try { await Task.Delay(delay, pending.Token); }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { return; }
            await SavePendingAsync();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pending, pending)) _pending = null;
                pending.Dispose();
            }
        }
    }

    public Task FlushAsync()
    {
        lock (_sync) _pending?.Cancel();
        return SavePendingAsync();
    }

    private async Task SavePendingAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            while (true)
            {
                long version;
                lock (_sync)
                {
                    version = _requestedVersion;
                    if (version == _savedVersion) return;
                }

                await save();
                // Mark only successful writes as saved, keeping failed edits available for retry.
                lock (_sync) _savedVersion = version;
            }
        }
        finally { _saveLock.Release(); }
    }
}
