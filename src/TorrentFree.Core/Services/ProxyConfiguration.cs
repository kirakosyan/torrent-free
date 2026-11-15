namespace TorrentFree.Services;

/// <summary>A committed snapshot; editing a form must never mutate the running proxy.</summary>
public sealed record ProxyConfiguration(bool Enabled, string Host, int Port, string Username, string Password, bool PasswordUnavailable = false)
{
    public bool IsValid => !Enabled || (!string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535);
}
