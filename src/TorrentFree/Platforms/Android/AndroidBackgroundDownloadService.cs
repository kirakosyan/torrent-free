using Android.Content;
using TorrentFree.Services;

namespace TorrentFree;

/// <summary>
/// Android implementation that keeps downloads alive using a foreground service.
/// </summary>
public sealed class AndroidBackgroundDownloadService : IBackgroundDownloadService
{
    public void Start()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(DownloadForegroundService));

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
#pragma warning disable CA1416
                context.StartForegroundService(intent);
#pragma warning restore CA1416
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start foreground service: {ex}");
        }
    }

    public void Stop()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(DownloadForegroundService));
            context.StopService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to stop foreground service: {ex}");
        }
    }
}
