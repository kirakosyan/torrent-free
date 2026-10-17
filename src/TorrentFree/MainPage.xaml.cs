using System.Reflection;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using TorrentFree.Models;
using TorrentFree.Services;
using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class MainPage : ContentPage
{
    private const string GitHubUrl = "https://github.com/kirakosyan/torrent-free";
    private Window? _promptWindow;
    private bool _isPageVisible;
    private CancellationTokenSource? _notificationScrollCancellation;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        NotificationService.DownloadNotificationTapped += OnDownloadNotificationTapped;
    }

    private void OnDashboardLayoutChanged(object? sender, EventArgs e)
    {
        if (DashboardGrid is null || DashboardHeader is null || StorePrompts is null
            || MagnetInput is null || DownloadsHeading is null || DownloadControlsScroll is null
            || DashboardGrid.Height <= 0)
            return;

        // Preserve room for the download list as the window, banner, or keyboard changes size.
        var available = DashboardGrid.Height - DashboardHeader.Height - StorePrompts.Height
            - MagnetInput.Height - DownloadsHeading.Height - 36 - 120;
        var maximum = Math.Clamp(available, 56, 280);
        if (Math.Abs(DownloadControlsScroll.MaximumHeightRequest - maximum) > 1)
            DownloadControlsScroll.MaximumHeightRequest = maximum;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageVisible = true;

        if (BindingContext is MainViewModel vm)
        {
            await vm.InitializeCommand.ExecuteAsync(null);
            SelectPendingNotification(vm);
            if (!_isPageVisible) return;
            if (_promptWindow != Window)
            {
                DetachPromptWindow();
                _promptWindow = Window;
                if (_promptWindow is not null)
                {
                    _promptWindow.Resumed += OnPromptWindowResumed;
                    _promptWindow.Stopped += OnPromptWindowStopped;
                }
            }
            await SetPromptForegroundSafelyAsync(true);
        }
    }

    private void OnDownloadNotificationTapped()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Leave a cold-start tap pending until the first page has appeared.
                if (Shell.Current is not { } shell || Window is null) return;
                await shell.GoToAsync("//MainPage");
                if (BindingContext is MainViewModel viewModel)
                {
                    await viewModel.EnsureInitializedAsync();
                    SelectPendingNotification(viewModel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Opening completed download notification failed: {ex}");
            }
        });
    }

    private void SelectPendingNotification(MainViewModel viewModel)
    {
        // InitializeCommand reports errors in the UI rather than throwing. Keep a
        // cold-start tap queued until its torrent list has actually been restored.
        if (!viewModel.IsInitialized || !_isPageVisible) return;
        var id = NotificationService.TakePendingTorrentId();
        if (id is null) return;
        var torrent = viewModel.Torrents.FirstOrDefault(item => item.Id == id);
        if (torrent is null) return;
        viewModel.ShowDownloadsForNotification(torrent);
        _ = ScrollToNotificationSafelyAsync(viewModel, torrent);
    }

    private async Task ScrollToNotificationSafelyAsync(MainViewModel viewModel, TorrentItem torrent)
    {
        _notificationScrollCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _notificationScrollCancellation = cancellation;
        var reachedDownload = false;

        void OnSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(viewModel.SelectedTorrent, torrent) || viewModel.CanShowSelectedTorrentDetails)
                cancellation.Cancel();
        }

        void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
        {
            var index = viewModel.DisplayTorrents.IndexOf(torrent);
            reachedDownload |= index >= 0 && index >= e.FirstVisibleItemIndex && index <= e.LastVisibleItemIndex;
        }

        try
        {
            viewModel.PropertyChanged += OnSelectionChanged;
            DownloadsList.Scrolled += OnScrolled;
            // ScrollTo can be dropped while a hidden native list is being arranged.
            // Retry briefly until Scrolled confirms the target is visible.
            for (var attempt = 0; attempt < 20 && !reachedDownload; attempt++)
            {
                await Task.Delay(100, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (reachedDownload) return;
                if (!_isPageVisible || !ReferenceEquals(viewModel.SelectedTorrent, torrent)
                    || viewModel.CanShowSelectedTorrentDetails)
                    return;
                if (!DownloadsList.IsLoaded || DownloadsList.Height <= 0
                    || !viewModel.DisplayTorrents.Contains(torrent))
                    continue;

                try
                {
                    DownloadsList.ScrollTo(torrent, position: ScrollToPosition.Center, animate: false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Scrolling to completed download failed: {ex}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening completed download in list failed: {ex}");
        }
        finally
        {
            viewModel.PropertyChanged -= OnSelectionChanged;
            DownloadsList.Scrolled -= OnScrolled;
            if (ReferenceEquals(_notificationScrollCancellation, cancellation))
                _notificationScrollCancellation = null;
        }
    }

    protected override void OnDisappearing()
    {
        _isPageVisible = false;
        _notificationScrollCancellation?.Cancel();
        DetachPromptWindow();
        _ = SetPromptForegroundSafelyAsync(false);
        base.OnDisappearing();
    }

    private void DetachPromptWindow()
    {
        if (_promptWindow is not null)
        {
            _promptWindow.Resumed -= OnPromptWindowResumed;
            _promptWindow.Stopped -= OnPromptWindowStopped;
            _promptWindow = null;
        }
    }

    private async void OnPromptWindowResumed(object? sender, EventArgs e)
    {
        if (_isPageVisible)
            await SetPromptForegroundSafelyAsync(true);
    }

    private async void OnPromptWindowStopped(object? sender, EventArgs e)
    {
        await SetPromptForegroundSafelyAsync(false);
    }

    private async Task SetPromptForegroundSafelyAsync(bool foreground)
    {
        try
        {
            if (BindingContext is MainViewModel vm)
                await vm.Prompts.SetForegroundAsync(foreground);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Store prompt lifecycle failed: {ex.Message}");
        }
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        try
        {
            var aboutMessage = AboutDialogMessageBuilder.Build(
                LocalizationResourceManager.Instance["AboutMessage"],
                LocalizationResourceManager.Instance["AboutVersionLabel"],
                TryGetAppVersion(),
                LocalizationResourceManager.Instance["AboutBuildLabel"],
                TryGetBuildNumber(),
                LocalizationResourceManager.Instance["AboutFileVersionLabel"],
                TryGetFileVersion(),
                LocalizationResourceManager.Instance["AboutSourceLabel"],
                GitHubUrl,
                LocalizationResourceManager.Instance["AboutUnavailable"]);

            var openSources = await DisplayAlertAsync(
                LocalizationResourceManager.Instance["AboutTitle"],
                aboutMessage,
                LocalizationResourceManager.Instance["OpenSource"],
                LocalizationResourceManager.Instance["OK"]);

            if (openSources)
            {
                await TryOpenSourceUrlAsync();
            }
        }
        catch (Exception ex)
        {
            // async void: an unhandled exception here would crash the app.
            System.Diagnostics.Debug.WriteLine($"About dialog error: {ex}");
        }
    }

    private static async Task TryOpenSourceUrlAsync()
    {
        var sourceUri = new Uri(GitHubUrl);

        try
        {
            await Browser.Default.OpenAsync(sourceUri, BrowserLaunchMode.External);
            return;
        }
        catch (FeatureNotSupportedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"External browser is not supported: {ex}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open source URL in external browser: {ex}");
        }

        try
        {
            await Launcher.Default.OpenAsync(sourceUri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open source URL: {ex}");
        }
    }

    private static string? TryGetAppVersion()
    {
        try
        {
            var assembly = GetAppAssembly();
            return GetAssemblyMetadata(assembly, "DisplayVersion")
                ?? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
                ?? AppInfo.Current.VersionString;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBuildNumber()
    {
        try
        {
            var assembly = GetAppAssembly();
            return GetAssemblyMetadata(assembly, "BuildNumber")
                ?? AppInfo.Current.BuildString;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetFileVersion()
    {
        try
        {
            var assembly = GetAppAssembly();
            return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? assembly.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static Assembly GetAppAssembly() => Assembly.GetEntryAssembly() ?? typeof(App).Assembly;

    private static string? GetAssemblyMetadata(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
    }
}
