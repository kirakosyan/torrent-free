using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace TorrentFree.Services;

/// <summary>A process-owned Windows power request, independent of the calling thread.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSleepPreventionService : ISleepPreventionService, IDisposable
{
    private const int PowerRequestSystemRequired = 1;
    private readonly object _gate = new();
    private SafeFileHandle? _request;
    private bool _disposed;

    public void SetPreventSleep(bool preventSleep)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!preventSleep)
            {
                ReleaseRequest();
                return;
            }
            if (_request is not null) return;

            SafeFileHandle? pending = null;
            try
            {
                var reason = Marshal.StringToHGlobalUni(LocalizationResourceManager.Instance["PreventSleepWhileDownloadingLabel"]);
                try
                {
                    var context = new ReasonContext { Version = 0, Flags = 1, SimpleReasonString = reason };
                    pending = PowerCreateRequest(ref context);
                }
                finally { Marshal.FreeHGlobal(reason); }

                if (pending.IsInvalid || !PowerSetRequest(pending, PowerRequestSystemRequired))
                {
                    System.Diagnostics.Debug.WriteLine($"Windows sleep prevention failed: {new Win32Exception(Marshal.GetLastWin32Error())}");
                    return;
                }
                _request = pending;
                pending = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows sleep prevention error: {ex}");
            }
            finally { pending?.Dispose(); }
        }
    }

    private void ReleaseRequest()
    {
        var request = _request;
        _request = null;
        if (request is null) return;
        try
        {
            if (!PowerClearRequest(request, PowerRequestSystemRequired))
                System.Diagnostics.Debug.WriteLine($"Windows power request release failed: {new Win32Exception(Marshal.GetLastWin32Error())}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows power request release error: {ex}");
        }
        finally { request.Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseRequest();
        }
    }

    // The simple string occupies the first pointer in REASON_CONTEXT's union. Reserve
    // the rest of its Detailed form so the native layout is correct on x86, x64 and ARM64.
    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
        public uint LocalizedReasonId;
        public uint ReasonStringCount;
        public nint ReasonStrings;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafeFileHandle request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(SafeFileHandle request, int requestType);
}
