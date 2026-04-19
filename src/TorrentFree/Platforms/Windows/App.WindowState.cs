using Microsoft.Maui.Controls;
using Microsoft.UI.Windowing;
using TorrentFree.Models;
using WinRT.Interop;

namespace TorrentFree;

public partial class App
{
    private AppWindow? _desktopAppWindow;
    private Window? _trackedDesktopWindow;
    private bool? _lastDesktopWasMaximized;

    partial void ConfigurePlatformWindow(Window window)
    {
        window.Destroying += OnDesktopWindowDestroying;
    }

    partial void ApplyPlatformWindowSettings(Window window, AppSettings settings)
    {
        TrackDesktopWindow(window);
        _lastDesktopWasMaximized = settings.DesktopWasMaximized;

        if (settings.DesktopWasMaximized != true)
        {
            return;
        }

        if (TryGetDesktopPresenter(window) is { State: not OverlappedPresenterState.Maximized } presenter)
        {
            presenter.Maximize();
        }
    }

    private void TrackDesktopWindow(Window window)
    {
        if (ReferenceEquals(_trackedDesktopWindow, window))
        {
            return;
        }

        UntrackDesktopWindow();

        _trackedDesktopWindow = window;
        _desktopAppWindow = TryGetDesktopAppWindow(window);
        if (_desktopAppWindow is not null)
        {
            _desktopAppWindow.Changed += OnDesktopAppWindowChanged;
        }
    }

    private void UntrackDesktopWindow()
    {
        if (_desktopAppWindow is not null)
        {
            _desktopAppWindow.Changed -= OnDesktopAppWindowChanged;
        }

        _desktopAppWindow = null;
        _trackedDesktopWindow = null;
    }

    private void OnDesktopAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange && !args.DidSizeChange)
        {
            return;
        }

        _ = PersistDesktopWindowStateAsync(GetDesktopWindowMaximized(sender));
    }

    private void OnDesktopWindowDestroying(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Destroying -= OnDesktopWindowDestroying;

        if (!ReferenceEquals(_trackedDesktopWindow, window))
        {
            return;
        }

        _ = PersistDesktopWindowStateAsync(GetDesktopWindowMaximized(_desktopAppWindow));
        UntrackDesktopWindow();
    }

    private async Task PersistDesktopWindowStateAsync(bool? desktopWasMaximized)
    {
        if (_lastDesktopWasMaximized == desktopWasMaximized)
        {
            return;
        }

        try
        {
            await _storageService.UpdateDesktopWindowStateAsync(desktopWasMaximized);
            _lastDesktopWasMaximized = desktopWasMaximized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to persist desktop window state: {ex.Message}");
        }
    }

    private static bool? GetDesktopWindowMaximized(AppWindow? appWindow)
    {
        return appWindow?.Presenter is OverlappedPresenter presenter
            ? presenter.State == OverlappedPresenterState.Maximized
            : null;
    }

    private static OverlappedPresenter? TryGetDesktopPresenter(Window window)
    {
        return TryGetDesktopAppWindow(window)?.Presenter as OverlappedPresenter;
    }

    private static AppWindow? TryGetDesktopAppWindow(Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
        {
            return null;
        }

        var windowHandle = WindowNative.GetWindowHandle(platformWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        return AppWindow.GetFromWindowId(windowId);
    }
}
