namespace TorrentFree.UnitTests;

internal sealed class ImmediateDispatcher : TorrentFree.Services.IUiDispatcher
{
    public static ImmediateDispatcher Instance { get; } = new();
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
