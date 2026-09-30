using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.UI.Windowing;
using TorrentFree.Models;
using TorrentFree.Services;
using TorrentFree.ViewModels;
using WinRT.Interop;

namespace TorrentFree;

public partial class App
{
    private AppWindow? _desktopAppWindow;
    private Window? _trackedDesktopWindow;
    private bool? _lastDesktopWasMaximized;
    private Task? _desktopShutdownTask;
    private bool _desktopShutdownComplete;

    partial void ConfigurePlatformWindow(Window window)
    {
        window.Created += OnDesktopWindowCreated;
        window.Destroying += OnDesktopWindowDestroying;
    }

    private void OnDesktopWindowCreated(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Created -= OnDesktopWindowCreated;
        TrackDesktopWindow(window);
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
            _desktopAppWindow.Closing += OnDesktopAppWindowClosing;
        }
    }

    private void UntrackDesktopWindow()
    {
        if (_desktopAppWindow is not null)
        {
            _desktopAppWindow.Changed -= OnDesktopAppWindowChanged;
            _desktopAppWindow.Closing -= OnDesktopAppWindowClosing;
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

    private async void OnDesktopAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_desktopShutdownComplete) return;
        var window = _trackedDesktopWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is null) return;
        args.Cancel = true;
        await CloseDesktopAsync(window, GetDesktopWindowMaximized(sender));
    }

    internal Task CloseForUpdateAsync()
    {
        var window = _trackedDesktopWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No active desktop window to close for the update.");
        return CloseDesktopAsync(window, GetDesktopWindowMaximized(_desktopAppWindow));
    }

    private Task CloseDesktopAsync(Microsoft.UI.Xaml.Window window, bool? desktopWasMaximized) =>
        _desktopShutdownTask ??= FlushAndCloseDesktopAsync(window, desktopWasMaximized);

    private async Task FlushAndCloseDesktopAsync(Microsoft.UI.Xaml.Window window, bool? desktopWasMaximized)
    {
        try
        {
            await FlushAndShutdownDesktopAsync(desktopWasMaximized)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Desktop transfer shutdown failed: {ex.Message}");
        }
        finally
        {
            _desktopShutdownComplete = true;
            try { window.Close(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Desktop window close failed: {ex.Message}");
            }
        }
    }

    private async Task FlushAndShutdownDesktopAsync(bool? desktopWasMaximized)
    {
        // Destroying cannot delay process exit. Flush the settings debounce while Closing
        // is still cancelled, before disposing the service which consumes those settings.
        try
        {
            if (MauiProgram.Services.GetService<SettingsViewModel>() is { } settings)
                await settings.FlushPendingSettingsAsync();
        }
        catch (Exception ex)
        {
            // A storage failure must not prevent the transfer engine from shutting down.
            System.Diagnostics.Debug.WriteLine($"Desktop settings flush failed: {ex.Message}");
        }

        await Task.WhenAll(
            PersistDesktopWindowStateAsync(desktopWasMaximized),
            _torrentService.DisposeAsync().AsTask());
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
        try
        {
            await AppSettingsPersistence.RunExclusiveAsync(async () =>
            {
                if (_lastDesktopWasMaximized == desktopWasMaximized)
                {
                    return;
                }

                await _storageService.UpdateDesktopWindowStateAsync(desktopWasMaximized);
                _lastDesktopWasMaximized = desktopWasMaximized;
            });
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
