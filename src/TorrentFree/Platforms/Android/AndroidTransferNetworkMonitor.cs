using Android.Content;
using Android.Net;
using Microsoft.Maui.Networking;

namespace TorrentFree.Services;

/// <summary>Checks the default route, so a secondary Wi-Fi connection cannot permit cellular transfers.</summary>
public sealed class AndroidTransferNetworkMonitor : ITransferNetworkMonitor, IDisposable
{
    private readonly ConnectivityManager? _manager;
    private readonly DefaultNetworkCallback? _callback;
    private volatile bool _wifiConnected;
    private volatile bool _disposed;

    public AndroidTransferNetworkMonitor()
    {
        _manager = Android.App.Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        _wifiConnected = ReadDefaultWifi();
        if (_manager is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _callback = new DefaultNetworkCallback(this);
            _manager.RegisterDefaultNetworkCallback(_callback);
        }
        else
        {
            // Android 8 guarantees an ordered capabilities callback immediately after
            // OnAvailable. Earlier versions use the connectivity event and a snapshot.
            Connectivity.Current.ConnectivityChanged += OnLegacyConnectivityChanged;
        }
    }

    public bool IsWifiConnected => _wifiConnected;
    public event EventHandler? Changed;

    private bool ReadDefaultWifi()
    {
        using var network = _manager?.ActiveNetwork;
        using var capabilities = network is null ? null : _manager?.GetNetworkCapabilities(network);
        return IsWifi(capabilities);
    }

    private static bool IsWifi(NetworkCapabilities? capabilities) =>
        capabilities?.HasTransport(TransportType.Wifi) == true
        && !capabilities.HasTransport(TransportType.Cellular)
        && capabilities.HasCapability(NetCapability.Internet)
        && capabilities.HasCapability(NetCapability.Validated);

    private void Update(bool wifi)
    {
        if (_disposed || _wifiConnected == wifi) return;
        _wifiConnected = wifi;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLegacyConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) => Update(ReadDefaultWifi());

    public void Dispose()
    {
        _disposed = true;
        if (_callback is not null)
        {
            _manager?.UnregisterNetworkCallback(_callback);
            _callback.Dispose();
        }
        else Connectivity.Current.ConnectivityChanged -= OnLegacyConnectivityChanged;
    }

    private sealed class DefaultNetworkCallback(AndroidTransferNetworkMonitor owner) : ConnectivityManager.NetworkCallback
    {
        private Network? _current;

        public override void OnAvailable(Network network)
        {
            _current = network;
            // OnAvailable does not describe the transport. Wait for the immediately
            // following capabilities callback rather than publishing a false Wi-Fi loss
            // during handover. Android warns against synchronous capability queries here.
        }

        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities capabilities)
        {
            if (network.Equals(_current)) owner.Update(IsWifi(capabilities));
        }

        public override void OnLost(Network network)
        {
            if (!network.Equals(_current)) return;
            _current = null;
            owner.Update(false);
        }

        public override void OnUnavailable() => owner.Update(false);
    }
}
