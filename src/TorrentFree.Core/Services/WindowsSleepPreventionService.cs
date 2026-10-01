using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace TorrentFree.Services;

/// <summary>A process-owned Windows power request, independent of the calling thread.</summary>
public sealed class WindowsSleepPreventionService : ISleepPreventionService, IDisposable
{
    private const int PowerRequestSystemRequired = 1;
    private const uint PowerRequestContextSimpleString = 1;
    private readonly object _gate = new();
    private readonly Func<IDisposable?> _acquireRequest;
    private IDisposable? _request;
    private bool _failed;
    private bool _disposed;

    public WindowsSleepPreventionService()
    {
        var reason = LocalizationResourceManager.Instance["PreventSleepWhileDownloadingLabel"];
        _acquireRequest = () => CreateRequest(reason);
    }

    internal WindowsSleepPreventionService(Func<IDisposable?> acquireRequest)
        => _acquireRequest = acquireRequest;

    internal bool IsHeld
    {
        get { lock (_gate) return _request is not null; }
    }

    public void SetPreventSleep(bool preventSleep)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!preventSleep)
            {
                ReleaseRequest();
                _failed = false;
                return;
            }
            if (_request is not null || _failed) return;

            try
            {
                _request = _acquireRequest();
                _failed = _request is null;
            }
            catch (Exception ex)
            {
                _failed = true;
                System.Diagnostics.Debug.WriteLine($"Windows sleep prevention error: {ex}");
            }
        }
    }

    private static IDisposable? CreateRequest(string reasonText)
    {
        if (!OperatingSystem.IsWindows()) return null;
        SafeFileHandle? pending = null;
        try
        {
            var reason = Marshal.StringToHGlobalUni(reasonText);
            try
            {
                var context = new ReasonContext { Version = 0, Flags = PowerRequestContextSimpleString, SimpleReasonString = reason };
                pending = PowerCreateRequest(ref context);
            }
            finally { Marshal.FreeHGlobal(reason); }

            if (pending.IsInvalid || !PowerSetRequest(pending, PowerRequestSystemRequired))
            {
                System.Diagnostics.Debug.WriteLine($"Windows sleep prevention failed: {new Win32Exception(Marshal.GetLastWin32Error())}");
                return null;
            }
            var request = new NativeRequest(pending);
            pending = null;
            return request;
        }
        finally { pending?.Dispose(); }
    }

    private void ReleaseRequest()
    {
        var request = _request;
        _request = null;
        if (request is null) return;
        try
        {
            request.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows power request release error: {ex}");
        }
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
    internal struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
        public uint LocalizedReasonId;
        public uint ReasonStringCount;
        public nint ReasonStrings;
    }

    [SupportedOSPlatform("windows")]
    private sealed class NativeRequest(SafeFileHandle handle) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (!PowerClearRequest(handle, PowerRequestSystemRequired))
                    System.Diagnostics.Debug.WriteLine($"Windows power request release failed: {new Win32Exception(Marshal.GetLastWin32Error())}");
            }
            finally { handle.Dispose(); }
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafeFileHandle request, int requestType);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(SafeFileHandle request, int requestType);
}
