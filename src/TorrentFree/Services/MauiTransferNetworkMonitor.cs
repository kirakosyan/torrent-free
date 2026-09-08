using Microsoft.Maui.Networking;

namespace TorrentFree.Services;

public sealed class MauiTransferNetworkMonitor : ITransferNetworkMonitor, IDisposable
{
    private readonly IConnectivity _connectivity;

    public MauiTransferNetworkMonitor()
    {
        _connectivity = Connectivity.Current;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsWifiConnected
    {
        get
        {
            try
            {
                return _connectivity.NetworkAccess == NetworkAccess.Internet
                    && _connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to determine Wi-Fi connectivity: {ex.Message}");
                return false;
            }
        }
    }

    public event EventHandler? Changed;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _connectivity.ConnectivityChanged -= OnConnectivityChanged;
}
