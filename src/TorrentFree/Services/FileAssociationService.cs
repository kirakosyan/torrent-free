#if WINDOWS
using Microsoft.Win32;

namespace TorrentFree.Services;

public sealed class FileAssociationService : IFileAssociationService
{
    private const string Extension = ".torrent";
    private const string ProgId = "TorrentFree.Torrent";

    public bool IsSupported => true;

    public Task<bool> IsAssociatedAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{Extension}");
            var current = key?.GetValue(string.Empty) as string;
            return Task.FromResult(string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> AssociateAsync()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return Task.FromResult(false);
            }

            using var extKey = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{Extension}");
            extKey?.SetValue(string.Empty, ProgId);

            using var progKey = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{ProgId}");
            progKey?.SetValue(string.Empty, "TorrentFree Torrent");

            using var iconKey = progKey?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(string.Empty, $"\"{exePath}\",0");

            using var commandKey = progKey?.CreateSubKey("shell\\open\\command");
            commandKey?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemoveAssociationAsync()
    {
        try
        {
            using var extKey = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{Extension}", writable: true);
            var current = extKey?.GetValue(string.Empty) as string;
            if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
            {
                Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{Extension}", throwOnMissingSubKey: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{ProgId}", throwOnMissingSubKey: false);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
#else
namespace TorrentFree.Services;

public sealed class FileAssociationService : IFileAssociationService
{
    public bool IsSupported => false;

    public Task<bool> IsAssociatedAsync() => Task.FromResult(false);

    public Task<bool> AssociateAsync() => Task.FromResult(false);

    public Task<bool> RemoveAssociationAsync() => Task.FromResult(false);
}
#endif
