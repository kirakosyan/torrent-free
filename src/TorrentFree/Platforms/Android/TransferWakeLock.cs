using Android.Content;
using Android.OS;

namespace TorrentFree;

/// <summary>
/// Optional partial wake lock held only while the transfer foreground service runs and the user
/// enabled "keep device awake". A foreground service alone does not stop the CPU from sleeping
/// with the screen off, which stalls peer connections.
/// </summary>
internal static class TransferWakeLock
{
    private const string Tag = "TorrentFree:transfers";
    private static readonly object Gate = new();
    private static PowerManager.WakeLock? _wakeLock;
    private static bool _enabled;
    private static bool _serviceActive;

    public static void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            _enabled = enabled;
            Apply();
        }
    }

    public static void SetServiceActive(bool active)
    {
        lock (Gate)
        {
            _serviceActive = active;
            Apply();
        }
    }

    private static void Apply()
    {
        try
        {
            if (_enabled && _serviceActive)
            {
                if (_wakeLock?.IsHeld == true)
                {
                    return;
                }

                if (_wakeLock is null)
                {
                    var powerManager = (PowerManager?)Android.App.Application.Context.GetSystemService(Context.PowerService);
                    _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, Tag);
                    _wakeLock?.SetReferenceCounted(false);
                }

                // Released when transfers stop, the setting is disabled, or the service ends.
                _wakeLock?.Acquire();
            }
            else if (_wakeLock?.IsHeld == true)
            {
                _wakeLock.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Transfer wake lock update failed: {ex}");
        }
    }
}
