namespace TorrentFree.Models;

/// <summary>
/// Represents persisted app settings.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Global download limit in KB/s (0 = unlimited).
    /// </summary>
    public int GlobalDownloadLimitKbps { get; set; }

    /// <summary>
    /// Global upload limit in KB/s (0 = unlimited).
    /// </summary>
    public int GlobalUploadLimitKbps { get; set; }

    /// <summary>
    /// Max concurrent active downloads (0 = unlimited).
    /// </summary>
    public int MaxActiveDownloads { get; set; } = 2;

    /// <summary>
    /// Max concurrent active seeds (0 = unlimited).
    /// </summary>
    public int MaxActiveSeeds { get; set; } = 2;

    /// <summary>
    /// Global max seed ratio (0 = unlimited).
    /// </summary>
    public double GlobalMaxSeedRatio { get; set; }

    /// <summary>
    /// Global max seed time in minutes (0 = unlimited).
    /// </summary>
    public int GlobalMaxSeedMinutes { get; set; }

    /// <summary>
    /// When enabled, downloading torrents are shown on top.
    /// </summary>
    public bool SortByStatus { get; set; }

    /// <summary>
    /// When enabled, .torrent imports download next to the source .torrent file.
    /// </summary>
    public bool DownloadToTorrentFolder { get; set; } = true;

    /// <summary>
    /// Optional custom download folder used when <see cref="DownloadToTorrentFolder"/> is disabled.
    /// </summary>
    public string SpecificDownloadFolder { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if SOCKS5 proxy is enabled.
    /// </summary>
    public bool ProxyEnabled { get; set; }

    /// <summary>
    /// SOCKS5 proxy host address.
    /// </summary>
    public string ProxyHost { get; set; } = string.Empty;

    /// <summary>
    /// SOCKS5 proxy port (1-65535).
    /// </summary>
    public int ProxyPort { get; set; } = 1080;

    /// <summary>
    /// SOCKS5 proxy username (optional).
    /// </summary>
    public string ProxyUsername { get; set; } = string.Empty;

    /// <summary>
    /// SOCKS5 proxy password (optional).
    /// </summary>
    public string ProxyPassword { get; set; } = string.Empty;

    /// <summary>
    /// User-selected language code (e.g. "en", "es", "fr", "ru").
    /// Null or empty means follow system language.
    /// </summary>
    public string? Language { get; set; }
}