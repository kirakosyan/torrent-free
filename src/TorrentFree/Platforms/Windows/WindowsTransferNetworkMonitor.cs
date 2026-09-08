using Windows.Networking.Connectivity;

namespace TorrentFree.Services;

public sealed class WindowsTransferNetworkMonitor : ITransferNetworkMonitor, IDisposable
{
    public WindowsTransferNetworkMonitor() => NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;

    public bool IsWifiConnected
    {
        get
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return profile?.IsWlanConnectionProfile == true
                    && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to determine default Wi-Fi connection: {ex.Message}");
                return false;
            }
        }
    }

    public event EventHandler? Changed;

    private void OnNetworkStatusChanged(object sender) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
}
