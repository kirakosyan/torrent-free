using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

using AView = Android.Views.View;

namespace TorrentFree;

[Activity(Theme = "@style/TorrentFree.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ConfigureEdgeToEdge();
        base.OnCreate(savedInstanceState);
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
