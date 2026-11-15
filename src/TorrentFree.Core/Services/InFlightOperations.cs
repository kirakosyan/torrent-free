namespace TorrentFree.Services;

/// <summary>Shares an operation until it really completes, even if a caller stops waiting.</summary>
internal sealed class InFlightOperations<TKey> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Task> _tasks = new();

    public Task RunAsync(TKey key, Func<Task> operation)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_tasks.TryGetValue(key, out var existing)) return existing;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _tasks.Add(key, completion.Task);
        }
        _ = CompleteAsync(key, operation, completion);
        return completion.Task;
    }

    public Task WaitAsync(TKey key, CancellationToken cancellationToken)
    {
        lock (_gate)
            return _tasks.TryGetValue(key, out var task) ? task.WaitAsync(cancellationToken) : Task.CompletedTask;
    }

    private async Task CompleteAsync(TKey key, Func<Task> operation, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try { await operation().ConfigureAwait(false); }
        catch (Exception ex) { failure = ex; }
        lock (_gate)
        {
            _tasks.Remove(key);
            if (failure is null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }
    }
}
