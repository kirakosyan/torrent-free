namespace TorrentFree.Services;

/// <summary>Runs foreground work after the native activation callback has unwound.</summary>
internal sealed class DeferredForegroundUpdates(Action<Action> post, Func<bool, Task> apply)
{
    private int _generation;

    public void Request(bool foreground)
    {
        var generation = ++_generation;
        post(() =>
        {
            if (generation == _generation) _ = apply(foreground);
        });
    }
}
