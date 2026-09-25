using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.Core.View;
using Microsoft.Extensions.DependencyInjection;
using TorrentFree.Services;
using TorrentFree.ViewModels;

using AView = Android.Views.View;

namespace TorrentFree;

[Activity(Theme = "@style/TorrentFree.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "magnet")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataMimeType = "application/x-bittorrent")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ConfigureEdgeToEdge();
        base.OnCreate(savedInstanceState);

        // A recreated activity must not import the launch intent a second time.
        if (savedInstanceState is null)
        {
            HandleViewIntent(Intent);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleViewIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        MauiProgram.Services?.GetService<ITorrentService>()?.ResumeAfterBackgroundTimeout();
    }

    private void HandleViewIntent(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri)
        {
            return;
        }

        var viewModel = MauiProgram.Services?.GetService<MainViewModel>();
        if (viewModel is null)
        {
            return;
        }

        if (string.Equals(uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            var magnetLink = uri.ToString();
            if (!string.IsNullOrWhiteSpace(magnetLink))
            {
                MainThread.BeginInvokeOnMainThread(() => _ = viewModel.ImportMagnetLinkAsync(magnetLink));
            }

            return;
        }

        _ = ImportTorrentContentAsync(viewModel, uri);
    }

    private async Task ImportTorrentContentAsync(MainViewModel viewModel, Android.Net.Uri uri)
    {
        string fileName;
        byte[] content;
        try
        {
            fileName = QueryDisplayName(uri) ?? "download.torrent";
            await using var stream = ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("The .torrent file could not be opened.");
            content = await TorrentFileContentReader.ReadAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening shared .torrent failed: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
                viewModel.ErrorMessage = LocalizationResourceManager.Instance["ErrorImportTorrent"]);
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => viewModel.ImportTorrentContentAsync(fileName, content));
    }

    private string? QueryDisplayName(Android.Net.Uri uri)
    {
        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(uri.Path);
        }

        try
        {
            using var cursor = ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                return index >= 0 ? cursor.GetString(index) : null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read the shared file name: {ex.Message}");
        }

        return null;
    }

    private void ConfigureEdgeToEdge()
    {
        if (Window is null)
        {
            return;
        }

        WindowCompat.SetDecorFitsSystemWindows(Window, false);

        if (Window.DecorView is AView decorView)
        {
            ViewCompat.SetOnApplyWindowInsetsListener(decorView, new SystemBarsInsetsListener());
            ViewCompat.RequestApplyInsets(decorView);
        }
    }

    private sealed class SystemBarsInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(AView? view, WindowInsetsCompat? windowInsets)
        {
            if (view is null || windowInsets is null)
            {
                return windowInsets;
            }

            var insets = windowInsets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            if (insets is null)
            {
                return windowInsets;
            }

            view.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
            return WindowInsetsCompat.Consumed ?? windowInsets;
        }
    }
}
