namespace TorrentFree.Services;

/// <summary>Reports whether the current connection permits Wi-Fi-only transfers.</summary>
public interface ITransferNetworkMonitor
{
    bool IsWifiConnected { get; }
    event EventHandler? Changed;
}
