using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace TorrentFree;

[Service(Exported = false)]
public sealed class DownloadForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "torrentfree_downloads";
    private const string ChannelName = "Download activity";

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
#pragma warning disable CA1416
            StopForeground(StopForegroundFlags.Remove);
#pragma warning restore CA1416
        }
        else
        {
#pragma warning disable CA1422
            StopForeground(true);
#pragma warning restore CA1422
        }
        base.OnDestroy();
    }

    private Notification BuildNotification()
    {
        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("Torrent Free");
        builder.SetContentText("Downloads running in the background");
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetVisibility(NotificationCompat.VisibilityPublic);
        builder.SetPriority((int)NotificationPriority.Low);

        return builder.Build()!;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

#pragma warning disable CA1416
        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
        {
            Description = "Keeps torrent downloads running while the app is minimized"
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
#pragma warning restore CA1416
    }
}
