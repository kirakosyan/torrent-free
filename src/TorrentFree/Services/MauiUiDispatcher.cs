namespace TorrentFree.Services;

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
}
