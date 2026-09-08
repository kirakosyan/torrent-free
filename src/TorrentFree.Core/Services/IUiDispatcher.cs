namespace TorrentFree.Services;

/// <summary>Dispatches observable model changes to the application's UI thread.</summary>
public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}
