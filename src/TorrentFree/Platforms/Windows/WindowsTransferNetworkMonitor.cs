using Windows.Networking.Connectivity;

namespace TorrentFree.Services;

public sealed class WindowsTransferNetworkMonitor : ITransferNetworkMonitor, IDisposable
{
    public WindowsTransferNetworkMonitor() => NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;

    /// <summary>
    /// Wi-Fi-only exists to keep transfers off mobile data. On Windows the internet profile is
    /// often wired or a VPN, which have no Wi-Fi flag, so those qualify unless they are mobile
    /// broadband or metered. Metered Wi-Fi (including phone hotspots) is excluded too.
    /// </summary>
    public bool IsWifiConnected
    {
        get
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile is null || profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.InternetAccess)
                {
                    return false;
                }

                if (profile.IsWwanConnectionProfile)
                {
                    return false;
                }

                var cost = profile.GetConnectionCost();
                return cost is not null
                    && !cost.Roaming
                    && !cost.OverDataLimit
                    && cost.NetworkCostType is NetworkCostType.Unrestricted or NetworkCostType.Unknown;
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
